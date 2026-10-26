using System.IO;

namespace MicaRomManager.Services;

public static class ArgsBuilder
{
    public static bool RequiresCore(string template) =>
        !string.IsNullOrWhiteSpace(template) &&
        template.Contains("{core}", StringComparison.OrdinalIgnoreCase);

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
