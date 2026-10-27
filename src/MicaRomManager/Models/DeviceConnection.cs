using MicaRomManager.Services.Sending;

namespace MicaRomManager.Models;

/// <summary>本次运行内的设备连接：配置 + 盘符 + 同名策略。</summary>
public sealed class DeviceConnection
{
    public required DeviceProfile Profile { get; init; }
    public required DriveEntry Drive { get; init; }
    public bool OverwriteSameName { get; init; }

    public string TargetRoot { get; init; } = "";

    public string Summary => $"{Profile.Name} · {Drive.RootPath}";

    public string PolicyText => OverwriteSameName ? "同名覆盖" : "同名跳过";
}
