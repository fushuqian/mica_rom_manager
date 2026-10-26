using System.IO;
using MicaRomManager.Data;
using MicaRomManager.Models;

namespace MicaRomManager.Services;

public static class EmulatorPaths
{
    public static string? ResolveExe(Emulator emulator) => Resolve(emulator.ExePath, emulator.InstallDir, true);

    public static string? ResolveCore(Emulator emulator) => Resolve(emulator.CorePath, emulator.InstallDir, true);

    public static string? ResolveConfig(Emulator emulator) => Resolve(emulator.ConfigPath, emulator.InstallDir, false);

    private static string? Resolve(string value, string installDir, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        try
        {
            var expanded = LocalPath.Expand(value);
            var baseDir = string.IsNullOrWhiteSpace(installDir) ? "" : LocalPath.Expand(installDir);
            var combined = Path.IsPathRooted(expanded)
                ? expanded
                : Path.Combine(baseDir, expanded);
            var full = Path.GetFullPath(combined);
            if (!mustExist) return full;
            return File.Exists(full) ? full : null;
        }
        catch
        {
            return null;
        }
    }
}

public static class EmulatorResolver
{
    public static Emulator? Resolve(Game game, IReadOnlyList<Emulator> all, EmulatorRepository repository)
    {
        if (game.EmulatorId is long id)
        {
            var bound = all.FirstOrDefault(e => e.Id == id && e.Enabled);
            if (bound != null) return bound;
        }

        // 平台管理里配置的默认模拟器优先
        var platformEmulatorId = AppServices.Platforms.Get(game.Platform)?.EmulatorId;
        if (platformEmulatorId is long defaultId)
        {
            var platformDefault = all.FirstOrDefault(e => e.Id == defaultId && e.Enabled);
            if (platformDefault != null) return platformDefault;
        }

        return repository.GetDefaultForPlatform(game.Platform)
               ?? repository.ListForPlatform(game.Platform).FirstOrDefault();
    }
}
