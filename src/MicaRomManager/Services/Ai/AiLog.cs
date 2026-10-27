using System.IO;

namespace MicaRomManager.Services.Ai;

/// <summary>AI 调用日志：只记主机、模型与状态码，不写 Key。</summary>
public static class AiLog
{
    private const long MaxBytes = 1024 * 1024;

    public static void Write(string message)
    {
        try
        {
            var dir = AppServices.Paths?.LogsDir;
            if (string.IsNullOrWhiteSpace(dir)) return;

            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "ai.log");
            if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                File.Move(path, path + ".1", true);

            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }
}
