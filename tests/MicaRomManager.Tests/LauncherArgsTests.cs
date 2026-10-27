using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class LauncherArgsTests
{
    [Fact]
    public void Build_ReplacesQuotedCoreAndRom()
    {
        var result = ArgsBuilder.Build(
            "-L \"{core}\" \"{rom}\"",
            @"C:\roms\Super Metroid.sfc",
            @"C:\emu\cores\snes9x_libretro.dll");

        Assert.Equal(
            "-L \"C:\\emu\\cores\\snes9x_libretro.dll\" \"C:\\roms\\Super Metroid.sfc\"",
            result);
    }

    [Fact]
    public void Build_QuotesBarePlaceholder()
    {
        Assert.Equal("\"C:\\roms\\Game.gba\"", ArgsBuilder.Build("{rom}", @"C:\roms\Game.gba"));
    }

    [Fact]
    public void Build_ReplacesRomDirAndName()
    {
        var result = ArgsBuilder.Build(
            "--dir {romdir} --file {romname}",
            @"D:\Games\Zelda\Zelda.nes");

        Assert.Equal("--dir \"D:\\Games\\Zelda\" --file \"Zelda.nes\"", result);
    }

    [Fact]
    public void Build_KeepsTemplateWhenNoPlaceholder()
    {
        Assert.Equal("--fullscreen", ArgsBuilder.Build("--fullscreen", @"C:\a.bin"));
    }

    [Theory]
    [InlineData("-L \"{core}\" \"{rom}\"", true)]
    [InlineData("\"{rom}\"", false)]
    [InlineData("", false)]
    public void RequiresCore_DetectsCorePlaceholder(string template, bool expected)
    {
        Assert.Equal(expected, ArgsBuilder.RequiresCore(template));
    }

    [Fact]
    public void ResolveTemplate_PrefersPlatformTemplate()
    {
        Assert.Equal("--fullscreen",
            ArgsBuilder.ResolveTemplate("--fullscreen", "RetroArch", "-L \"{core}\" \"{rom}\""));
    }

    [Fact]
    public void ResolveTemplate_UsesEmulatorTemplateWithCore()
    {
        Assert.Equal("-L \"{core}\" \"{rom}\"",
            ArgsBuilder.ResolveTemplate("", "My Emulator", "-L \"{core}\" \"{rom}\""));
    }

    [Fact]
    public void ResolveTemplate_RetroArchNameGetsDefaultEvenWithPlainTemplate()
    {
        Assert.Equal(ArgsBuilder.RetroArchDefaultTemplate,
            ArgsBuilder.ResolveTemplate(null, "RetroArch", "\"{rom}\""));
    }

    [Fact]
    public void ResolveTemplate_NonCoreEmulatorKeepsOwnTemplate()
    {
        Assert.Equal("\"{rom}\"", ArgsBuilder.ResolveTemplate(null, "Dolphin", "\"{rom}\""));
    }

    [Fact]
    public void ResolveTemplate_EmptyFallsBackToRom()
    {
        Assert.Equal("\"{rom}\"", ArgsBuilder.ResolveTemplate(null, "Dolphin", ""));
    }
}
