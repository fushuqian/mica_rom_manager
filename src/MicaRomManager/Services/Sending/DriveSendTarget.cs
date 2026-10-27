using System.IO;
using MicaRomManager.Models;
using MicaRomManager.Services.Devices;

namespace MicaRomManager.Services.Sending;

/// <summary>按设备配置把文件复制到盘符设备；只读源文件。</summary>
public sealed class DriveSendTarget
{
    public SendResult Send(
        IReadOnlyList<SendFile> files,
        DeviceProfile profile,
        string driveRoot,
        ConflictPolicy policy,
        IProgress<SendProgress>? progress = null,
        CancellationToken token = default)
    {
        var warnings = new List<string>();
        var copied = 0;
        var skipped = 0;
        var failed = 0;
        long bytes = 0;

        for (var index = 0; index < files.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var file = files[index];
            var fileName = Path.GetFileName(file.SourcePath);
            progress?.Report(new SendProgress(index + 1, files.Count, fileName));

            if (!File.Exists(file.SourcePath))
            {
                failed++;
                warnings.Add($"「{file.Title}」的 ROM 文件不存在：{file.SourcePath}");
                continue;
            }

            if (!profile.AllowUnmappedPlatforms && !SendPathBuilder.IsPlatformMapped(profile, file.Platform))
            {
                skipped++;
                warnings.Add($"「{file.Title}」的平台「{file.Platform}」不在设备「{profile.Name}」支持列表，已跳过。");
                continue;
            }

            try
            {
                var target = SendPathBuilder.ResolveTargetFile(profile, driveRoot, new Game
                {
                    Title = file.Title,
                    Platform = file.Platform,
                    ReleaseYear = file.Year,
                }, file.SourcePath);

                if (File.Exists(target) && policy == ConflictPolicy.Skip)
                {
                    skipped++;
                    continue;
                }

                var directory = Path.GetDirectoryName(target);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

                File.Copy(file.SourcePath, target, true);
                copied++;
                bytes += new FileInfo(file.SourcePath).Length;
            }
            catch (Exception ex)
            {
                failed++;
                warnings.Add($"「{file.Title}」发送失败：{ex.Message}");
            }
        }

        return new SendResult(copied, skipped, failed, bytes, Path.GetFullPath(driveRoot), warnings);
    }
}
