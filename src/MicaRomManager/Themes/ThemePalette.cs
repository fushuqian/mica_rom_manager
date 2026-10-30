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
        ["WindowBackgroundBrush"] = Color.FromRgb(0x1A, 0x1A, 0x1A),
        ["SurfaceBrush"] = Color.FromArgb(0xE6, 0x1C, 0x1C, 0x1C),
        ["CardBrush"] = Color.FromArgb(0xB3, 0x1F, 0x1F, 0x1F),
        ["PanelBrush"] = Color.FromArgb(0xFF, 0x1C, 0x1C, 0x1C),
        ["FlyoutBrush"] = Color.FromArgb(0xF5, 0x2A, 0x2A, 0x2A),
        ["InputBrush"] = Color.FromArgb(0xB3, 0x26, 0x26, 0x26),
        ["NavBrush"] = Color.FromArgb(0x80, 0x1C, 0x1C, 0x1C),
        ["BorderBrush"] = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF),
        ["InputBorderBrush"] = Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF),
        ["TextPrimaryBrush"] = Color.FromRgb(0xF2, 0xF2, 0xF2),
        ["TextSecondaryBrush"] = Color.FromRgb(0xB0, 0xB0, 0xB0),
        ["AccentBrush"] = Color.FromRgb(0x4C, 0xC2, 0xFF),
        ["AccentHoverBrush"] = Color.FromRgb(0x6F, 0xD0, 0xFF),
        ["AccentPressedBrush"] = Color.FromRgb(0x2A, 0x9F, 0xD6),
        ["AccentSubtleBrush"] = Color.FromArgb(0x33, 0x4C, 0xC2, 0xFF),
        ["OnAccentTextBrush"] = Color.FromRgb(0x0B, 0x1A, 0x24),
        ["DangerBrush"] = Color.FromRgb(0xFF, 0x99, 0xA4),
        ["WarningBrush"] = Color.FromRgb(0xFC, 0xE1, 0x00),
        ["WarningBackgroundBrush"] = Color.FromArgb(0x33, 0xFC, 0xE1, 0x00),
        ["RowAltBrush"] = Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF),
        ["HoverBrush"] = Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF),
        ["PressedBrush"] = Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF),
        ["SelectionBrush"] = Color.FromArgb(0x33, 0x4C, 0xC2, 0xFF),
        ["GridLineBrush"] = Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF),
        ["ScrollThumbBrush"] = Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF),
        ["ScrollThumbHoverBrush"] = Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF),
        ["CardShadowBrush"] = Color.FromArgb(0x40, 0x00, 0x00, 0x00),
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
