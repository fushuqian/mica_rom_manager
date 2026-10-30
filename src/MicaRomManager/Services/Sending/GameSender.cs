using System.Diagnostics;
using System.IO;
using MicaRomManager.Models;
using MicaRomManager.Services.Devices;

namespace MicaRomManager.Services.Sending;

public sealed record GameSendProgress(long CopiedBytes, long TotalBytes, int FileIndex, int FileCount, string FileName);

public sealed record GameSendOutcome(
    int Copied,
    int Skipped,
    int Failed,
    long Bytes,
    List<string> Messages,
    string TargetFolder);

/// <summary>按设备连接发送单个游戏的全部碟片，支持字节级进度回调。</summary>
public static class GameSender
{
    private const int BufferSize = 1024 * 1024;

    public static GameSendOutcome Send(
        Game game,
        DeviceConnection connection,
        Action<GameSendProgress>? progress = null,
        CancellationToken token = default,
        Func<string, long>? freeSpaceProbe = null)
    {
        var messages = new List<string>();

        if (!connection.Profile.AllowUnmappedPlatforms &&
            !SendPathBuilder.IsPlatformMapped(connection.Profile, game.Platform))
        {
            messages.Add($"平台「{game.Platform}」不在设备「{connection.Profile.Name}」的支持列表");
            return new GameSendOutcome(0, 1, 0, 0, messages, "");
        }

        var discs = game.Discs.Count > 0 ? game.Discs : new[] { game.RomPath };
        var files = discs.Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        if (files.Count == 0)
        {
            messages.Add("没有可发送的 ROM 文件");
            return new GameSendOutcome(0, 0, 1, 0, messages, "");
        }

        var totalBytes = files.Sum(SafeSize);

        // 发送前先看空间：不足时一个字节都不拷，避免写到一半留下半截文件。
        var freeBytes = (freeSpaceProbe ?? DefaultFreeSpace)(connection.Drive.RootPath);
        if (totalBytes > 0 && freeBytes >= 0 && freeBytes < totalBytes)
        {
            messages.Add(
                $"设备空间不足：需要 {Game.FormatSize(totalBytes)}，可用 {Game.FormatSize(freeBytes)}。请清理后再发送。");
            return new GameSendOutcome(0, 0, files.Count, 0, messages, "");
        }

        var copied = 0;
        var skipped = 0;
        var failed = 0;
        long copiedBytes = 0;
        var targetFolder = "";

        for (var index = 0; index < files.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var source = files[index];
            var fileName = Path.GetFileName(source);

            if (!File.Exists(source))
            {
                failed++;
                messages.Add($"文件不存在：{fileName}");
                continue;
            }

            try
            {
                var target = SendPathBuilder.ResolveTargetFile(
                    connection.Profile, connection.Drive.RootPath, game, source);
                targetFolder = Path.GetDirectoryName(target) ?? targetFolder;

                if (File.Exists(target) && !connection.OverwriteSameName)
                {
                    skipped++;
                    messages.Add($"已存在，跳过：{fileName}");
                    continue;
                }

                var directory = Path.GetDirectoryName(target);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

                var baseBytes = copiedBytes;
                try
                {
                    CopyWithProgress(source, target, token, copied =>
                    {
                        copiedBytes = baseBytes + copied;
                        progress?.Invoke(new GameSendProgress(copiedBytes, totalBytes, index + 1, files.Count, fileName));
                    });
                }
                catch
                {
                    // 中途失败或取消：删掉写了一半的目标文件，别把半截 ROM 留在设备上
                    TryDeleteFile(target);
                    throw;
                }

                copied++;
            }
            catch (Exception ex)
            {
                failed++;
                messages.Add($"{fileName}：{ex.Message}");
            }
        }

        if (progress != null && totalBytes > 0)
            progress(new GameSendProgress(copiedBytes, totalBytes, files.Count, files.Count, ""));

        return new GameSendOutcome(copied, skipped, failed, copiedBytes, messages, targetFolder);
    }

    private static void CopyWithProgress(string source, string target, CancellationToken token, Action<long> onBytes)
    {
        var reporter = new ThrottledReporter(onBytes);
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
        using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize);

        var buffer = new byte[BufferSize];
        long copied = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            output.Write(buffer, 0, read);
            copied += read;
            reporter.Report(copied);
        }

        reporter.Flush(copied);
    }

    private static long SafeSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>读取目标盘可用空间；探测失败返回 -1（视为未知，不阻止发送）。</summary>
    private static long DefaultFreeSpace(string root)
    {
        try
        {
            var path = Path.GetPathRoot(Path.GetFullPath(root));
            if (string.IsNullOrWhiteSpace(path)) return -1;
            return new DriveInfo(path).AvailableFreeSpace;
        }
        catch
        {
            return -1;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 清理失败不掩盖原始错误
        }
    }

    /// <summary>进度回调节流：最多每 150ms 通知一次，避免大文件复制时刷爆 UI。</summary>
    private sealed class ThrottledReporter
    {
        private readonly Action<long> _report;
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private long _lastValue;

        public ThrottledReporter(Action<long> report) => _report = report;

        public void Report(long value)
        {
            _lastValue = value;
            if (_watch.ElapsedMilliseconds < 150) return;
            _watch.Restart();
            _report(value);
        }

        public void Flush(long value)
        {
            if (value == _lastValue) _report(value);
        }
    }
}
