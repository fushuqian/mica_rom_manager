using System.IO;

namespace MicaRomManager.Services;

public static class ArgsBuilder
{
    public const string RetroArchDefaultTemplate = "-L \"{core}\" \"{rom}\"";

    public static bool RequiresCore(string template) =>
        !string.IsNullOrWhiteSpace(template) &&
        template.Contains("{core}", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 平台未配置启动参数时的回退：优先沿用模拟器自带参数；RetroArch 类模拟器未带 {core} 时补默认模板。
    /// </summary>
    public static string ResolveTemplate(string? platformTemplate, string? emulatorName, string? emulatorTemplate)
    {
        if (!string.IsNullOrWhiteSpace(platformTemplate)) return platformTemplate.Trim();

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
