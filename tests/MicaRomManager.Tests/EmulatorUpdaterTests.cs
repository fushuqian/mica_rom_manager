using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class EmulatorUpdaterTests
{
    [Fact]
    public void PickWindowsAsset_PrefersWin64ArchiveOverOthers()
    {
        var assets = new List<(string? Name, string? Url)>
        {
            ("emulator-3ds.zip", "https://x/3ds.zip"),
            ("emulator-win64.zip", "https://x/win64.zip"),
            ("emulator-ubuntu.tar.gz", "https://x/linux.tar.gz"),
        };

        var picked = EmulatorUpdater.PickWindowsAsset(assets);

        Assert.NotNull(picked);
        Assert.Equal("emulator-win64.zip", picked!.Value.Name);
    }

    [Fact]
    public void PickWindowsAsset_FallsBackToZipThen7z()
    {
        var zipFirst = EmulatorUpdater.PickWindowsAsset(new List<(string?, string?)>
        {
            ("emulator.7z", "https://x/a.7z"),
            ("emulator.zip", "https://x/a.zip"),
        });
        Assert.Equal("emulator.zip", zipFirst!.Value.Name);

        var only7z = EmulatorUpdater.PickWindowsAsset(new List<(string?, string?)>
        {
            ("emulator.7z", "https://x/a.7z"),
        });
        Assert.Equal("emulator.7z", only7z!.Value.Name);
    }

    [Fact]
    public void PickWindowsAsset_IgnoresAssetsWithoutUrlAndHandlesEmpty()
    {
        Assert.Null(EmulatorUpdater.PickWindowsAsset(Array.Empty<(string?, string?)>()));

        var picked = EmulatorUpdater.PickWindowsAsset(new List<(string?, string?)>
        {
            ("emulator-win.zip", "https://x/win.zip"),
            ("notes.txt", "https://x/notes.txt"),
        });
        Assert.Equal("emulator-win.zip", picked!.Value.Name);
    }

    [Theory]
    [InlineData("v0.10.0", "0.9.0", true)]   // 数字段比较：0.10 > 0.9（字典序会判错）
    [InlineData("v0.9.0", "0.10.0", false)]
    [InlineData("1.2.3", "1.2.3", false)]
    [InlineData("1.2.4", "1.2.3", true)]
    [InlineData("2.0", null, true)]
    [InlineData(null, "2.0", false)]
    [InlineData("v1.0.0-beta", "1.0.0", false)]
    public void IsNewer_ComparesByNumericSegments(string? latest, string? current, bool expected)
    {
        Assert.Equal(expected, EmulatorUpdater.IsNewer(latest, current));
    }
}
