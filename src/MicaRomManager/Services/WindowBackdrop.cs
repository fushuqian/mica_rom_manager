using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace MicaRomManager.Services;

/// <summary>Win11 DWM 系统背景（Mica/Acrylic）与圆角；低版本自动回退纯色。</summary>
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

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
            return;
        }

        window.SourceInitialized += (_, _) => ApplyCore(window, kind);
        // WindowChrome 会在 SourceInitialized 之后接管非客户区，Loaded 再补一次，避免 backdrop 被覆盖
        window.Loaded += (_, _) =>
        {
            ApplyCore(window, kind);
            window.Dispatcher.BeginInvoke(() => ApplyCore(window, kind),
                System.Windows.Threading.DispatcherPriority.Loaded);
        };
    }

    private static void ApplyCore(Window window, BackdropKind kind)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // WPF 的合成目标默认清成黑色；必须设为透明，DWM 的 Mica 才能透出来
        try
        {
            var source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget != null)
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
        }
        catch
        {
            // 某些渲染后端不支持时忽略，仍尝试 DWM backdrop
        }

        var build = Environment.OSVersion.Version.Build;
        var applied = false;

        try
        {
            if (build >= 22621)
            {
                var corner = DwmcpRound;
                DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));

                var backdrop = (int)kind;
                applied = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) == 0;
            }

            // 跟随系统深色模式，否则深色系统上会出现刺眼的浅色标题栏
            var dark = IsSystemDark() ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        }
        catch
        {
            applied = false;
        }

        if (!applied)
        {
            window.Background = new SolidColorBrush(IsSystemDark()
                ? Color.FromRgb(0x1A, 0x1A, 0x1A)
                : Color.FromRgb(0xF4, 0xF6, 0xF9));
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
