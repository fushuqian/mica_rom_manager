using MicaRomManager.Services;

namespace MicaRomManager.Models;

/// <summary>平台缩写 → 展示名（如 PS1 → PlayStation）；库里仍存短名，只影响界面显示。</summary>
public static class PlatformDisplayNames
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase);

    public static void Initialize(IEnumerable<PlatformPreset> presets)
    {
        Map.Clear();
        foreach (var preset in presets)
        {
            foreach (var pair in preset.Display)
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) continue;
                Map[pair.Key.Trim()] = string.IsNullOrWhiteSpace(pair.Value) ? pair.Key.Trim() : pair.Value.Trim();
            }
        }
    }

    public static string Of(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return "";
        return Map.TryGetValue(platform.Trim(), out var display) ? display : platform;
    }
}
