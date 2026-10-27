using System.IO;
using MicaRomManager.Models;
using MicaRomManager.Services.Sending;
using Xunit;

namespace MicaRomManager.Tests;

public class GameSenderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-gamesend-" + Guid.NewGuid().ToString("N"));
    private readonly string _sourceDir;
    private readonly string _driveDir;

    public GameSenderTests()
    {
        _sourceDir = Path.Combine(_dir, "source");
        _driveDir = Path.Combine(_dir, "drive");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_driveDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // 清理失败不影响测试
        }
    }

    private static DeviceProfile Profile(bool allowUnmapped = false) => new()
    {
        Id = "test",
        Name = "测试设备",
        RootFolder = "Roms",
        FolderTemplate = "{root}/{platform}",
        AllowUnmappedPlatforms = allowUnmapped,
        PlatformFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PlayStation"] = "PS",
            ["NES"] = "FC",
        },
    };

    private DeviceConnection Connection(bool overwrite = false, bool allowUnmapped = false)
    {
        var profile = Profile(allowUnmapped);
        return new DeviceConnection
        {
            Profile = profile,
            Drive = new DriveEntry(_driveDir, "测试盘", long.MaxValue, long.MaxValue, true),
            OverwriteSameName = overwrite,
        };
    }

    private string CreateSource(string name, string content)
    {
        var path = Path.Combine(_sourceDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Send_CopiesAllDiscsAndReportsProgress()
    {
        var disc1 = CreateSource("Game (Disc 1).bin", "one");
        var disc2 = CreateSource("Game (Disc 2).bin", "two");
        var game = new Game
        {
            Title = "Game",
            Platform = "PlayStation",
            RomPath = disc1,
            DiscPaths = disc2,
        };

        var reports = new List<GameSendProgress>();
        var outcome = GameSender.Send(game, Connection(), reports.Add);

        Assert.Equal(2, outcome.Copied);
        Assert.Equal(0, outcome.Failed);
        Assert.True(File.Exists(Path.Combine(_driveDir, "Roms", "PS", "Game (Disc 1).bin")));
        Assert.True(File.Exists(Path.Combine(_driveDir, "Roms", "PS", "Game (Disc 2).bin")));
        Assert.NotEmpty(reports);
        Assert.Equal(6, reports[^1].TotalBytes);
        Assert.Equal(6, reports[^1].CopiedBytes);
    }

    [Fact]
    public void Send_SkipsExistingFileByDefault()
    {
        var source = CreateSource("game.chd", "new");
        var target = Path.Combine(_driveDir, "Roms", "PS", "game.chd");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "old");

        var outcome = GameSender.Send(
            new Game { Title = "G", Platform = "PlayStation", RomPath = source },
            Connection(overwrite: false));

        Assert.Equal(0, outcome.Copied);
        Assert.Equal(1, outcome.Skipped);
        Assert.Equal("old", File.ReadAllText(target));
    }

    [Fact]
    public void Send_OverwritesWhenRequested()
    {
        var source = CreateSource("game.chd", "new");
        var target = Path.Combine(_driveDir, "Roms", "PS", "game.chd");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "old");

        var outcome = GameSender.Send(
            new Game { Title = "G", Platform = "PlayStation", RomPath = source },
            Connection(overwrite: true));

        Assert.Equal(1, outcome.Copied);
        Assert.Equal("new", File.ReadAllText(target));
    }

    [Fact]
    public void Send_MissingFileIsCountedButDoesNotThrow()
    {
        var good = CreateSource("good.nes", "ok");
        var game = new Game
        {
            Title = "G",
            Platform = "NES",
            RomPath = Path.Combine(_sourceDir, "missing.nes"),
            DiscPaths = good,
        };

        var outcome = GameSender.Send(game, Connection());

        Assert.Equal(1, outcome.Failed);
        Assert.Equal(1, outcome.Copied);
        Assert.True(File.Exists(Path.Combine(_driveDir, "Roms", "FC", "good.nes")));
    }

    [Fact]
    public void Send_UnmappedPlatformIsSkipped()
    {
        var source = CreateSource("game.iso", "x");

        var outcome = GameSender.Send(
            new Game { Title = "G", Platform = "PS2", RomPath = source },
            Connection(allowUnmapped: false));

        Assert.Equal(0, outcome.Copied);
        Assert.Equal(1, outcome.Skipped);
        Assert.Contains(outcome.Messages, message => message.Contains("PS2"));
    }
}
