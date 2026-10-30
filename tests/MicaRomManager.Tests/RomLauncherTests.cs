using System.IO;
using MicaRomManager.Data;
using MicaRomManager.Models;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class RomLauncherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-launch-" + Guid.NewGuid().ToString("N"));
    private readonly RomLauncher _launcher;

    public RomLauncherTests()
    {
        Directory.CreateDirectory(_dir);
        var db = new Database(Path.Combine(_dir, "t.db"));
        _launcher = new RomLauncher(new GameRepository(db));
    }

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

    private string Touch(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "rom");
        return path;
    }

    [Fact]
    public void TryLaunch_MissingRomFileIsReportedWithoutThrowing()
    {
        var game = new Game { Title = "G", Platform = "SNES", RomPath = Path.Combine(_dir, "nope.sfc") };
        var emulator = new Emulator { Name = "RetroArch", ExePath = Touch("retroarch.exe") };

        var ok = _launcher.TryLaunch(game, emulator, out var message);

        Assert.False(ok);
        Assert.Contains("ROM 文件不存在", message);
    }

    [Fact]
    public void TryLaunch_NoEmulatorExplainsHowToConfigure()
    {
        var game = new Game { Title = "G", Platform = "SNES", RomPath = Touch("g.sfc") };

        var ok = _launcher.TryLaunch(game, null, out var message);

        Assert.False(ok);
        Assert.Contains("还没有为平台", message);
        Assert.Contains("设置", message);
    }

    [Fact]
    public void TryLaunch_MissingEmulatorExeIsReported()
    {
        var game = new Game { Title = "G", Platform = "SNES", RomPath = Touch("g.sfc") };
        var emulator = new Emulator { Name = "RetroArch", ExePath = Path.Combine(_dir, "gone.exe") };

        var ok = _launcher.TryLaunch(game, emulator, out var message);

        Assert.False(ok);
        Assert.Contains("可执行文件不存在", message);
    }
}
