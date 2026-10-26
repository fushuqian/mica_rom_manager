using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class PlatformFormatsTests
{
    [Fact]
    public void Parse_SupportsSeparatorsAndLeadingDots()
    {
        var result = PlatformFormats.Parse("iso, CUE .bin；chd、pbp iso");

        Assert.Equal(new[] { ".iso", ".cue", ".bin", ".chd", ".pbp" }, result);
    }

    [Fact]
    public void Normalize_ReturnsSpaceSeparatedLowercase()
    {
        Assert.Equal("iso cue bin chd pbp", PlatformFormats.Normalize("  ISO, Cue .bin;chd、pbp "));
    }

    [Fact]
    public void Supports_IsCaseInsensitive()
    {
        Assert.True(PlatformFormats.Supports("iso cue", ".cue"));
        Assert.True(PlatformFormats.Supports("iso cue", "ISO"));
        Assert.False(PlatformFormats.Supports("iso cue", ".chd"));
    }

    [Fact]
    public void Defaults_ContainCommonPlayStationFormats()
    {
        Assert.Contains(".pbp", PlatformFormats.Parse(PlatformFormats.GetDefault("PlayStation")));
        Assert.Contains(".chd", PlatformFormats.Parse(PlatformFormats.GetDefault("PS2")));
        Assert.Equal("", PlatformFormats.GetDefault("其他"));
    }

    [Fact]
    public void Resolve_UniqueMatchUsesConfiguredPlatform()
    {
        var formats = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["PS1"] = new[] { ".pbp" },
        };
        var builtin = ExtensionMap.Detect("game.pbp");

        var (platform, needsSpecify) = PlatformFormats.Resolve("game.pbp", builtin, formats);

        Assert.Equal("PS1", platform);
        Assert.False(needsSpecify);
    }

    [Fact]
    public void Resolve_MultipleMatchesNeedsSpecify()
    {
        var formats = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["PS1"] = new[] { ".iso" },
            ["PS2"] = new[] { ".iso" },
        };
        var builtin = ExtensionMap.Detect("game.iso");

        var (platform, needsSpecify) = PlatformFormats.Resolve("game.iso", builtin, formats);

        Assert.Equal(ExtensionMap.UnspecifiedPlatform, platform);
        Assert.True(needsSpecify);
    }

    [Fact]
    public void Resolve_FallsBackToBuiltinWhenNoPlatformMatches()
    {
        var formats = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["PS1"] = new[] { ".pbp" },
        };
        var builtin = ExtensionMap.Detect("game.nes");

        var (platform, needsSpecify) = PlatformFormats.Resolve("game.nes", builtin, formats);

        Assert.Equal("NES", platform);
        Assert.False(needsSpecify);
    }

    [Fact]
    public void Resolve_ArchiveAlwaysNeedsSpecify()
    {
        var formats = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["PS1"] = new[] { ".zip" },
        };
        var builtin = ExtensionMap.Detect("game.zip");

        var (platform, needsSpecify) = PlatformFormats.Resolve("game.zip", builtin, formats);

        Assert.Equal(ExtensionMap.UnspecifiedPlatform, platform);
        Assert.True(needsSpecify);
    }
}
