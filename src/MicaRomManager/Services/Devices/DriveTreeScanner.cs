using System.IO;
using MicaRomManager.Models;

namespace MicaRomManager.Services.Devices;

/// <summary>
/// 只读扫描设备目录结构：最多 3 层、400 个目录；只统计扩展名，不读文件名与内容。
/// </summary>
public static class DriveTreeScanner
{
    private const int MaxDepth = 3;
    private const int MaxNodes = 400;

    private static readonly HashSet<string> SkipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "$RECYCLE.BIN", "System Volume Information", "Recovery", "Config.Msi",
        "Windows", "Program Files", "Program Files (x86)", "ProgramData",
    };

    public static Task<DeviceScanSummary> ScanAsync(string root, IProgress<string>? progress,
        CancellationToken token) =>
        Task.Run(() => Scan(root, progress, token), token);

    private static DeviceScanSummary Scan(string root, IProgress<string>? progress, CancellationToken token)
    {
        var fullRoot = Path.GetFullPath(root);
        var driveRoot = Path.GetPathRoot(fullRoot) ?? fullRoot;
        var drive = new DriveInfo(driveRoot);
        var nodes = new List<ScanNode>();
        var skipped = 0;
        var pending = new Queue<(string path, string relative, int depth)>();
        pending.Enqueue((fullRoot, "", 0));

        while (pending.Count > 0 && nodes.Count < MaxNodes)
        {
            token.ThrowIfCancellationRequested();
            var (path, relative, depth) = pending.Dequeue();
            var files = CountFiles(path, ref skipped);
            if (files != null)
            {
                nodes.Add(new ScanNode
                {
                    RelPath = relative.Length == 0 ? "(根目录)" : relative,
                    FileCount = files.Value.count,
                    TopExtensions = files.Value.extensions,
                });
            }

            if (depth >= MaxDepth) continue;

            try
            {
                foreach (var dir in Directory.EnumerateDirectories(path))
                {
                    token.ThrowIfCancellationRequested();
                    if (nodes.Count + pending.Count >= MaxNodes) break;

                    var name = Path.GetFileName(dir);
                    if (SkipNames.Contains(name)) continue;

                    try
                    {
                        var attributes = File.GetAttributes(dir);
                        if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    }
                    catch
                    {
                        skipped++;
                        continue;
                    }

                    var childRelative = relative.Length == 0 ? name : Path.Combine(relative, name);
                    pending.Enqueue((dir, childRelative, depth + 1));
                }
            }
            catch
            {
                skipped++;
            }

            if (nodes.Count % 50 == 0)
                progress?.Report($"已扫描 {nodes.Count} 个目录…");
        }

        return new DeviceScanSummary
        {
            DriveRoot = fullRoot,
            VolumeLabel = SafeVolumeLabel(drive),
            TotalBytes = SafeTotalSize(drive),
            FreeBytes = SafeFreeSize(drive),
            Nodes = nodes,
            SkippedDirectories = skipped,
        };
    }

    private static (int count, List<string> extensions)? CountFiles(string path, ref int skipped)
    {
        try
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var count = 0;
            foreach (var file in Directory.EnumerateFiles(path))
            {
                var extension = Path.GetExtension(file);
                if (extension.Length == 0) extension = "(无扩展名)";
                counts[extension] = counts.TryGetValue(extension, out var current) ? current + 1 : 1;
                count++;
                if (count >= 2000) break; // 单个目录只统计前 2000 个文件，避免卡死
            }

            var top = counts.OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .Take(5)
                .Select(pair => $"{pair.Key}×{pair.Value}")
                .ToList();
            return (count, top);
        }
        catch
        {
            skipped++;
            return null;
        }
    }

    private static string SafeVolumeLabel(DriveInfo drive)
    {
        try
        {
            return drive.VolumeLabel;
        }
        catch
        {
            return "";
        }
    }

    private static long SafeTotalSize(DriveInfo drive)
    {
        try
        {
            return drive.TotalSize;
        }
        catch
        {
            return 0;
        }
    }

    private static long SafeFreeSize(DriveInfo drive)
    {
        try
        {
            return drive.AvailableFreeSpace;
        }
        catch
        {
            return 0;
        }
    }
}
