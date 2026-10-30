using MicaRomManager.Models;

namespace MicaRomManager.Services;

/// <summary>
/// 把内置/自定义模拟器模板转成可入库的 <see cref="Emulator"/>。
/// 纯逻辑，不碰 UI 与数据库，便于单测。
/// </summary>
public static class EmulatorTemplateFactory
{
    public const string BlankName = "新模拟器";

    /// <summary>用户把字典改成简称后，模板仍能按常见别名绑定平台。</summary>
    private static readonly Dictionary<string, string[]> PlatformAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Game Boy"] = new[] { "GB" },
        ["GameCube"] = new[] { "NGC" },
        ["Mega Drive"] = new[] { "MD", "Genesis" },
        ["Master System"] = new[] { "SMS" },
        ["Game Gear"] = new[] { "GG" },
        ["Saturn"] = new[] { "SS", "Sega Saturn" },
        ["Dreamcast"] = new[] { "DC" },
        ["PlayStation"] = new[] { "PS1", "PSX" },
        ["PlayStation 2"] = new[] { "PS2" },
        ["PlayStation 3"] = new[] { "PS3" },
        ["PlayStation Portable"] = new[] { "PSP" },
        ["PC Engine"] = new[] { "PCE", "TG16" },
        ["Virtual Boy"] = new[] { "VB" },
        ["WonderSwan"] = new[] { "WS" },
        ["WonderSwan Color"] = new[] { "WSC" },
        ["Wii U"] = new[] { "WIIU" },
        ["Xbox"] = new[] { "XBOX" },
        ["Xbox 360"] = new[] { "XBOX360", "X360" },
        ["Nintendo 64"] = new[] { "N64" },
        ["Nintendo DS"] = new[] { "NDS" },
        ["Nintendo 3DS"] = new[] { "3DS" },
        ["Super Nintendo"] = new[] { "SNES" },
    };

    public static Emulator CreateFromTemplate(
        EmulatorTemplate template,
        IReadOnlyCollection<string> existingNames,
        IReadOnlyCollection<string> availablePlatforms)
    {
        var repo = EmulatorUpdater.NormalizeRepository(template.GithubRepo);
        var website = template.WebsiteUrl?.Trim() ?? "";
        if (website.Length == 0 && repo.Length > 0) website = $"https://github.com/{repo}/releases";

        var emulator = new Emulator
        {
            Name = UniqueName(template.Name, existingNames),
            ArgsTemplate = string.IsNullOrWhiteSpace(template.ArgsTemplate)
                ? "\"{rom}\""
                : template.ArgsTemplate.Trim(),
            SupportedExt = NormalizeExtensions(template.Extensions),
            GithubRepo = repo,
            WebsiteUrl = website,
            Notes = template.Notes?.Trim() ?? "",
            Enabled = true,
        };

        foreach (var platform in template.Platforms)
        {
            // 先同名；再常见简称别名（GB/MD/PS1/DC/XBOX360…）；最后按去空格/符号归一化匹配。
            var match = FindPlatform(platform, availablePlatforms);
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

    private static string? FindPlatform(string platform, IReadOnlyCollection<string> availablePlatforms)
    {
        var exact = availablePlatforms.FirstOrDefault(name =>
            string.Equals(name, platform, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        if (PlatformAliases.TryGetValue(platform, out var aliases))
        {
            foreach (var alias in aliases)
            {
                var hit = availablePlatforms.FirstOrDefault(name =>
                    string.Equals(name, alias, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
            }
        }

        // “Xbox 360” vs “XBOX360”这类只差空格/符号的写法
        var normalized = NormalizePlatform(platform);
        return availablePlatforms.FirstOrDefault(name =>
            string.Equals(NormalizePlatform(name), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePlatform(string name) =>
        new(name.Where(char.IsLetterOrDigit).ToArray());

    private static bool Contains(IEnumerable<string> names, string value) =>
        names.Any(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
}
