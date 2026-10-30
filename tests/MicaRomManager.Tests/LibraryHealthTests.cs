using MicaRomManager.Models;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class LibraryHealthTests
{
    private static Game GameAt(string romPath, string discPaths = "") => new()
    {
        Title = "x",
        RomPath = romPath,
        DiscPaths = discPaths,
    };

    [Fact]
    public void Inspect_FlagsUnavailableRootOnce()
    {
        var games = new[]
        {
            GameAt(@"Z:\Roms\a.chd"),
            GameAt(@"Z:\Roms\b.chd"),
            GameAt(@"C:\Roms\c.chd"),
        };

        var report = LibraryHealth.Inspect(games,
            root => !root.StartsWith(@"Z:\", StringComparison.OrdinalIgnoreCase),
            _ => false);

        Assert.Equal(new[] { @"Z:\" }, report.UnavailableRoots);
        Assert.Equal(3, report.MissingGames);
        Assert.False(report.CanCleanMissing);
    }

    [Fact]
    public void Inspect_AllRootsAvailable_CanClean()
    {
        var games = new[] { GameAt(@"C:\Roms\a.chd") };

        var report = LibraryHealth.Inspect(games, _ => true, _ => true);

        Assert.Empty(report.UnavailableRoots);
        Assert.True(report.CanCleanMissing);
        Assert.Equal(0, report.MissingGames);
    }

    [Fact]
    public void Inspect_ChecksEveryDiscRoot()
    {
        var games = new[]
        {
            GameAt(@"C:\Roms\a.chd", @"Z:\Roms\a2.chd"),
        };

        var report = LibraryHealth.Inspect(games,
            root => !root.StartsWith(@"Z:\", StringComparison.OrdinalIgnoreCase),
            _ => true);

        Assert.Equal(new[] { @"Z:\" }, report.UnavailableRoots);
    }

    [Fact]
    public void Inspect_IgnoresRelativeAndEmptyPaths()
    {
        var games = new[]
        {
            GameAt(@"roms\a.nes"),
            GameAt(""),
        };

        var report = LibraryHealth.Inspect(games, _ => false, _ => false);

        Assert.Empty(report.UnavailableRoots);
        Assert.Equal(2, report.MissingGames);
        Assert.True(report.CanCleanMissing);
    }

    [Fact]
    public void Inspect_EmptyLibraryHasNoWarning()
    {
        var report = LibraryHealth.Inspect(Array.Empty<Game>());

        Assert.Equal(0, report.TotalGames);
        Assert.Equal(0, report.MissingGames);
        Assert.Empty(report.UnavailableRoots);
        Assert.True(report.CanCleanMissing);
    }
}
