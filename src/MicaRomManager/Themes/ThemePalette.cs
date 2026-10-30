using System.Windows;
using System.Windows.Media;

namespace MicaRomManager.Themes;

/// <summary>
/// 跟随系统浅色/深色切换画刷；画刷对象原地改色，StaticResource 引用会一起更新。
/// </summary>
public static class ThemePalette
{
    public static bool IsDark { get; private set; }

    private static readonly Dictionary<string, Color> Dark = new()
    {
        ["WindowBackgroundBrush"] = Color.FromRgb(0x0B, 0x0B, 0x0D),
        ["SurfaceBrush"] = Color.FromArgb(0xE6, 0x12, 0x12, 0x16),
        ["CardBrush"] = Color.FromArgb(0xB3, 0x1A, 0x1A, 0x1E),
        ["PanelBrush"] = Color.FromArgb(0xFF, 0x14, 0x14, 0x18),
        ["FlyoutBrush"] = Color.FromArgb(0xF5, 0x1E, 0x1E, 0x23),
        ["InputBrush"] = Color.FromArgb(0x66, 0x2A, 0x2A, 0x30),
        ["NavBrush"] = Color.FromArgb(0x80, 0x14, 0x14, 0x18),
        ["BorderBrush"] = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
        ["InputBorderBrush"] = Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF),
        ["HairlineBrush"] = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
        ["ShellPanelBrush"] = Color.FromArgb(0x78, 0x12, 0x12, 0x16),        ["MainPanelBrush"] = Color.FromArgb(0x99, 0x17, 0x17, 0x1B),
        ["ShellScrimBrush"] = Color.FromArgb(0x33, 0x0A, 0x0A, 0x0C),
        ["ShellPanelLiftBrush"] = Color.FromArgb(0x3D, 0x20, 0x20, 0x26),
        ["MainPanelLiftBrush"] = Color.FromArgb(0x4D, 0x2A, 0x2A, 0x31),
        ["TextPrimaryBrush"] = Color.FromRgb(0xF2, 0xF2, 0xF4),
        ["TextSecondaryBrush"] = Color.FromRgb(0x8A, 0x8A, 0x90),
        ["AccentBrush"] = Color.FromRgb(0x0A, 0x84, 0xFF),
        ["AccentHoverBrush"] = Color.FromRgb(0x40, 0x9C, 0xFF),
        ["AccentPressedBrush"] = Color.FromRgb(0x00, 0x60, 0xCC),
        ["AccentSubtleBrush"] = Color.FromArgb(0x24, 0x0A, 0x84, 0xFF),
        ["OnAccentTextBrush"] = Color.FromRgb(0xFF, 0xFF, 0xFF),
        ["DangerBrush"] = Color.FromRgb(0xFF, 0x99, 0xA4),
        ["WarningBrush"] = Color.FromRgb(0xFF, 0xD6, 0x6B),
        ["WarningBackgroundBrush"] = Color.FromArgb(0x33, 0xFC, 0xE1, 0x00),
        ["RowAltBrush"] = Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF),
        ["HoverBrush"] = Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF),
        ["PressedBrush"] = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
        ["SelectionBrush"] = Color.FromArgb(0x1A, 0x0A, 0x84, 0xFF),
        ["GridLineBrush"] = Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF),
        ["ScrollThumbBrush"] = Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF),
        ["ScrollThumbHoverBrush"] = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF),
    };

    public static void Apply()
    {
        IsDark = Services.WindowBackdrop.IsSystemDark();
        if (!IsDark) return;

        foreach (var (key, color) in Dark)
        {
            if (Application.Current.TryFindResource(key) is not SolidColorBrush) continue;
            Application.Current.Resources[key] = new SolidColorBrush(color);
        }
    }
}
