using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MicaRomManager.Data;
using MicaRomManager.Models;

namespace MicaRomManager.Services;

public sealed record OrganizeResult(int Renamed, int Skipped, int Failed, List<string> FailedFiles);

/// <summary>
/// 子库维护：按标题重命名库内文件、清理文件已不存在的条目。
/// </summary>
public static class LibraryOrganizer
{
    private const int MaxTitleLength = 150;

    /// <summary>
    /// 生成目标文件名：多碟才带 Disc 号；区域/语言各自非空才带括号。
    /// 例：新鬼武者 Disc-1 (欧版)(英语).chd
    /// </summary>
    public static string BuildFileName(Game game, int discIndex, string sourcePath)
    {
        if (game == null || string.IsNullOrWhiteSpace(sourcePath)) return "";

        var title = SanitizeTitle(game.Title);
        if (title.Length == 0) return "";

        var builder = new StringBuilder(title);
        if (game.Discs.Count > 1) builder.Append(" Disc-").Append(discIndex + 1);

        var region = JoinValues(game.Region);
        var language = JoinValues(game.Language);
        var suffix = "";
        if (region.Length > 0) suffix += $"({region})";
        if (language.Length > 0) suffix += $"({language})";
        if (suffix.Length > 0) builder.Append(' ').Append(suffix);

        return builder + Path.GetExtension(sourcePath);
    }

    /// <summary>删除传入记录中所有碟片都不存在的条目（无 RomPath 的不动）。</summary>
    public static int RemoveMissing(GameRepository repo, IEnumerable<Game> games)
    {
        var removed = 0;
        foreach (var game in games)
        {
            if (string.IsNullOrWhiteSpace(game.RomPath)) continue;

            var discs = game.Discs;
            if (discs.Count > 0 && discs.All(d => !File.Exists(d)))
            {
                repo.Delete(game.Id);
                removed++;
            }
        }

        return removed;
    }

    /// <summary>
    /// 只重命名位于 platformDir 内的碟片文件；链接入库（库外）的文件不动。
    /// </summary>
    public static OrganizeResult Organize(GameRepository repo, IEnumerable<Game> games, string platformDir)
    {
        var renamed = 0;
        var skipped = 0;
        var failed = 0;
        var failedFiles = new List<string>();

        foreach (var game in games)
        {
            var paths = game.Discs.ToList();
            if (paths.Count == 0)
            {
                skipped++;
                continue;
            }

            var newPaths = new List<string>(paths);
            var changed = false;

            for (var index = 0; index < paths.Count; index++)
            {
                var source = paths[index];
                if (!File.Exists(source) || !IsUnderDirectory(source, platformDir)) continue;

                var directory = Path.GetDirectoryName(source);
                if (string.IsNullOrEmpty(directory)) continue;

                var fileName = BuildFileName(game, index, source);
                if (fileName.Length == 0) continue;

                var target = Path.Combine(directory, fileName);
                if (string.Equals(target, source, StringComparison.OrdinalIgnoreCase)) continue;

                target = ResolveUniqueName(target, source);
                if (string.Equals(target, source, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    File.Move(source, target);
                    newPaths[index] = target;
                    changed = true;
                    renamed++;
                }
                catch
                {
                    failed++;
                    failedFiles.Add(Path.GetFileName(source));
                }
            }

            if (!changed)
            {
                skipped++;
                continue;
            }

            game.RomPath = newPaths[0];
            game.DiscPaths = string.Join("\n", newPaths.Skip(1).Where(p => !string.IsNullOrWhiteSpace(p)));
            try
            {
                repo.Update(game);
            }
            catch
            {
                failed++;
                failedFiles.Add(Path.GetFileName(newPaths[0]));
            }
        }

        return new OrganizeResult(renamed, skipped, failed, failedFiles);
    }

    public static bool IsUnderDirectory(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory)) return false;

        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       + Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string SanitizeTitle(string? raw)
    {
        var text = raw?.Trim() ?? "";
        if (text.Length == 0) return "";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = text.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var cleaned = Regex.Replace(new string(chars), @"\s+", " ").Trim().TrimEnd('.');
        if (cleaned.Length == 0) return "";
        if (cleaned.Length > MaxTitleLength) cleaned = cleaned[..MaxTitleLength].TrimEnd();
        return cleaned;
    }

    private static string JoinValues(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var invalid = Path.GetInvalidFileNameChars();
        var parts = raw.Split(new[] { ',', '，', '、', ';', '；' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => new string(p.Select(c => invalid.Contains(c) ? '_' : c).ToArray()))
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return string.Join(",", parts);
    }

    private static string ResolveUniqueName(string target, string source)
    {
        if (!File.Exists(target)) return target;

        var directory = Path.GetDirectoryName(target) ?? "";
        var name = Path.GetFileNameWithoutExtension(target);
        var extension = Path.GetExtension(target);

        for (var index = 2; index < 1000; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}){extension}");
            if (!File.Exists(candidate) || string.Equals(candidate, source, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return target;
    }
}
