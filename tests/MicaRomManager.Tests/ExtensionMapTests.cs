using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class ExtensionMapTests
{
    [Theory]
    [InlineData("game.sfc", "SNES")]
    [InlineData("game.SMC", "SNES")]
    [InlineData("game.gba", "GBA")]
    [InlineData("game.nes", "NES")]
    [InlineData("game.Z64", "N64")]
    public void Detect_MapsKnownPlatforms(string fileName, string platform)
    {
        var info = ExtensionMap.Detect(fileName);
        Assert.Equal(platform, info.Platform);
        Assert.False(info.NeedsSpecify);
    }

    [Theory]
    [InlineData("disc.iso")]
    [InlineData("disc.chd")]
    [InlineData("rom.zip")]
    public void Detect_DiscImagesAndArchivesNeedSpecify(string fileName)
    {
        var info = ExtensionMap.Detect(fileName);
        Assert.Null(info.Platform);
        Assert.True(info.NeedsSpecify);
    }

    [Fact]
    public void Detect_UnknownExtensionNeedsSpecify()
    {
        var info = ExtensionMap.Detect("unknown.xyz");
        Assert.Null(info.Platform);
        Assert.True(info.NeedsSpecify);
    }

    [Theory]
    [InlineData("rom.zip", true)]
    [InlineData("rom.7Z", true)]
    [InlineData("rom.rar", true)]
    [InlineData("rom.sfc", false)]
    public void IsArchive_DetectsArchiveExtensions(string fileName, bool expected)
    {
        Assert.Equal(expected, ExtensionMap.IsArchive(fileName));
    }

    [Fact]
    public void IsKnownOrArchive_AcceptsKnownAndArchives()
    {
        Assert.True(ExtensionMap.IsKnownOrArchive("a.sfc"));
        Assert.True(ExtensionMap.IsKnownOrArchive("a.zip"));
        Assert.False(ExtensionMap.IsKnownOrArchive("a.txt"));
    }
}
