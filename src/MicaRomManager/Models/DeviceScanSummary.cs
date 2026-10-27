namespace MicaRomManager.Models;

public sealed class ScanNode
{
    public string RelPath { get; init; } = "";
    public int FileCount { get; init; }
    public List<string> TopExtensions { get; init; } = new();

    public override string ToString() =>
        $"{RelPath} | 文件 {FileCount} | {(TopExtensions.Count == 0 ? "无文件" : string.Join(" ", TopExtensions))}";
}

/// <summary>设备目录扫描摘要：只有目录名与扩展名统计，不含文件名与文件内容。</summary>
public sealed class DeviceScanSummary
{
    public string DriveRoot { get; init; } = "";
    public string VolumeLabel { get; init; } = "";
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public List<ScanNode> Nodes { get; init; } = new();
    public int SkippedDirectories { get; init; }

    public IEnumerable<string> TopLevelDirectories =>
        Nodes.Where(node => !node.RelPath.Contains('\\') && !node.RelPath.Contains('/'))
            .Select(node => node.RelPath);

    public IEnumerable<string> DirectoryNames =>
        Nodes.Select(node => node.RelPath.Split('\\', '/').Last())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    public string ToPromptText(int maxChars = 6000)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"盘符：{DriveRoot}");
        builder.AppendLine($"卷标：{(VolumeLabel.Length == 0 ? "无" : VolumeLabel)}");
        builder.AppendLine($"容量：{FormatSize(TotalBytes)}，可用：{FormatSize(FreeBytes)}");
        builder.AppendLine($"跳过的无权限目录：{SkippedDirectories}");
        builder.AppendLine("目录 | 文件数 | 扩展名统计");
        foreach (var node in Nodes)
        {
            if (builder.Length + node.ToString().Length + 1 > maxChars)
            {
                builder.AppendLine("（已截断，仅展示前部分目录）");
                break;
            }

            builder.AppendLine(node.ToString());
        }

        return builder.ToString();
    }

    private static string FormatSize(long bytes) =>
        bytes <= 0 ? "0" : $"{bytes / 1024d / 1024d / 1024d:0.#} GB";
}
