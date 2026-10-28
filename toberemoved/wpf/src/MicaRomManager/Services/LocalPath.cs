using System.IO;

namespace MicaRomManager.Services;

/// <summary>
/// 便携路径存储：位于数据目录之下的绝对路径统一存为 <c>%DATA%\...</c>，
/// 换盘符或整体搬移程序目录后仍可用。
/// </summary>
public static class LocalPath
{
    public const string DataToken = "%DATA%";

    public static string ToStored(string absolutePath)
    {
        var dataDir = AppServices.Paths?.DataDir;
        return string.IsNullOrWhiteSpace(dataDir) ? absolutePath : ToStored(absolutePath, dataDir);
    }

    public static string ToStored(string absolutePath, string dataDir)
    {
        if (string.IsNullOrWhiteSpace(absolutePath)) return absolutePath;

        string full;
        try
        {
            full = Path.GetFullPath(absolutePath);
        }
        catch
        {
            return absolutePath;
        }

        var root = NormalizeRoot(dataDir);
        if (string.IsNullOrEmpty(root)) return absolutePath;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return absolutePath;

        var relative = full[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return relative.Length == 0 ? DataToken : DataToken + Path.DirectorySeparatorChar + relative;
    }

    public static string Expand(string stored)
    {
        var dataDir = AppServices.Paths?.DataDir;
        return string.IsNullOrWhiteSpace(dataDir) ? stored : Expand(stored, dataDir);
    }

    public static string Expand(string stored, string dataDir)
    {
        if (string.IsNullOrWhiteSpace(stored)) return stored;
        if (!stored.StartsWith(DataToken, StringComparison.OrdinalIgnoreCase)) return stored;

        var relative = stored[DataToken.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return relative.Length == 0 ? Path.GetFullPath(dataDir) : Path.GetFullPath(Path.Combine(dataDir, relative));
    }

    public static bool IsUnderDataDir(string absolutePath)
    {
        var dataDir = AppServices.Paths?.DataDir;
        return !string.IsNullOrWhiteSpace(dataDir) && IsUnderDataDir(absolutePath, dataDir);
    }

    public static bool IsUnderDataDir(string absolutePath, string dataDir)
    {
        if (string.IsNullOrWhiteSpace(absolutePath)) return false;

        try
        {
            var full = Path.GetFullPath(absolutePath);
            var root = NormalizeRoot(dataDir);
            return root.Length > 0 && full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>数据目录规范化为「末尾带分隔符」的形式，保证前缀边界判断正确。</summary>
    private static string NormalizeRoot(string dataDir)
    {
        if (string.IsNullOrWhiteSpace(dataDir)) return "";

        var full = Path.GetFullPath(dataDir);
        return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
    }
}
