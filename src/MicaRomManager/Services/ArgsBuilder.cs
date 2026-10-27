using System.IO;

namespace MicaRomManager.Services;

public static class ArgsBuilder
{
    public const string RetroArchDefaultTemplate = "-L \"{core}\" \"{rom}\"";

    public static bool RequiresCore(string template) =>
        !string.IsNullOrWhiteSpace(template) &&
        template.Contains("{core}", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 组合启动参数：模拟器默认参数为底，平台参数作为额外参数追加；平台留空则只用默认参数。
    /// </summary>
    public static string ResolveTemplate(string? platformTemplate, string? emulatorName, string? emulatorTemplate)
    {
        var baseArgs = ResolveEmulatorTemplate(emulatorName, emulatorTemplate);
        var extra = platformTemplate?.Trim() ?? "";
        if (extra.Length == 0) return baseArgs;
        if (baseArgs.Length == 0) return extra;
        return $"{baseArgs} {extra}";
    }

    /// <summary>模拟器自身参数；RetroArch 未配置或缺少 {core} 时补默认模板。</summary>
    private static string ResolveEmulatorTemplate(string? emulatorName, string? emulatorTemplate)
    {
        var emulatorArgs = emulatorTemplate?.Trim() ?? "";
        if (RequiresCore(emulatorArgs)) return emulatorArgs;

        if ((emulatorName ?? "").Contains("RetroArch", StringComparison.OrdinalIgnoreCase))
            return RetroArchDefaultTemplate;

        return string.IsNullOrWhiteSpace(emulatorArgs) ? "\"{rom}\"" : emulatorArgs;
    }

    /// <summary>占位符统一替换为带引号的路径，模板里原有引号不会重复。</summary>
    public static string Build(string template, string romPath, string? corePath = null, string? configPath = null)
    {
        var romDir = Path.GetDirectoryName(romPath) ?? "";
        var romName = Path.GetFileName(romPath);
        return BuildWithPaths(template, romPath, romDir, romName, corePath, configPath);
    }

    public static string BuildWithPaths(string template, string rom, string romDir, string romName,
        string? core, string? config)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{rom}"] = rom,
            ["{romdir}"] = romDir,
            ["{romname}"] = romName,
            ["{core}"] = core ?? "",
            ["{config}"] = config ?? "",
        };

        var result = template ?? "";
        foreach (var (token, value) in map)
        {
            var quoted = $"\"{value}\"";
            result = result.Replace($"\"{token}\"", quoted, StringComparison.OrdinalIgnoreCase);
            result = result.Replace(token, quoted, StringComparison.OrdinalIgnoreCase);
        }

        return result.Trim();
    }
}
