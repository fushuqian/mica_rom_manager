namespace MicaRomManager.Models;

/// <summary>平台级默认设置：默认模拟器、库内子文件夹。</summary>
public sealed class PlatformSetting
{
    public string Name { get; set; } = "";
    public long? EmulatorId { get; set; }
    public string Folder { get; set; } = "";
    /// <summary>支持的 ROM 扩展名，空格分隔（如 "iso cue bin chd pbp"）；空表示不限制。</summary>
    public string Formats { get; set; } = "";
    public bool IsCustom { get; set; }
}
