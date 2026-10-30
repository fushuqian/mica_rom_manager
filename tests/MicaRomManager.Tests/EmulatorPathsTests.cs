using System.IO;
using MicaRomManager.Models;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class EmulatorPathsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-empaths-" + Guid.NewGuid().ToString("N"));

    public EmulatorPathsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // 清理失败不影响测试结果
        }
    }

    private string CreateFile(string relative)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void ResolveExe_RelativeFileNameIsResolvedAgainstInstallDir()
    {
        CreateFile("retroarch.exe");
        var emulator = new Emulator { Name = "RetroArch", ExePath = "retroarch.exe", InstallDir = _dir };

        var resolved = EmulatorPaths.ResolveExe(emulator);

        Assert.NotNull(resolved);
        Assert.Equal(Path.Combine(_dir, "retroarch.exe"), resolved);
    }

    [Fact]
    public void ResolveExe_AbsolutePathIsUsedAsIs()
    {
        var exe = CreateFile(@"RetroArch\retroarch.exe");
        var emulator = new Emulator { ExePath = exe, InstallDir = Path.Combine(_dir, "ignored") };

        Assert.Equal(exe, EmulatorPaths.ResolveExe(emulator));
    }

    [Fact]
    public void ResolveExe_MissingFileReturnsNull()
    {
        var emulator = new Emulator { ExePath = "missing.exe", InstallDir = _dir };

        Assert.Null(EmulatorPaths.ResolveExe(emulator));
    }

    [Fact]
    public void ResolvePlatformCore_FallsBackToNullWhenNotConfigured()
    {
        CreateFile(@"cores\snes9x_libretro.dll");
        var emulator = new Emulator { InstallDir = _dir, CorePath = @"cores\snes9x_libretro.dll" };

        Assert.Null(EmulatorPaths.ResolvePlatformCore(emulator, null));
        Assert.Null(EmulatorPaths.ResolvePlatformCore(emulator, "  "));

        var platformCore = EmulatorPaths.ResolvePlatformCore(emulator, @"cores\snes9x_libretro.dll");
        Assert.Equal(Path.Combine(_dir, "cores", "snes9x_libretro.dll"), platformCore);

        // 模拟器自带核心在平台未指定时使用
        Assert.Equal(Path.Combine(_dir, "cores", "snes9x_libretro.dll"), EmulatorPaths.ResolveCore(emulator));
    }

    [Fact]
    public void ResolveConfig_DoesNotRequireFileToExist()
    {
        var emulator = new Emulator { InstallDir = _dir, ConfigPath = @"RetroArch\config\retroarch.cfg" };

        var resolved = EmulatorPaths.ResolveConfig(emulator);

        Assert.NotNull(resolved);
        Assert.False(File.Exists(resolved));
    }

    [Fact]
    public void ResolveExe_EmptyConfigReturnsNull()
    {
        Assert.Null(EmulatorPaths.ResolveExe(new Emulator()));
    }
}
