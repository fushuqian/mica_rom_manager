using System.IO;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace MicaRomManager.Services;

public sealed class EmulatorInstaller
{
    public sealed record InstallResult(string InstallDir, IReadOnlyList<string> ExeCandidates);

    private static readonly string[] PenalizedKeywords =
    {
        "uninstall", "setup", "install", "update", "updater", "crash", "report", "vcredist", "helper", "launcher",
    };

    private readonly PathService _paths;

    public EmulatorInstaller(PathService paths) => _paths = paths;

    public InstallResult InstallFromFolder(string sourceDirectory, string emulatorName)
    {
        var target = PrepareTarget(emulatorName);
        CopyDirectory(sourceDirectory, target);
        return new InstallResult(target, FindExeCandidates(target, emulatorName));
    }

    public InstallResult InstallFromZip(string zipPath, string emulatorName)
    {
        var temp = Path.Combine(Path.GetTempPath(), "MicaRomManager-" + Guid.NewGuid().ToString("N"));
        try
        {
            ZipFile.ExtractToDirectory(zipPath, temp, true);
            var source = FindExtractedRoot(temp);
            var target = PrepareTarget(emulatorName);
            CopyDirectory(source, target);
            return new InstallResult(target, FindExeCandidates(target, emulatorName));
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    /// <summary>按扩展名分发：zip 用内置解压，7z / rar / tar.gz 用 SharpCompress。</summary>
    public InstallResult InstallFromArchive(string archivePath, string emulatorName)
    {
        var fileName = Path.GetFileName(archivePath);
        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            return InstallFromSharpCompress(archivePath, emulatorName);
        }

        var extension = Path.GetExtension(archivePath).ToLowerInvariant();
        return extension switch
        {
            ".zip" => InstallFromZip(archivePath, emulatorName),
            ".7z" => InstallFromSharpCompress(archivePath, emulatorName),
            ".rar" => InstallFromSharpCompress(archivePath, emulatorName),
            _ => throw new NotSupportedException("仅支持 zip / 7z / rar / tar.gz 压缩包。"),
        };
    }

    private InstallResult InstallFromSharpCompress(string archivePath, string emulatorName)
    {
        var temp = Path.Combine(Path.GetTempPath(), "MicaRomManager-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            var tempRoot = Path.GetFullPath(temp + Path.DirectorySeparatorChar);

            using (var archive = ArchiveFactory.OpenArchive(archivePath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory || string.IsNullOrEmpty(entry.Key)) continue;

                    // 防止目录穿越：解压后的最终路径必须仍在临时目录内
                    var destination = Path.GetFullPath(Path.Combine(temp, entry.Key));
                    if (!destination.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("压缩包包含非法路径，已中止安装。");
                    }

                    entry.WriteToDirectory(temp, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                }
            }

            var source = FindExtractedRoot(temp);
            var target = PrepareTarget(emulatorName);
            CopyDirectory(source, target);
            return new InstallResult(target, FindExeCandidates(target, emulatorName));
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    /// <summary>同名旧安装目录整体移到 data\emulators\.backup，不直接删除。</summary>
    public string PrepareTarget(string emulatorName)
    {
        var safeName = RomImporter.SanitizeFolderName(emulatorName);
        var target = Path.Combine(_paths.EmulatorsDir, safeName);
        if (Directory.Exists(target))
        {
            Directory.CreateDirectory(_paths.BackupsDir);
            var backup = Path.Combine(_paths.BackupsDir, $"{safeName}-{DateTime.Now:yyyyMMddHHmmss}");
            Directory.Move(target, backup);
        }

        Directory.CreateDirectory(target);
        return target;
    }

    public static List<string> FindExeCandidates(string directory, string emulatorName)
    {
        var result = new List<string>();
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (var file in Directory.EnumerateFiles(directory, "*.exe", options))
            {
                result.Add(file);
            }
        }
        catch
        {
            return result;
        }

        return result
            .Select(path => (Path: path, Score: ScoreExe(path, directory, emulatorName)))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Path)
            .Select(x => x.Path)
            .ToList();
    }

    public static string? PickBestExe(IEnumerable<string> candidates, string emulatorName) =>
        candidates.OrderByDescending(path => ScoreExe(path, Path.GetDirectoryName(path) ?? "", emulatorName)).FirstOrDefault();

    private static int ScoreExe(string path, string root, string emulatorName)
    {
        var fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        var target = new string(emulatorName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var score = 0;

        if (!string.IsNullOrEmpty(target))
        {
            if (fileName.Replace(" ", "").Replace("-", "").Replace("_", "") == target) score += 100;
            else if (fileName.Contains(target)) score += 40;
        }

        if (PenalizedKeywords.Any(keyword => fileName.Contains(keyword))) score -= 200;

        try
        {
            var depth = Path.GetRelativePath(root, path).Count(c => c == Path.DirectorySeparatorChar);
            score += Math.Max(0, 20 - depth * 5);
        }
        catch
        {
            // 忽略路径计算异常
        }

        return score;
    }

    private static string FindExtractedRoot(string tempDirectory)
    {
        var entries = Directory.GetFileSystemEntries(tempDirectory);
        return entries.Length == 1 && Directory.Exists(entries[0]) ? entries[0] : tempDirectory;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true };

        foreach (var file in Directory.EnumerateFiles(source, "*", options))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source, "*", options))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        catch
        {
            // 临时目录清理失败可忽略
        }
    }
}
