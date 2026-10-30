using MicaRomManager.Models;

namespace MicaRomManager.Services;

/// <summary>
/// 把内置/自定义模拟器模板转成可入库的 <see cref="Emulator"/>。
/// 纯逻辑，不碰 UI 与数据库，便于单测。
/// </summary>
public static class EmulatorTemplateFactory
{
    public const string BlankName = "新模拟器";

    public static Emulator CreateFromTemplate(
        EmulatorTemplate template,
        IReadOnlyCollection<string> existingNames,
        IReadOnlyCollection<string> availablePlatforms)
    {
        var repo = EmulatorUpdater.NormalizeRepository(template.GithubRepo);
        var emulator = new Emulator
        {
            Name = UniqueName(template.Name, existingNames),
            ArgsTemplate = string.IsNullOrWhiteSpace(template.ArgsTemplate)
                ? "\"{rom}\""
                : template.ArgsTemplate.Trim(),
            SupportedExt = NormalizeExtensions(template.Extensions),
            GithubRepo = repo,
            WebsiteUrl = repo.Length == 0 ? "" : $"https://github.com/{repo}/releases",
            Notes = template.Notes?.Trim() ?? "",
            Enabled = true,
        };

        foreach (var platform in template.Platforms)
        {
            // 模板里的平台名是标准名；用户可能把字典改成 GB / PS1 这类简称，
            // 这里只做同名匹配，匹配不上就留给用户在「设置 → 平台」手动绑定。
            var match = availablePlatforms.FirstOrDefault(name =>
                string.Equals(name, platform, StringComparison.OrdinalIgnoreCase));
            if (match == null) continue;
            if (emulator.Platforms.Contains(match, StringComparer.OrdinalIgnoreCase)) continue;
            emulator.Platforms.Add(match);
        }

        return emulator;
    }

    public static Emulator CreateBlank(IReadOnlyCollection<string> existingNames) => new()
    {
        Name = UniqueName(BlankName, existingNames),
        ArgsTemplate = "\"{rom}\"",
        Enabled = true,
    };

    /// <summary>重名时追加序号：RetroArch、RetroArch 2、RetroArch 3…</summary>
    public static string UniqueName(string? baseName, IReadOnlyCollection<string> existingNames)
    {
        var name = string.IsNullOrWhiteSpace(baseName) ? BlankName : baseName.Trim();
        if (!Contains(existingNames, name)) return name;

        var index = 2;
        while (Contains(existingNames, $"{name} {index}")) index++;
        return $"{name} {index}";
    }

    /// <summary>模板扩展名统一存成「空格分隔、小写、无点」。</summary>
    public static string NormalizeExtensions(IEnumerable<string>? extensions) =>
        extensions == null
            ? ""
            : PlatformFormats.Normalize(string.Join(" ", extensions));

    private static bool Contains(IEnumerable<string> names, string value) =>
        names.Any(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
}
