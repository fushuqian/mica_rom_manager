using System.IO;
using MicaRomManager.Models;

namespace MicaRomManager.Services;

/// <summary>列出模拟器安装目录下 cores 文件夹中的核心文件（相对安装目录）。</summary>
public static class EmulatorCores
{
    public static IReadOnlyList<string> List(Emulator emulator)
    {
        var root = ResolveRoot(emulator);
        if (string.IsNullOrWhiteSpace(root)) return Array.Empty<string>();

        var coresDir = Path.Combine(root, "cores");
        if (!Directory.Exists(coresDir)) return Array.Empty<string>();

        try
        {
            return Directory.EnumerateFiles(coresDir, "*.dll")
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .Select(path => Path.GetRelativePath(root, path))
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static string? ResolveRoot(Emulator emulator)
    {
        if (!string.IsNullOrWhiteSpace(emulator.InstallDir))
        {
            var expanded = PortablePath.Expand(emulator.InstallDir);
            if (Directory.Exists(expanded)) return expanded;
        }

        var exe = EmulatorPaths.ResolveExe(emulator);
        return string.IsNullOrWhiteSpace(exe) ? null : Path.GetDirectoryName(exe);
    }
}
