using System.IO;

namespace MicaRomManager.Services;

/// <summary>
/// 便携模式路径：以程序所在目录为基准，把程序目录下的绝对路径存为相对路径，
/// 换盘符或整体搬移程序目录后仍可用。位于数据目录下的路径仍优先存为 <c>%DATA%</c>。
/// </summary>
public static class PortablePath
{
    public const string SettingKey = "library.portable";

    public static bool Enabled => AppServices.Settings?.GetBool(SettingKey) == true;

    public static string Expand(string stored) => Expand(stored, AppContext.BaseDirectory);

    public static string Expand(string stored, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(stored)) return "";

        var expanded = LocalPath.Expand(stored);

        // 数据目录未初始化（如单元测试）时 LocalPath 不会展开 %DATA%，此处保持原样
        if (expanded.StartsWith(LocalPath.DataToken, StringComparison.OrdinalIgnoreCase))
            return expanded;

        if (Path.IsPathRooted(expanded) || string.IsNullOrWhiteSpace(baseDir)) return expanded;

        try
        {
            return Path.GetFullPath(Path.Combine(baseDir, expanded));
        }
        catch
        {
            return expanded;
        }
    }

    public static string ToStored(string absolutePath) => ToStored(absolutePath, Enabled);

    public static string ToStored(string absolutePath, bool portable) =>
        ToStored(absolutePath, portable, AppContext.BaseDirectory);

    public static string ToStored(string absolutePath, bool portable, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(absolutePath)) return "";

        var stored = LocalPath.ToStored(absolutePath);
        if (!portable || !Path.IsPathRooted(stored)) return stored;
        return ToRelative(stored, baseDir);
    }

    public static string ExpandMultiLine(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return "";

        var lines = stored.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("\n", lines.Select(Expand));
    }

    public static string ToStoredMultiLine(string stored, bool portable)
    {
        if (string.IsNullOrWhiteSpace(stored)) return "";

        var lines = stored.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join("\n", lines.Select(line => ToStored(line, portable)));
    }

    private static string ToRelative(string absolutePath, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || string.IsNullOrWhiteSpace(baseDir))
            return absolutePath;

        try
        {
            var full = Path.GetFullPath(absolutePath);
            var root = Path.GetFullPath(baseDir);
            var relative = Path.GetRelativePath(root, full);
            return Path.IsPathRooted(relative) ? absolutePath : relative;
        }
        catch
        {
            return absolutePath;
        }
    }
}
