using System.IO;
using MicaRomManager.Data;
using MicaRomManager.Models;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class LibraryOrganizerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-organizer-" + Guid.NewGuid().ToString("N"));

    public LibraryOrganizerTests() => Directory.CreateDirectory(_dir);

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

    private Game NewGame(string title, string region = "", string language = "", params string[] extraDiscs)
    {
        var game = new Game
        {
            Title = title,
            Region = region,
            Language = language,
            RomPath = Path.Combine(_dir, "disc1.chd"),
        };
        if (extraDiscs.Length > 0) game.DiscPaths = string.Join("\n", extraDiscs);
        return game;
    }

    [Fact]
    public void BuildFileName_SingleDiscHasNoDiscNumber()
    {
        var game = NewGame("新鬼武者", "欧版", "英语");

        Assert.Equal("新鬼武者 (欧版)(英语).chd", LibraryOrganizer.BuildFileName(game, 0, game.RomPath));
    }

    [Fact]
    public void BuildFileName_MultiDiscAddsDiscNumber()
    {
        var game = NewGame("新鬼武者", "欧版", "英语", Path.Combine(_dir, "disc2.chd"));

        Assert.Equal("新鬼武者 Disc-1 (欧版)(英语).chd", LibraryOrganizer.BuildFileName(game, 0, game.RomPath));
        Assert.Equal("新鬼武者 Disc-2 (欧版)(英语).chd", LibraryOrganizer.BuildFileName(game, 1, game.RomPath));
    }

    [Fact]
    public void BuildFileName_OmitsEmptyRegionAndLanguage()
    {
        var game = NewGame("新鬼武者", "", "英语", Path.Combine(_dir, "disc2.chd"));
        Assert.Equal("新鬼武者 Disc-1 (英语).chd", LibraryOrganizer.BuildFileName(game, 0, game.RomPath));

        game = NewGame("新鬼武者", "欧版", "", Path.Combine(_dir, "disc2.chd"));
        Assert.Equal("新鬼武者 Disc-1 (欧版).chd", LibraryOrganizer.BuildFileName(game, 0, game.RomPath));

        game = NewGame("新鬼武者", "", "", Path.Combine(_dir, "disc2.chd"));
        Assert.Equal("新鬼武者 Disc-1.chd", LibraryOrganizer.BuildFileName(game, 0, game.RomPath));
    }

    [Fact]
    public void BuildFileName_JoinsMultipleValuesWithComma()
    {
        var game = NewGame("新鬼武者", "", "简体中文,英语");

        Assert.Equal("新鬼武者 (简体中文,英语).chd", LibraryOrganizer.BuildFileName(game, 0, game.RomPath));
    }

    [Fact]
    public void BuildFileName_SanitizesInvalidCharacters()
    {
        var game = NewGame("新:鬼?武者*", "欧版", "英语");

        Assert.Equal("新_鬼_武者_ (欧版)(英语).chd", LibraryOrganizer.BuildFileName(game, 0, game.RomPath));
    }

    [Fact]
    public void BuildFileName_EmptyTitleReturnsEmpty()
    {
        var game = NewGame("  ", "欧版", "英语");

        Assert.Equal("", LibraryOrganizer.BuildFileName(game, 0, game.RomPath));
    }

    [Fact]
    public void RemoveMissing_DeletesOnlyEntriesWithoutAnyExistingFile()
    {
        var repo = new GameRepository(new Database(Path.Combine(_dir, "cleanup.db")));

        var existing = Path.Combine(_dir, "exists.chd");
        var existing2 = Path.Combine(_dir, "exists2.chd");
        File.WriteAllText(existing, "x");
        File.WriteAllText(existing2, "y");

        repo.Insert(new Game { Title = "kept", RomPath = existing });
        repo.Insert(new Game { Title = "missing", RomPath = Path.Combine(_dir, "gone.chd") });
        repo.Insert(new Game
        {
            Title = "partial",
            RomPath = existing2,
            DiscPaths = Path.Combine(_dir, "gone2.chd"),
        });
        repo.Insert(new Game { Title = "empty", RomPath = "" });

        var removed = LibraryOrganizer.RemoveMissing(repo, repo.ListAll());

        Assert.Equal(1, removed);
        var remaining = repo.ListAll();
        Assert.Equal(3, remaining.Count);
        Assert.DoesNotContain(remaining, g => g.Title == "missing");
    }

    [Fact]
    public void Organize_RenamesLibraryFileAndUpdatesDatabase()
    {
        var repo = new GameRepository(new Database(Path.Combine(_dir, "organize.db")));

        var source = Path.Combine(_dir, "old name.chd");
        File.WriteAllText(source, "x");
        repo.Insert(new Game
        {
            Title = "新鬼武者",
            Platform = "PS2",
            Region = "欧版",
            Language = "英语",
            RomPath = source,
        });

        var result = LibraryOrganizer.Organize(repo, repo.ListAll(), _dir);

        var target = Path.Combine(_dir, "新鬼武者 (欧版)(英语).chd");
        Assert.Equal(1, result.Renamed);
        Assert.True(File.Exists(target));
        Assert.False(File.Exists(source));
        Assert.Equal(target, repo.ListAll().Single().RomPath);
    }

    [Fact]
    public void Organize_AddsSequenceWhenTargetExists()
    {
        var repo = new GameRepository(new Database(Path.Combine(_dir, "conflict.db")));

        var source = Path.Combine(_dir, "old name.chd");
        File.WriteAllText(source, "x");
        File.WriteAllText(Path.Combine(_dir, "新鬼武者 (欧版)(英语).chd"), "occupied");

        repo.Insert(new Game
        {
            Title = "新鬼武者",
            Platform = "PS2",
            Region = "欧版",
            Language = "英语",
            RomPath = source,
        });

        var result = LibraryOrganizer.Organize(repo, repo.ListAll(), _dir);

        var target = Path.Combine(_dir, "新鬼武者 (欧版)(英语) (2).chd");
        Assert.Equal(1, result.Renamed);
        Assert.True(File.Exists(target));
        Assert.Equal(target, repo.ListAll().Single().RomPath);
    }
}
