using System.IO;
using MicaRomManager.Data;
using MicaRomManager.Models;

namespace MicaRomManager.Services;

public static class EmulatorPaths
{
    public static string? ResolveExe(Emulator emulator) => Resolve(emulator.ExePath, emulator.InstallDir, true);

    public static string? ResolveCore(Emulator emulator) => Resolve(emulator.CorePath, emulator.InstallDir, true);

    /// <summary>
    /// 安装目录的绝对路径：库里存的是「可存储形式」（可能带 %DATA% 或是相对路径），
    /// 判断存在性、打开目录、删除前都必须先展开，否则永远判成缺失。
    /// </summary>
    public static string ResolveInstallDir(Emulator emulator)
    {
        if (string.IsNullOrWhiteSpace(emulator.InstallDir)) return "";

        try
        {
            return Path.GetFullPath(PortablePath.Expand(emulator.InstallDir));
        }
        catch
        {
            return "";
        }
    }

    public static bool InstallDirExists(Emulator emulator) =>
        ResolveInstallDir(emulator) is { Length: > 0 } dir && Directory.Exists(dir);

    /// <summary>平台级核心（相对模拟器安装目录）；未配置返回 null，由调用方回退到模拟器自带核心。</summary>
    public static string? ResolvePlatformCore(Emulator emulator, string? platformCore)
    {
        if (string.IsNullOrWhiteSpace(platformCore)) return null;
        return Resolve(platformCore, emulator.InstallDir, true);
    }

    public static string? ResolveConfig(Emulator emulator) => Resolve(emulator.ConfigPath, emulator.InstallDir, false);

    private static string? Resolve(string value, string installDir, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        try
        {
            var expanded = LocalPath.Expand(value);
            var baseDir = string.IsNullOrWhiteSpace(installDir) ? "" : PortablePath.Expand(installDir);
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

        // 绑定平台已退役，未在平台设置里指定默认模拟器时由调用方提示
        return null;
    }
}
