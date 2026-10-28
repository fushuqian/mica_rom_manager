namespace MicaRomManager.Models;

/// <summary>平台级默认设置：默认模拟器、库内子文件夹。</summary>
public sealed class PlatformSetting
{
    public string Name { get; set; } = "";
    public long? EmulatorId { get; set; }
    public string Folder { get; set; } = "";
    /// <summary>支持的 ROM 扩展名，空格分隔（如 "iso cue bin chd pbp"）；空表示不限制。</summary>
    public string Formats { get; set; } = "";
    /// <summary>平台默认核心（相对模拟器安装目录，如 "cores\pcsx_rearmed_libretro.dll"）；空表示用模拟器自带核心。</summary>
    public string CorePath { get; set; } = "";
    /// <summary>平台级启动参数；空时使用 "{rom}"。</summary>
    public string ArgsTemplate { get; set; } = "";
    public bool IsCustom { get; set; }
}
