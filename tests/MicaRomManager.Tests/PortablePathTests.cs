using System.IO;
using MicaRomManager.Data;
using MicaRomManager.Models;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class PortablePathTests
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "mica-app-test");

    [Fact]
    public void ToStored_UnderBaseDirectory_ReturnsRelative()
    {
        var path = Path.Combine(_base, "game_library", "PS2", "x.chd");

        Assert.Equal(Path.Combine("game_library", "PS2", "x.chd"),
            PortablePath.ToStored(path, true, _base));
    }

    [Fact]
    public void ToStored_PortableOff_KeepsAbsolute()
    {
        var path = Path.Combine(_base, "game_library", "PS2", "x.chd");

        Assert.Equal(path, PortablePath.ToStored(path, false, _base));
    }

    [Fact]
    public void Expand_RelativePath_CombinesWithBase()
    {
        var stored = Path.Combine("game_library", "PS2", "x.chd");

        Assert.Equal(Path.Combine(_base, "game_library", "PS2", "x.chd"),
            PortablePath.Expand(stored, _base));
    }

    [Fact]
    public void Expand_AbsolutePath_Unchanged()
    {
        var path = Path.Combine(_base, "game_library", "x.chd");

        Assert.Equal(path, PortablePath.Expand(path, _base));
    }

    [Fact]
    public void ToStored_ThenExpand_RoundTrips()
    {
        var path = Path.Combine(_base, "data", "covers", "a.png");

        var stored = PortablePath.ToStored(path, true, _base);

        Assert.Equal(path, PortablePath.Expand(stored, _base));
    }

    [Fact]
    public void MultiLine_RoundTrips()
    {
        var a = Path.Combine(AppContext.BaseDirectory, "game_library", "a.chd");
        var b = Path.Combine(AppContext.BaseDirectory, "game_library", "b.chd");

        var stored = PortablePath.ToStoredMultiLine($"{a}\n{b}", true);

        Assert.Equal($"{a}\n{b}", PortablePath.ExpandMultiLine(stored));
    }

    [Fact]
    public void RelocateLibraryPaths_MovesEvenWhenOldFileStillExists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mica-relocate-" + Guid.NewGuid().ToString("N"));
        var oldRoot = Path.Combine(dir, "old");
        var newRoot = Path.Combine(dir, "new");
        Directory.CreateDirectory(Path.Combine(oldRoot, "PS2"));
        Directory.CreateDirectory(Path.Combine(newRoot, "PS2"));
        var oldFile = Path.Combine(oldRoot, "PS2", "x.chd");
        var newFile = Path.Combine(newRoot, "PS2", "x.chd");
        File.WriteAllText(oldFile, "old");
        File.WriteAllText(newFile, "new");

        try
        {
            var repo = new GameRepository(new Database(Path.Combine(dir, "t.db")));
            repo.Insert(new Game
            {
                Title = "x",
                Platform = "PS2",
                RomPath = oldFile,
            });

            var changed = repo.RelocateLibraryPaths(oldRoot, newRoot);

            Assert.Equal(1, changed);
            Assert.Equal(newFile, repo.ListAll().Single().RomPath);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // 临时目录清理失败不影响测试结果
            }
        }
    }

    [Fact]
    public void RelocateLibraryPaths_MovesRecordToNewRoot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mica-relocate-" + Guid.NewGuid().ToString("N"));
        var oldRoot = Path.Combine(dir, "old");
        var newRoot = Path.Combine(dir, "new");
        Directory.CreateDirectory(Path.Combine(oldRoot, "PS2"));
        Directory.CreateDirectory(Path.Combine(newRoot, "PS2"));
        var newFile = Path.Combine(newRoot, "PS2", "x.chd");
        File.WriteAllText(newFile, "x");

        try
        {
            var repo = new GameRepository(new Database(Path.Combine(dir, "t.db")));
            repo.Insert(new Game
            {
                Title = "x",
                Platform = "PS2",
                RomPath = Path.Combine(oldRoot, "PS2", "x.chd"),
            });

            var changed = repo.RelocateLibraryPaths(oldRoot, newRoot);

            Assert.Equal(1, changed);
            Assert.Equal(newFile, repo.ListAll().Single().RomPath);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // 临时目录清理失败不影响测试结果
            }
        }
    }
}
