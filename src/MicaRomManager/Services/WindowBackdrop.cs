using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

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

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

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

            // 应用内部统一浅色，避免系统深色模式把 Mica 变暗后与文字冲突
            var light = 0;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref light, sizeof(int));

            var margins = new Margins { Left = -1, Top = -1, Right = -1, Bottom = -1 };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);
        }
        catch
        {
            applied = false;
        }

        if (!applied)
        {
            window.Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF9));
        }
    }
}
