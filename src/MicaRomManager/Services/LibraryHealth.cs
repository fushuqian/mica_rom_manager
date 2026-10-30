using System.IO;
using MicaRomManager.Models;

namespace MicaRomManager.Services;

/// <summary>库存路径健康检查结果：哪些盘当前不可用、有多少条 ROM 文件缺失。</summary>
public sealed record LibraryHealthReport(int TotalGames, int MissingGames, IReadOnlyList<string> UnavailableRoots)
{
    public bool HasUnavailableRoots => UnavailableRoots.Count > 0;

    /// <summary>盘没插好时不能判断「文件真失效」，此时禁止清理记录，避免误删。</summary>
    public bool CanCleanMissing => UnavailableRoots.Count == 0;
}

/// <summary>
/// 只读检查库内 ROM 所在的盘符/共享根是否可用。
/// 目录/文件存在性可注入，便于单测不依赖真实盘符。
/// </summary>
public static class LibraryHealth
{
    public static LibraryHealthReport Inspect(
        IEnumerable<Game> games,
        Func<string, bool>? directoryExists = null,
        Func<string, bool>? fileExists = null)
    {
        directoryExists ??= Directory.Exists;
        fileExists ??= File.Exists;

        var list = games.ToList();
        var roots = new List<string>();
        var missing = 0;

        foreach (var game in list)
        {
            foreach (var path in game.Discs)
                AddRoot(path);

            if (string.IsNullOrWhiteSpace(game.RomPath) || !fileExists(game.RomPath))
                missing++;
        }

        roots.Sort(StringComparer.OrdinalIgnoreCase);
        return new LibraryHealthReport(list.Count, missing, roots);

        void AddRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!Path.IsPathRooted(path)) return;

            string? root;
            try
            {
                root = Path.GetPathRoot(Path.GetFullPath(path));
            }
            catch
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(root)) return;
            if (roots.Contains(root, StringComparer.OrdinalIgnoreCase)) return;
            if (directoryExists(root)) return;
            roots.Add(root);
        }
    }
}
