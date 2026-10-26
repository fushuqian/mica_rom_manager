using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using MicaRomManager.Models;

namespace MicaRomManager.Services;

public partial class ImportCandidate : ObservableObject
{
    public string FilePath { get; init; } = "";
    public long FileSize { get; init; }
    public bool NeedsSpecify { get; init; }

    [ObservableProperty] private string title = "";
    [ObservableProperty] private string platform = "";

    public string FileName => Path.GetFileName(FilePath);
    public string FileSizeDisplay => Game.FormatSize(FileSize);
}

public static class RomImporter
{
    public sealed record ScanResult(List<ImportCandidate> Candidates, int DuplicateCount, int ExcludedCount);

    public static ScanResult Scan(IEnumerable<string> inputs, bool recursive, IEnumerable<string> existingPaths)
    {
        var existing = new HashSet<string>(existingPaths, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<ImportCandidate>();
        var duplicates = 0;
        var excluded = 0;

        foreach (var input in inputs)
        {
            if (File.Exists(input))
            {
                AddFile(input, true);
            }
            else if (Directory.Exists(input))
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = recursive,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.System,
                };
                foreach (var file in Directory.EnumerateFiles(input, "*", options))
                {
                    if (ExtensionMap.IsKnownOrArchive(file)) AddFile(file, false);
                }
            }
        }

        return new ScanResult(candidates, duplicates, excluded);

        void AddFile(string file, bool explicitSelection)
        {
            if (!ExtensionMap.IsKnownOrArchive(file))
            {
                if (!explicitSelection) return;
                excluded++;
                return;
            }

            var fullPath = Path.GetFullPath(file);
            if (existing.Contains(fullPath) || !seen.Add(fullPath))
            {
                duplicates++;
                return;
            }

            var info = ExtensionMap.Detect(fullPath);
            long size = 0;
            try
            {
                size = new FileInfo(fullPath).Length;
            }
            catch
            {
                // 读不到大小不影响导入
            }

            candidates.Add(new ImportCandidate
            {
                FilePath = fullPath,
                FileSize = size,
                NeedsSpecify = info.NeedsSpecify,
                Title = Path.GetFileNameWithoutExtension(fullPath),
                Platform = info.Platform ?? ExtensionMap.UnspecifiedPlatform,
            });
        }
    }

    /// <summary>复制到库目录并返回新路径；同名文件自动追加序号。</summary>
    public static string CopyToLibrary(string sourceFile, string libraryRoot, string platform)
    {
        var folderName = string.IsNullOrWhiteSpace(platform) ? "未分类" : platform;
        var directory = Path.Combine(libraryRoot, SanitizeFolderName(folderName));
        Directory.CreateDirectory(directory);

        var target = Path.Combine(directory, Path.GetFileName(sourceFile));
        if (File.Exists(target) || string.Equals(target, sourceFile, StringComparison.OrdinalIgnoreCase))
        {
            var name = Path.GetFileNameWithoutExtension(sourceFile);
            var extension = Path.GetExtension(sourceFile);
            var index = 1;
            do
            {
                target = Path.Combine(directory, $"{name} ({index}){extension}");
                index++;
            } while (File.Exists(target));
        }

        File.Copy(sourceFile, target, false);
        return target;
    }

    public static string SanitizeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var result = new string(chars).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(result) ? "未分类" : result;
    }
}
