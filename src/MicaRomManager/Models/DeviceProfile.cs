using System.Text.Json.Serialization;

namespace MicaRomManager.Models;

/// <summary>
/// 发送设备配置：传输方式 + 品牌型号 + 盘内目录结构。
/// 内置配置在 Resources\DeviceProfiles.json，用户配置在 data\devices\*.json。
/// </summary>
public sealed class DeviceProfile
{
    /// <summary>唯一标识；保存时为空会自动生成。</summary>
    public string Id { get; set; } = "";

    /// <summary>显示名，如「TrimUI Smart Pro」。</summary>
    public string Name { get; set; } = "";

    public string Brand { get; set; } = "";

    public string Model { get; set; } = "";

    /// <summary>传输方式：drive = 盘符设备；mtp = 安卓 MTP（暂未支持）。</summary>
    public string Transport { get; set; } = "drive";

    /// <summary>盘内根目录，如 Roms。</summary>
    public string RootFolder { get; set; } = "Roms";

    /// <summary>目录模板，支持 {root}、{platform}、{title}、{year}。</summary>
    public string FolderTemplate { get; set; } = "{root}/{platform}";

    /// <summary>为 false 时，映射表外的平台不发送（用于限定设备支持的系统）。</summary>
    public bool AllowUnmappedPlatforms { get; set; } = true;

    /// <summary>平台名 → 设备目录名，如 PlayStation → PS。</summary>
    public Dictionary<string, string> PlatformFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string Notes { get; set; } = "";

    [JsonIgnore]
    public bool IsBuiltIn { get; set; }

    [JsonIgnore]
    public bool IsDrive => string.Equals(Transport, "drive", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string KindDisplay => IsDrive ? "盘符设备" : "MTP（暂未支持）";

    [JsonIgnore]
    public string SourceDisplay => IsBuiltIn ? "内置" : "自定义";

    [JsonIgnore]
    public string Display
    {
        get
        {
            var maker = $"{Brand} {Model}".Trim();
            return string.IsNullOrWhiteSpace(maker) ? Name : $"{Name}（{maker}）";
        }
    }
}
