using System.IO;
using System.Text.Json;
using MicaRomManager.Data;
using MicaRomManager.Models;

namespace MicaRomManager.Services;

/// <summary>厂商 → 主机的预置清单：用于「平台」页的两级列表，并在缺项时补进平台表。</summary>
public static class PlatformPresetStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static List<PlatformPreset> Load(PathService paths)
    {
        try
        {
            var userFile = Path.Combine(paths.DataDir, "PlatformPresets.json");
            if (!File.Exists(userFile) && File.Exists(PathService.PlatformPresetsPath))
                File.Copy(PathService.PlatformPresetsPath, userFile, false);

            var file = File.Exists(userFile) ? userFile : PathService.PlatformPresetsPath;
            if (!File.Exists(file)) return new List<PlatformPreset>();

            return JsonSerializer.Deserialize<List<PlatformPreset>>(File.ReadAllText(file), Options)
                   ?? new List<PlatformPreset>();
        }
        catch
        {
            return new List<PlatformPreset>();
        }
    }

    /// <summary>只读随程序发布的清单（不碰 data 副本），用于界面展示名。</summary>
    public static List<PlatformPreset> LoadShipped()
    {
        try
        {
            if (!File.Exists(PathService.PlatformPresetsPath)) return new List<PlatformPreset>();
            return JsonSerializer.Deserialize<List<PlatformPreset>>(
                       File.ReadAllText(PathService.PlatformPresetsPath), Options)
                   ?? new List<PlatformPreset>();
        }
        catch
        {
            return new List<PlatformPreset>();
        }
    }

    /// <summary>把预置清单里还没有的主机补进平台表（幂等）。</summary>
    public static int SeedMissing(PlatformRepository repository, PathService paths)
    {
        var added = 0;
        foreach (var preset in Load(paths))
        {
            foreach (var name in preset.Consoles)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (repository.Get(name) != null) continue;
                repository.EnsureExists(name);
                added++;
            }
        }

        return added;
    }

    /// <summary>按预置清单查厂商；不在清单里的算「自定义」。</summary>
    public static string VendorOf(IEnumerable<PlatformPreset> presets, string platform)
    {
        foreach (var preset in presets)
        {
            if (preset.Consoles.Any(name => string.Equals(name, platform, StringComparison.OrdinalIgnoreCase)))
                return preset.Vendor;
        }

        return "自定义";
    }
}

public sealed class PlatformPreset
{
    public string Vendor { get; set; } = "";
    public List<string> Consoles { get; set; } = new();

    /// <summary>短名 → 展示名；可选。</summary>
    public Dictionary<string, string> Display { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
