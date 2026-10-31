using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace MicaRomManager.Services;

/// <summary>
/// 窗口背景：优先用 SetWindowCompositionAttribute 的亚克力（失焦不会掉成灰实色），
/// 老系统回退纯色；同时统一圆角与深色标题栏。
/// </summary>
public static class WindowBackdrop
{
    public enum BackdropKind
    {
        Mica = 2,
        Acrylic = 3,
        Tabbed = 4,
    }

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmcpRound = 2;

    /// <summary>是否用手动亚克力；关掉则用 DWM 系统背景（失焦会在激活回调里重刷）。</summary>
    private static readonly bool UseAcrylic = false;

    private const int WcaAccentPolicy = 19;
    private const int AccentEnableAcrylicBlurBehind = 4;
    private const int AccentFlagDrawAllBorders = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    public static void Apply(Window window, BackdropKind kind = BackdropKind.Mica)
    {
        if (window == null) return;

        try
        {
            window.Background = Brushes.Transparent;
        }
        catch
        {
            // 保持原背景
        }

        if (window.IsLoaded)
        {
            ApplyCore(window, kind);
            HookActivation(window, kind);
            return;
        }

        window.SourceInitialized += (_, _) => ApplyCore(window, kind);
        // WindowChrome 会在 SourceInitialized 之后接管非客户区，Loaded 再补一次，避免 backdrop 被覆盖
        window.Loaded += (_, _) =>
        {
            ApplyCore(window, kind);
            window.Dispatcher.BeginInvoke(() => ApplyCore(window, kind),
                System.Windows.Threading.DispatcherPriority.Loaded);
            HookActivation(window, kind);
        };
    }

    private static bool _hooked;

    /// <summary>失焦时 DWM 会把系统 Mica 降成实色；重新下发布景属性可让它保持原样。</summary>
    private static void HookActivation(Window window, BackdropKind kind)
    {
        if (!_hooked)
        {
            _hooked = true;
            try
            {
                var hwnd = new WindowInteropHelper(window).Handle;
                HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
                {
                    if (msg is 0x0006 or 0x0086) // WM_ACTIVATE / WM_NCACTIVATE
                    {
                        window.Dispatcher.BeginInvoke(() => ApplyCore(window, kind),
                            System.Windows.Threading.DispatcherPriority.Render);
                    }

                    return IntPtr.Zero;
                });
            }
            catch
            {
                // 钩子失败时仍靠 Activated/Deactivated 事件
            }
        }

        window.Activated -= OnActivated;
        window.Deactivated -= OnDeactivated;
        window.Activated += OnActivated;
        window.Deactivated += OnDeactivated;

        void OnActivated(object? sender, EventArgs e) => ApplyCore(window, kind);
        void OnDeactivated(object? sender, EventArgs e) => ApplyCore(window, kind);
    }

    private static void ApplyCore(Window window, BackdropKind kind)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // WPF 的合成目标默认清成黑色；必须设为透明，DWM 的材质才能透出来
        try
        {
            var source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget != null)
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
        }
        catch
        {
            // 某些渲染后端不支持时忽略，仍尝试系统背景
        }

        var build = Environment.OSVersion.Version.Build;
        var dark = IsSystemDark();

        try
        {
            if (build >= 22000)
            {
                var corner = DwmcpRound;
                DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));
            }

            var immersiveDark = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref immersiveDark, sizeof(int));
        }
        catch
        {
            // 忽略：下面还有纯色兜底
        }

        // 亚克力：GradientColor 用 AABBGGRR，透明度略低于纯白，失焦也保持
        if (UseAcrylic && build >= 22000 && EnableAcrylic(hwnd, dark))
            return;

        // 老系统 / 失败：退回 DWM 系统背景，再不行用纯色
        try
        {
            if (build >= 22621)
            {
                var backdrop = (int)kind;
                if (DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) == 0) return;
            }
        }
        catch
        {
            // 继续走纯色
        }

        window.Background = new SolidColorBrush(dark
            ? Color.FromRgb(0x1A, 0x1A, 0x1A)
            : Color.FromRgb(0xF4, 0xF6, 0xF9));
    }

    private static bool EnableAcrylic(IntPtr hwnd, bool dark)
    {
        // ABGR；浅色偏白、深色偏黑，alpha 控制染色强度
        var tint = dark ? unchecked((int)0x4018181D) : unchecked((int)0x40F4F5FA);

        var policy = new AccentPolicy
        {
            AccentState = AccentEnableAcrylicBlurBehind,
            AccentFlags = AccentFlagDrawAllBorders,
            GradientColor = tint,
            AnimationId = 0,
        };

        var size = Marshal.SizeOf<AccentPolicy>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, buffer, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = buffer,
                SizeOfData = size,
            };
            return SetWindowCompositionAttribute(hwnd, ref data) != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static bool IsSystemDark()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return value is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }
}
