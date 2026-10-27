using System.IO;
using MicaRomManager.Models;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class SharePackagerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mica-share-test-" + Guid.NewGuid().ToString("N"));

    public SharePackagerTests()
    {
        Directory.CreateDirectory(RomDir);
        Directory.CreateDirectory(EmulatorDir);
        Directory.CreateDirectory(Path.Combine(EmulatorDir, "cores"));

        File.WriteAllText(Path.Combine(RomDir, "Super Metroid.sfc"), "rom-data");
        File.WriteAllText(Path.Combine(EmulatorDir, "retroarch.exe"), "exe");
        File.WriteAllText(Path.Combine(EmulatorDir, "cores", "snes9x_libretro.dll"), "core");
    }

    private string RomDir => Path.Combine(_root, "library", "SNES");
    private string EmulatorDir => Path.Combine(_root, "emulators", "RetroArch");
    private string ExportDir => Path.Combine(_root, "export");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch
        {
            // 测试清理失败忽略
        }
    }

    private (Game Game, Emulator Emulator) BuildSample()
    {
        var emulator = new Emulator
        {
            Id = 1,
            Name = "RetroArch",
            InstallDir = EmulatorDir,
            ExePath = "retroarch.exe",
            ArgsTemplate = "\"{rom}\"",
            CorePath = "cores\\snes9x_libretro.dll",
            Version = "1.19",
        };

        var game = new Game
        {
            Title = "Super Metroid",
            Platform = "SNES",
            RomPath = Path.Combine(RomDir, "Super Metroid.sfc"),
            EmulatorId = 1,
        };

        return (game, emulator);
    }

    [Fact]
    public void Export_CreatesLauncherRomManifestAndReadme()
    {
        var (game, emulator) = BuildSample();
        var packager = new SharePackager();

        var result = packager.Export(
            ExportDir,
            "我的分享包",
            new List<ShareItem> { new() { Game = game, Emulator = emulator, RomPath = game.RomPath } },
            includeEmulators: true,
            includeSaves: false,
            saveDirectory: null,
            createZip: false);

        Assert.True(Directory.Exists(result.PackageDir));
        Assert.Single(result.Launchers);
        Assert.Empty(result.Warnings);
        Assert.Null(result.ZipPath);

        var launcher = Path.Combine(result.PackageDir, result.Launchers[0]);
        Assert.True(File.Exists(launcher));

        var script = File.ReadAllText(launcher);
        Assert.Contains("chcp 65001", script);
        Assert.Contains("emulators\\RetroArch\\retroarch.exe", script);
        Assert.Contains("cores\\snes9x_libretro.dll", script);
        Assert.Contains("roms\\Super Metroid\\Super Metroid.sfc", script);

        Assert.True(File.Exists(Path.Combine(result.PackageDir, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(result.PackageDir, "README.txt")));
        Assert.True(File.Exists(Path.Combine(result.PackageDir, "emulators", "RetroArch", "retroarch.exe")));
        Assert.True(File.Exists(Path.Combine(result.PackageDir, "roms", "Super Metroid", "Super Metroid.sfc")));
    }

    [Fact]
    public void Export_CanCreateZip()
    {
        var (game, emulator) = BuildSample();
        var packager = new SharePackager();

        var result = packager.Export(
            ExportDir,
            "zip包",
            new List<ShareItem> { new() { Game = game, Emulator = emulator, RomPath = game.RomPath } },
            includeEmulators: false,
            includeSaves: false,
            saveDirectory: null,
            createZip: true);

        Assert.NotNull(result.ZipPath);
        Assert.True(File.Exists(result.ZipPath));
    }

    [Fact]
    public void Export_WarnsWhenRomMissing()
    {
        var (game, emulator) = BuildSample();
        var packager = new SharePackager();

        var result = packager.Export(
            ExportDir,
            "missing",
            new List<ShareItem> { new() { Game = game, Emulator = emulator, RomPath = Path.Combine(RomDir, "nope.sfc") } },
            includeEmulators: false,
            includeSaves: false,
            saveDirectory: null,
            createZip: false);

        Assert.Empty(result.Launchers);
        Assert.NotEmpty(result.Warnings);
    }

    [Theory]
    [InlineData("a/b:c", "a_b_c")]
    [InlineData("  name.  ", "name")]
    [InlineData("", "")]
    public void SanitizeName_RemovesInvalidCharacters(string input, string expected)
    {
        Assert.Equal(expected, SharePackager.SanitizeName(input));
    }

    [Fact]
    public void BuildLauncherScript_UsesUtf8FriendlyHeader()
    {
        var script = SharePackager.BuildLauncherScript("emulators\\X\\x.exe", "\"roms\\a.sfc\"");
        var lines = script.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToArray();

        Assert.Equal("@echo off", lines[0]);
        Assert.Equal("chcp 65001 >nul", lines[1]);
        Assert.Equal("cd /d \"%~dp0\"", lines[2]);
        Assert.Equal("start \"\" \"emulators\\X\\x.exe\" \"roms\\a.sfc\"", lines[3]);
    }
}
