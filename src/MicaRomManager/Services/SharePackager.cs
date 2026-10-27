using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MicaRomManager.Models;

namespace MicaRomManager.Services;

public sealed class ShareItem
{
    public required Game Game { get; init; }
    public required Emulator Emulator { get; init; }
    public required string RomPath { get; init; }
}

public sealed class SharePackResult
{
    public string PackageDir { get; set; } = "";
    public string? ZipPath { get; set; }
    public long TotalBytes { get; set; }
    public List<string> Warnings { get; } = new();
    public List<string> Launchers { get; } = new();
}

public sealed class SharePackager
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly string[] InvalidNameChars = { "\\", "/", ":", "*", "?", "\"", "<", ">", "|" };

    private sealed record EmulatorLayout(string? ExeRelative, string? CoreRelative);

    public long EstimateSize(IReadOnlyList<ShareItem> items, bool includeEmulators, string? saveDirectory)
    {
        long total = 0;
        foreach (var item in items)
        {
            try
            {
                total += new FileInfo(item.RomPath).Length;
            }
            catch
            {
                // 文件不可读时忽略
            }
        }

        if (includeEmulators)
        {
            foreach (var emulator in items.Select(i => i.Emulator).DistinctBy(e => e.Id))
            {
                var directory = GetEmulatorSourceDirectory(emulator);
                if (!string.IsNullOrEmpty(directory)) total += DirectorySize(directory);
            }
        }

        if (!string.IsNullOrWhiteSpace(saveDirectory) && Directory.Exists(saveDirectory))
        {
            total += DirectorySize(saveDirectory);
        }

        return total;
    }

    public SharePackResult Export(
        string exportRoot,
        string packageName,
        IReadOnlyList<ShareItem> items,
        bool includeEmulators,
        bool includeSaves,
        string? saveDirectory,
        bool createZip)
    {
        var result = new SharePackResult();
        if (items.Count == 0)
        {
            throw new InvalidOperationException("没有可打包的游戏。");
        }

        var root = Path.GetFullPath(exportRoot);
        Directory.CreateDirectory(root);

        var folder = SanitizeName(packageName);
        if (string.IsNullOrWhiteSpace(folder)) folder = "ROM分享包";
        var packageDir = Path.Combine(root, folder);
        if (Directory.Exists(packageDir))
        {
            packageDir = Path.Combine(root, $"{folder}-{DateTime.Now:yyyyMMddHHmmss}");
        }

        Directory.CreateDirectory(packageDir);
        result.PackageDir = packageDir;

        var layouts = new Dictionary<(long EmulatorId, string Platform), EmulatorLayout>();
        if (includeEmulators)
        {
            var prepared = new HashSet<long>();
            foreach (var item in items)
            {
                var emulator = item.Emulator;
                var sourceDirectory = GetEmulatorSourceDirectory(emulator);
                if (string.IsNullOrEmpty(sourceDirectory) || !Directory.Exists(sourceDirectory))
                {
                    if (prepared.Add(emulator.Id))
                        result.Warnings.Add($"模拟器「{emulator.Name}」找不到安装目录，未打包；启动器将使用原路径。");
                    continue;
                }

                var emulatorFolder = SanitizeName(emulator.Name);
                if (string.IsNullOrWhiteSpace(emulatorFolder)) emulatorFolder = "emulator";
                var packageEmulatorDir = Path.Combine(packageDir, "emulators", emulatorFolder);

                if (prepared.Add(emulator.Id))
                {
                    try
                    {
                        CopyDirectory(sourceDirectory, packageEmulatorDir);
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"复制模拟器「{emulator.Name}」失败：{ex.Message}");
                        continue;
                    }
                }

                var exeAbsolute = EmulatorPaths.ResolveExe(emulator);
                var platformCore = AppServices.Platforms?.Get(item.Game.Platform)?.CorePath;
                var coreAbsolute = EmulatorPaths.ResolvePlatformCore(emulator, platformCore)
                                   ?? EmulatorPaths.ResolveCore(emulator);

                layouts[(emulator.Id, item.Game.Platform)] = new EmulatorLayout(
                    RelativeIfInside(exeAbsolute, sourceDirectory, "emulators", emulatorFolder),
                    RelativeIfInside(coreAbsolute, sourceDirectory, "emulators", emulatorFolder));
            }
        }

        if (includeSaves && !string.IsNullOrWhiteSpace(saveDirectory) && Directory.Exists(saveDirectory))
        {
            try
            {
                CopyDirectory(saveDirectory, Path.Combine(packageDir, "saves"));
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"复制存档失败：{ex.Message}");
            }
        }

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            try
            {
                if (!File.Exists(item.RomPath))
                {
                    result.Warnings.Add($"游戏「{item.Game.Title}」的 ROM 文件不存在，已跳过。");
                    continue;
                }

                layouts.TryGetValue((item.Emulator.Id, item.Game.Platform), out var layout);
                var platformSetting = AppServices.Platforms?.Get(item.Game.Platform);
                var argsTemplate = ArgsBuilder.ResolveTemplate(
                    platformSetting?.ArgsTemplate, item.Emulator.Name, item.Emulator.ArgsTemplate);

                var exeExpression = layout?.ExeRelative
                                    ?? EmulatorPaths.ResolveExe(item.Emulator)
                                    ?? item.Emulator.ExePath;
                var coreExpression = layout?.CoreRelative
                                     ?? EmulatorPaths.ResolvePlatformCore(item.Emulator, platformSetting?.CorePath)
                                     ?? EmulatorPaths.ResolveCore(item.Emulator)
                                     ?? item.Emulator.CorePath;

                if (ArgsBuilder.RequiresCore(argsTemplate) && string.IsNullOrWhiteSpace(coreExpression))
                {
                    result.Warnings.Add($"游戏「{item.Game.Title}」的平台「{item.Game.Platform}」缺少核心文件，未生成启动器。");
                    continue;
                }

                var titleFolder = SanitizeName(item.Game.Title);
                if (string.IsNullOrWhiteSpace(titleFolder)) titleFolder = Path.GetFileNameWithoutExtension(item.RomPath);
                var romRelative = Path.Combine("roms", titleFolder, Path.GetFileName(item.RomPath));
                var romTarget = Path.Combine(packageDir, romRelative);
                Directory.CreateDirectory(Path.GetDirectoryName(romTarget)!);
                File.Copy(item.RomPath, romTarget, true);

                var arguments = ArgsBuilder.Build(argsTemplate, romRelative, coreExpression, null);
                var launcherName = UniqueName($"Play-{titleFolder}", usedNames) + ".bat";
                var launcherPath = Path.Combine(packageDir, launcherName);
                File.WriteAllText(launcherPath, BuildLauncherScript(exeExpression, arguments), Utf8NoBom);
                result.Launchers.Add(launcherName);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"游戏「{item.Game.Title}」打包失败：{ex.Message}");
            }
        }

        WriteManifest(packageDir, items);
        WriteReadme(packageDir, items, result.Launchers);

        result.TotalBytes = DirectorySize(packageDir);

        if (createZip)
        {
            try
            {
                var zipPath = packageDir + ".zip";
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(packageDir, zipPath, CompressionLevel.Optimal, true);
                result.ZipPath = zipPath;
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"压缩 zip 失败：{ex.Message}");
            }
        }

        return result;
    }

    public static string BuildLauncherScript(string executablePath, string arguments)
    {
        var builder = new StringBuilder();
        builder.AppendLine("@echo off");
        builder.AppendLine("chcp 65001 >nul");
        builder.AppendLine("cd /d \"%~dp0\"");
        builder.AppendLine($"start \"\" \"{executablePath}\" {arguments}");
        return builder.ToString();
    }

    public static string SanitizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var cleaned = name;
        foreach (var invalid in InvalidNameChars)
        {
            cleaned = cleaned.Replace(invalid, "_");
        }

        cleaned = cleaned.Trim().TrimEnd('.');
        return cleaned.Length > 80 ? cleaned[..80].TrimEnd() : cleaned;
    }

    public static long DirectorySize(string directory)
    {
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            return Directory.EnumerateFiles(directory, "*", options).Sum(file =>
            {
                try
                {
                    return new FileInfo(file).Length;
                }
                catch
                {
                    return 0L;
                }
            });
        }
        catch
        {
            return 0;
        }
    }

    private static string? CopyConfig(Emulator emulator, string packageDir, string emulatorFolder, List<string> warnings)
    {
        var configPath = EmulatorPaths.ResolveConfig(emulator);
        if (string.IsNullOrWhiteSpace(configPath)) return null;

        var targetDirectory = Path.Combine(packageDir, "config", emulatorFolder);
        try
        {
            if (Directory.Exists(configPath))
            {
                CopyDirectory(configPath, targetDirectory);
                return Path.Combine("config", emulatorFolder);
            }

            if (File.Exists(configPath))
            {
                Directory.CreateDirectory(targetDirectory);
                var fileName = Path.GetFileName(configPath);
                File.Copy(configPath, Path.Combine(targetDirectory, fileName), true);
                return Path.Combine("config", emulatorFolder, fileName);
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"复制配置失败：{ex.Message}");
        }

        return null;
    }

    private static string? RelativeIfInside(string? absolutePath, string sourceDirectory, string packageFolder, string emulatorFolder)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath)) return null;
        try
        {
            var relative = Path.GetRelativePath(sourceDirectory, absolutePath);
            if (relative.StartsWith("..", StringComparison.Ordinal)) return null;
            return Path.Combine(packageFolder, emulatorFolder, relative);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetEmulatorSourceDirectory(Emulator emulator)
    {
        if (!string.IsNullOrWhiteSpace(emulator.InstallDir) && Directory.Exists(emulator.InstallDir))
        {
            return emulator.InstallDir;
        }

        var exePath = EmulatorPaths.ResolveExe(emulator);
        return string.IsNullOrWhiteSpace(exePath) ? null : Path.GetDirectoryName(exePath);
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
            var name = Path.GetFileName(directory);
            if (name.Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
            CopyDirectory(directory, Path.Combine(target, name));
        }
    }

    private static string UniqueName(string baseName, HashSet<string> used)
    {
        var name = baseName;
        var index = 2;
        while (!used.Add(name))
        {
            name = $"{baseName} ({index})";
            index++;
        }

        return name;
    }

    private static void WriteManifest(string packageDir, IReadOnlyList<ShareItem> items)
    {
        var manifest = new
        {
            tool = "MicaRomManager",
            version = "0.1.0",
            createdAt = DateTime.Now.ToString("O"),
            games = items.Select(i => new
            {
                title = i.Game.Title,
                platform = i.Game.Platform,
                rom = Path.Combine("roms", SanitizeName(i.Game.Title), Path.GetFileName(i.RomPath)),
                emulator = i.Emulator.Name,
            }).ToList(),
            emulators = items.Select(i => i.Emulator).DistinctBy(e => e.Id)
                .Select(e => new { name = e.Name, version = e.Version, argsTemplate = e.ArgsTemplate })
                .ToList(),
        };

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
        File.WriteAllText(Path.Combine(packageDir, "manifest.json"), json, Utf8NoBom);
    }

    private static void WriteReadme(string packageDir, IReadOnlyList<ShareItem> items, IReadOnlyList<string> launchers)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Mica ROM Manager 分享包");
        builder.AppendLine("======================");
        builder.AppendLine();
        builder.AppendLine("玩法：双击本目录下的 Play-*.bat 即可启动对应游戏。");
        builder.AppendLine("目录说明：");
        builder.AppendLine("  emulators\\  打包的模拟器（若打包时未勾选则需目标电脑自备）");
        builder.AppendLine("  roms\\       游戏 ROM");
        builder.AppendLine("  config\\     模拟器配置");
        builder.AppendLine("  saves\\      存档（若勾选）");
        builder.AppendLine("  manifest.json 本包内容清单");
        builder.AppendLine();
        builder.AppendLine("游戏列表：");
        foreach (var item in items)
        {
            builder.AppendLine($"  - {item.Game.Title}（{item.Game.Platform}） -> {item.Emulator.Name}");
        }

        if (launchers.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("启动器：");
            foreach (var launcher in launchers) builder.AppendLine($"  - {launcher}");
        }

        builder.AppendLine();
        builder.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        File.WriteAllText(Path.Combine(packageDir, "README.txt"), builder.ToString(), Utf8NoBom);
    }
}
