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
    /// <summary>ROM 内容指纹（导入时算好，直接落库）。</summary>
    [ObservableProperty] private string romHash = "";

    public string FileName => Path.GetFileName(FilePath);
    public string FileSizeDisplay => Game.FormatSize(FileSize);
}

public static class RomImporter
{
    public sealed record ScanResult(
        List<ImportCandidate> Candidates,
        int DuplicateCount,
        int ExcludedCount,
        int FormatSkippedCount = 0);

    /// <summary>扫描进度（处理到第几个、共几个、当前文件）。</summary>
    public sealed record ScanProgress(int Processed, int Total, string FilePath);

    public static ScanResult Scan(
        IEnumerable<string> inputs,
        bool recursive,
        IEnumerable<string> existingPaths,
        IReadOnlyCollection<string>? existingHashes = null,
        IReadOnlyCollection<string>? allowedExtensions = null,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>>? platformFormats = null,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var existing = new HashSet<string>(existingPaths, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowed = allowedExtensions?
            .Select(extension => extension.StartsWith('.') ? extension : "." + extension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 导入时没有指定平台白名单，则把各平台配置过的格式也视为已知格式
        if (allowed is not { Count: > 0 } && platformFormats is { Count: > 0 })
        {
            allowed = platformFormats.Values
                .SelectMany(extensions => extensions)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var candidates = new List<ImportCandidate>();
        var duplicates = 0;
        var excluded = 0;
        var formatSkipped = 0;

        // 先收集文件清单，才能给出「已处理 / 总数」的进度；过滤器仍按原语义在 AddFile 里处理。
        var files = new List<(string Path, bool Explicit)>();
        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(input))
            {
                files.Add((input, true));
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
                    cancellationToken.ThrowIfCancellationRequested();
                    files.Add((file, false));
                }
            }
        }

        var reportEvery = Math.Max(1, files.Count / 200);
        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (path, explicitSelection) = files[index];
            AddFile(path, explicitSelection);

            var processed = index + 1;
            if (progress != null &&
                (processed == files.Count || processed == 1 || processed % reportEvery == 0))
            {
                progress.Report(new ScanProgress(processed, files.Count, path));
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        return new ScanResult(candidates, duplicates, excluded, formatSkipped);

        void AddFile(string file, bool explicitSelection)
        {
            var extension = Path.GetExtension(file);

            // 有格式白名单时以白名单为准，不再受内置扩展名限制（支持 gdi/pkg 等自定义格式）
            if (allowed is { Count: > 0 })
            {
                if (!allowed.Contains(extension))
                {
                    if (explicitSelection) excluded++;
                    else formatSkipped++;
                    return;
                }
            }
            else if (!ExtensionMap.IsKnownOrArchive(file))
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

            var hash = RomHasher.Compute(fullPath);
            if (hash.Length > 0)
            {
                // 库内已有同一内容，或本批里已出现过同一内容 → 视为重复
                if ((existingHashes?.Contains(hash) ?? false) || !seenHashes.Add(hash))
                {
                    duplicates++;
                    return;
                }
            }

            var (platform, needsSpecify) = PlatformFormats.Resolve(fullPath, info, platformFormats);

            candidates.Add(new ImportCandidate
            {
                FilePath = fullPath,
                FileSize = size,
                NeedsSpecify = needsSpecify,
                Title = Path.GetFileNameWithoutExtension(fullPath),
                Platform = platform,
                RomHash = hash,
            });
        }
    }

    /// <summary>计算库内目标路径（同名自动追加序号）。folder 为空时按平台名归档。</summary>
    public static string ResolveTargetPath(string sourceFile, string libraryRoot, string platform, string? folder = null)
    {
        var folderName = !string.IsNullOrWhiteSpace(folder)
            ? folder
            : string.IsNullOrWhiteSpace(platform) ? "未分类" : platform;
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

        return target;
    }

    /// <summary>复制到库目录并返回新路径。</summary>
    public static string CopyToLibrary(string sourceFile, string libraryRoot, string platform, string? folder = null)
    {
        var target = ResolveTargetPath(sourceFile, libraryRoot, platform, folder);
        File.Copy(sourceFile, target, false);
        return target;
    }

    /// <summary>移动到库目录并返回新路径；跨盘时退化为复制后删除源文件。</summary>
    public static string MoveToLibrary(string sourceFile, string libraryRoot, string platform, string? folder = null)
    {
        var target = ResolveTargetPath(sourceFile, libraryRoot, platform, folder);
        try
        {
            File.Move(sourceFile, target);
        }
        catch (IOException)
        {
            File.Copy(sourceFile, target, false);
            File.Delete(sourceFile);
        }

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
