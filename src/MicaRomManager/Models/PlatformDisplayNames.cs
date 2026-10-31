using MicaRomManager.Services;

namespace MicaRomManager.Models;

/// <summary>主机命名习惯：日式（Famicom / Mega Drive）或欧美（NES / Genesis）。</summary>
public enum PlatformNaming
{
    Japanese,
    Western,
}

/// <summary>
/// 平台缩写 → 展示名（如 PS1 → PlayStation）；库里仍存短名，只影响界面显示。
/// 同时负责「厂商」归类与日式/欧美叫法切换（FC/NES、SFC/SNES、PCE、MD）。
/// </summary>
public static class PlatformDisplayNames
{
    public const string NamingKey = "ui.platform_naming";

    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Vendors = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> VendorList = new();

    /// <summary>日式 / 欧美叫法不同的主机；不在表里的走预置清单的展示名。</summary>
    private static readonly Dictionary<string, (string Japanese, string Western)> Regional =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["NES"] = ("Famicom", "NES"),
            ["SNES"] = ("Super Famicom", "SNES"),
            ["PCE"] = ("PC Engine", "TurboGrafx-16"),
            ["PCECD"] = ("PC Engine CD", "TurboGrafx-CD"),
            ["MD"] = ("Mega Drive", "Genesis"),
        };

    public static PlatformNaming Naming { get; private set; } = PlatformNaming.Japanese;

    /// <summary>厂商顺序（按预置清单出现的先后）。</summary>
    public static IReadOnlyList<string> VendorsInOrder => VendorList;

    public static void Initialize(IEnumerable<PlatformPreset> presets)
    {
        Map.Clear();
        Vendors.Clear();
        VendorList.Clear();

        foreach (var preset in presets)
        {
            var vendor = string.IsNullOrWhiteSpace(preset.Vendor) ? "其他" : preset.Vendor.Trim();
            if (!VendorList.Contains(vendor, StringComparer.OrdinalIgnoreCase)) VendorList.Add(vendor);

            foreach (var console in preset.Consoles)
            {
                if (string.IsNullOrWhiteSpace(console)) continue;
                Vendors[console.Trim()] = vendor;
            }

            foreach (var pair in preset.Display)
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) continue;
                Map[pair.Key.Trim()] = string.IsNullOrWhiteSpace(pair.Value) ? pair.Key.Trim() : pair.Value.Trim();
            }
        }
    }

    public static void SetNaming(PlatformNaming naming) => Naming = naming;

    public static PlatformNaming ParseNaming(string? stored) =>
        string.Equals(stored, "west", StringComparison.OrdinalIgnoreCase) ? PlatformNaming.Western : PlatformNaming.Japanese;

    public static string StoreNaming(PlatformNaming naming) => naming == PlatformNaming.Western ? "west" : "jp";

    public static string Of(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return "";
        var name = platform.Trim();

        if (Regional.TryGetValue(name, out var pair))
            return Naming == PlatformNaming.Western ? pair.Western : pair.Japanese;

        return Map.TryGetValue(name, out var display) ? display : platform;
    }

    /// <summary>所属厂商；不在预置清单里的算「自定义」（旧库里的自定义平台）。</summary>
    public static string VendorOf(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return "";
        return Vendors.TryGetValue(platform.Trim(), out var vendor) ? vendor : "自定义";
    }

    /// <summary>是不是预置清单里的主机（界面用「自定义」标记的反面）。</summary>
    public static bool IsKnown(string? platform) =>
        !string.IsNullOrWhiteSpace(platform) && Vendors.ContainsKey(platform.Trim());

    /// <summary>厂商排序权重：预置清单顺序 → 自定义最后。</summary>
    public static int VendorRank(string? platform)
    {
        var vendor = VendorOf(platform);
        var index = VendorList.FindIndex(item => string.Equals(item, vendor, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : int.MaxValue;
    }
}
