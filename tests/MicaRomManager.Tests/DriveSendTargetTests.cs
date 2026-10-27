using System.IO;
using MicaRomManager.Models;
using MicaRomManager.Services.Sending;
using Xunit;

namespace MicaRomManager.Tests;

public class DriveSendTargetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-drive-" + Guid.NewGuid().ToString("N"));
    private readonly string _sourceDir;
    private readonly string _driveDir;

    public DriveSendTargetTests()
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

    private string CreateSource(string name, string content)
    {
        var path = Path.Combine(_sourceDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Send_CopiesIntoPlatformFolder_AndKeepsFileName()
    {
        var source = CreateSource("game.chd", "data");

        var result = new DriveSendTarget().Send(
            new[] { new SendFile(source, "PlayStation", "Game", 1998) },
            Profile(), _driveDir, ConflictPolicy.Skip);

        Assert.Equal(1, result.Copied);
        var target = Path.Combine(_driveDir, "Roms", "PS", "game.chd");
        Assert.True(File.Exists(target));
        Assert.Equal("data", File.ReadAllText(target));
    }

    [Fact]
    public void Send_SkipsExistingFile()
    {
        var source = CreateSource("game.chd", "new");
        var target = Path.Combine(_driveDir, "Roms", "PS", "game.chd");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "old");

        var result = new DriveSendTarget().Send(
            new[] { new SendFile(source, "PlayStation", "Game", null) },
            Profile(), _driveDir, ConflictPolicy.Skip);

        Assert.Equal(1, result.Skipped);
        Assert.Equal("old", File.ReadAllText(target));
    }

    [Fact]
    public void Send_OverwritesWhenRequested()
    {
        var source = CreateSource("game.chd", "new");
        var target = Path.Combine(_driveDir, "Roms", "PS", "game.chd");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "old");

        var result = new DriveSendTarget().Send(
            new[] { new SendFile(source, "PlayStation", "Game", null) },
            Profile(), _driveDir, ConflictPolicy.Overwrite);

        Assert.Equal(1, result.Copied);
        Assert.Equal("new", File.ReadAllText(target));
    }

    [Fact]
    public void Send_MissingSourceDoesNotStopOthers()
    {
        var good = CreateSource("good.nes", "ok");
        var files = new[]
        {
            new SendFile(Path.Combine(_sourceDir, "missing.nes"), "NES", "Missing", null),
            new SendFile(good, "NES", "Good", null),
        };

        var result = new DriveSendTarget().Send(files, Profile(), _driveDir, ConflictPolicy.Skip);

        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Copied);
        Assert.True(File.Exists(Path.Combine(_driveDir, "Roms", "FC", "good.nes")));
    }

    [Fact]
    public void Send_UnmappedPlatformSkippedWhenNotAllowed()
    {
        var source = CreateSource("game.iso", "x");

        var result = new DriveSendTarget().Send(
            new[] { new SendFile(source, "PS2", "Game", null) },
            Profile(allowUnmapped: false), _driveDir, ConflictPolicy.Skip);

        Assert.Equal(0, result.Copied);
        Assert.Equal(1, result.Skipped);
        Assert.Contains(result.Warnings, w => w.Contains("PS2"));
    }

    [Fact]
    public void Send_MultipleDiscsGoToSameFolder()
    {
        var disc1 = CreateSource("Game (Disc 1).bin", "1");
        var disc2 = CreateSource("Game (Disc 2).bin", "2");

        var result = new DriveSendTarget().Send(
            new[]
            {
                new SendFile(disc1, "PlayStation", "Game", null),
                new SendFile(disc2, "PlayStation", "Game", null),
            },
            Profile(), _driveDir, ConflictPolicy.Skip);

        Assert.Equal(2, result.Copied);
        Assert.True(File.Exists(Path.Combine(_driveDir, "Roms", "PS", "Game (Disc 1).bin")));
        Assert.True(File.Exists(Path.Combine(_driveDir, "Roms", "PS", "Game (Disc 2).bin")));
    }
}
