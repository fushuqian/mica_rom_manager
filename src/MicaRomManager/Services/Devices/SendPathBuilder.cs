using System.IO;
using MicaRomManager.Models;

namespace MicaRomManager.Services.Devices;

/// <summary>按设备配置把游戏解析成目标目录与文件路径。</summary>
public static class SendPathBuilder
{
    private const string Unclassified = "未分类";

    public static bool IsPlatformMapped(DeviceProfile profile, string platform) =>
        !string.IsNullOrWhiteSpace(platform) &&
        profile.PlatformFolders.Any(pair =>
            string.Equals(pair.Key, platform, StringComparison.OrdinalIgnoreCase));

    public static string ResolvePlatformFolder(DeviceProfile profile, string platform)
    {
        if (string.IsNullOrWhiteSpace(platform)) return Unclassified;

        var mapped = profile.PlatformFolders.FirstOrDefault(pair =>
            string.Equals(pair.Key, platform, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(mapped.Value) ? platform : mapped.Value;
    }

    /// <summary>解析目标目录；模板支持 {root}、{platform}、{title}、{year}。</summary>
    public static string ResolveFolder(DeviceProfile profile, string driveRoot, Game game)
    {
        var driveFull = Path.GetFullPath(driveRoot);
        var rootFolder = SharePackager.SanitizeName(profile.RootFolder);
        var current = string.IsNullOrWhiteSpace(rootFolder) ? driveFull : Path.Combine(driveFull, rootFolder);

        var template = string.IsNullOrWhiteSpace(profile.FolderTemplate)
            ? "{root}/{platform}"
            : profile.FolderTemplate;

        var platform = ResolvePlatformFolder(profile, game.Platform);
        var year = game.ReleaseYear?.ToString() ?? "";

        foreach (var raw in template.Replace('\\', '/')
                     .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.Contains("{root}", StringComparison.OrdinalIgnoreCase))
            {
                var rest = raw.Replace("{root}", "", StringComparison.OrdinalIgnoreCase).Trim();
                if (rest.Length > 0) current = Path.Combine(current, SharePackager.SanitizeName(rest));
                continue;
            }

            var segment = raw
                .Replace("{platform}", platform, StringComparison.OrdinalIgnoreCase)
                .Replace("{title}", game.Title, StringComparison.OrdinalIgnoreCase)
                .Replace("{year}", year, StringComparison.OrdinalIgnoreCase);

            segment = SharePackager.SanitizeName(segment);
            if (segment.Length == 0) continue;

            current = Path.Combine(current, segment);
        }

        var full = Path.GetFullPath(current);
        EnsureInsideDrive(driveFull, full);
        return full;
    }

    public static string ResolveTargetFile(DeviceProfile profile, string driveRoot, Game game, string sourcePath)
    {
        var folder = ResolveFolder(profile, driveRoot, game);
        var fileName = Path.GetFileName(sourcePath);
        var target = Path.GetFullPath(Path.Combine(folder, fileName));
        EnsureInsideDrive(Path.GetFullPath(driveRoot), target);
        return target;
    }

    private static void EnsureInsideDrive(string driveRoot, string target)
    {
        var root = driveRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(target, driveRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"目标路径超出设备根目录：{target}");
        }
    }
}
