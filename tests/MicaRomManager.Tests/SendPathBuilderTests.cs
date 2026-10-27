using System.IO;
using MicaRomManager.Models;
using MicaRomManager.Services.Devices;
using Xunit;

namespace MicaRomManager.Tests;

public class SendPathBuilderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-send-" + Guid.NewGuid().ToString("N"));

    public SendPathBuilderTests() => Directory.CreateDirectory(_dir);

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

    private static DeviceProfile Profile(string template = "{root}/{platform}") => new()
    {
        Id = "test",
        Name = "测试设备",
        RootFolder = "Roms",
        FolderTemplate = template,
        AllowUnmappedPlatforms = false,
        PlatformFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PlayStation"] = "PS",
            ["NES"] = "FC",
        },
    };

    [Fact]
    public void ResolveFolder_UsesPlatformAlias()
    {
        var folder = SendPathBuilder.ResolveFolder(
            Profile(), _dir, new Game { Title = "X", Platform = "PlayStation" });

        Assert.Equal(Path.Combine(_dir, "Roms", "PS"), folder);
    }

    [Fact]
    public void ResolveFolder_SupportsTitleSegment()
    {
        var folder = SendPathBuilder.ResolveFolder(
            Profile("{root}/{platform}/{title}"), _dir,
            new Game { Title = "Final Fantasy VII", Platform = "PlayStation" });

        Assert.Equal(Path.Combine(_dir, "Roms", "PS", "Final Fantasy VII"), folder);
    }

    [Fact]
    public void ResolveFolder_SanitizesInvalidCharacters()
    {
        var folder = SendPathBuilder.ResolveFolder(
            Profile("{root}/{platform}/{title}"), _dir,
            new Game { Title = "A/B:C*?", Platform = "NES" });

        Assert.Equal(Path.Combine(_dir, "Roms", "FC", "A_B_C__"), folder);
    }

    [Fact]
    public void ResolveFolder_DoesNotEscapeDriveRoot()
    {
        var folder = SendPathBuilder.ResolveFolder(
            Profile("{root}/../../evil"), _dir, new Game { Title = "X", Platform = "NES" });

        Assert.StartsWith(Path.GetFullPath(_dir), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveFolder_UnknownPlatformFallsBackToName()
    {
        var folder = SendPathBuilder.ResolveFolder(
            Profile(), _dir, new Game { Title = "X", Platform = "ATARI2600" });

        Assert.Equal(Path.Combine(_dir, "Roms", "ATARI2600"), folder);
    }

    [Fact]
    public void IsPlatformMapped_IsCaseInsensitive()
    {
        Assert.True(SendPathBuilder.IsPlatformMapped(Profile(), "playstation"));
        Assert.False(SendPathBuilder.IsPlatformMapped(Profile(), "PS2"));
    }
}
