using System.IO;
using MicaRomManager.Models;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class EmulatorCoresTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-cores-" + Guid.NewGuid().ToString("N"));

    public EmulatorCoresTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "cores"));
        File.WriteAllText(Path.Combine(_dir, "cores", "b_libretro.dll"), "b");
        File.WriteAllText(Path.Combine(_dir, "cores", "a_libretro.dll"), "a");
        File.WriteAllText(Path.Combine(_dir, "cores", "readme.txt"), "t");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // 临时目录清理失败不影响测试结果
        }
    }

    [Fact]
    public void List_ReturnsDllsRelativeToInstallDir()
    {
        var emulator = new Emulator { Name = "RetroArch", InstallDir = _dir };

        var cores = EmulatorCores.List(emulator);

        Assert.Equal(new[]
        {
            Path.Combine("cores", "a_libretro.dll"),
            Path.Combine("cores", "b_libretro.dll"),
        }, cores);
    }

    [Fact]
    public void List_ReturnsEmptyWhenCoresFolderMissing()
    {
        var empty = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(empty);
        var emulator = new Emulator { Name = "RetroArch", InstallDir = empty };

        Assert.Empty(EmulatorCores.List(emulator));
    }

    [Fact]
    public void ResolvePlatformCore_CombinesWithInstallDir()
    {
        var emulator = new Emulator { InstallDir = _dir };

        var core = EmulatorPaths.ResolvePlatformCore(emulator, Path.Combine("cores", "a_libretro.dll"));

        Assert.Equal(Path.Combine(_dir, "cores", "a_libretro.dll"), core);
    }

    [Fact]
    public void ResolvePlatformCore_EmptyReturnsNull()
    {
        Assert.Null(EmulatorPaths.ResolvePlatformCore(new Emulator(), ""));
    }
}
