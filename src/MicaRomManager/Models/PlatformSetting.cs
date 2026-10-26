namespace MicaRomManager.Models;

/// <summary>平台级默认设置：默认模拟器、库内子文件夹。</summary>
public sealed class PlatformSetting
{
    public string Name { get; set; } = "";
    public long? EmulatorId { get; set; }
    public string Folder { get; set; } = "";
    public bool IsCustom { get; set; }
}
