using MicaRomManager.Models;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class EmulatorTemplateFactoryTests
{
    private static EmulatorTemplate RetroArchTemplate() => new()
    {
        Name = "RetroArch",
        ArgsTemplate = "-L \"{core}\" \"{rom}\"",
        GithubRepo = "https://github.com/libretro/RetroArch/releases",
        Notes = "需配置核心",
        Platforms = new List<string> { "NES", "Game Boy" },
        Extensions = new List<string> { ".nes", ".gb", ".NES" },
    };

    [Fact]
    public void CreateFromTemplate_MapsFieldsAndNormalizesRepository()
    {
        var emulator = EmulatorTemplateFactory.CreateFromTemplate(
            RetroArchTemplate(),
            Array.Empty<string>(),
            new[] { "NES", "GB" });

        Assert.Equal("RetroArch", emulator.Name);
        Assert.Equal("-L \"{core}\" \"{rom}\"", emulator.ArgsTemplate);
        Assert.Equal("libretro/RetroArch", emulator.GithubRepo);
        Assert.Equal("https://github.com/libretro/RetroArch/releases", emulator.WebsiteUrl);
        Assert.Equal("需配置核心", emulator.Notes);
        Assert.Equal("nes gb", emulator.SupportedExt);
        Assert.True(emulator.Enabled);
    }

    [Fact]
    public void CreateFromTemplate_BindsOnlySameNamePlatforms()
    {
        var emulator = EmulatorTemplateFactory.CreateFromTemplate(
            RetroArchTemplate(),
            Array.Empty<string>(),
            new[] { "nes", "GB", "SNES" });

        // 模板里的 "Game Boy" 与库里的 "GB" 不同名，不做猜测，留给用户手绑。
        Assert.Equal(new[] { "nes" }, emulator.Platforms);
        Assert.Empty(emulator.DefaultPlatforms);
    }

    [Fact]
    public void CreateFromTemplate_KeepsExistingWebsiteAndHandlesEmptyRepo()
    {
        var template = new EmulatorTemplate
        {
            Name = "Custom",
            GithubRepo = "",
            ArgsTemplate = "",
        };

        var emulator = EmulatorTemplateFactory.CreateFromTemplate(
            template, Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal("\"{rom}\"", emulator.ArgsTemplate);
        Assert.Equal("", emulator.GithubRepo);
        Assert.Equal("", emulator.WebsiteUrl);
    }

    [Theory]
    [InlineData("RetroArch", new[] { "RetroArch" }, "RetroArch 2")]
    [InlineData("RetroArch", new[] { "retroarch" }, "RetroArch 2")]
    [InlineData("RetroArch", new[] { "RetroArch", "RetroArch 2" }, "RetroArch 3")]
    [InlineData("RetroArch", new string[0], "RetroArch")]
    [InlineData("  ", new[] { "新模拟器" }, "新模拟器 2")]
    public void UniqueName_AppendsIndexOnCollision(string baseName, string[] existing, string expected)
    {
        Assert.Equal(expected, EmulatorTemplateFactory.UniqueName(baseName, existing));
    }

    [Fact]
    public void CreateBlank_UsesUniqueDefaultName()
    {
        var emulator = EmulatorTemplateFactory.CreateBlank(new[] { "新模拟器" });

        Assert.Equal("新模拟器 2", emulator.Name);
        Assert.Equal("\"{rom}\"", emulator.ArgsTemplate);
        Assert.Empty(emulator.Platforms);
    }
}
