using MicaRomManager.Models;
using Xunit;

namespace MicaRomManager.Tests;

public class BatchEditChangesTests
{
    [Fact]
    public void ApplyTo_OnlyTouchesCheckedFields()
    {
        var game = new Game
        {
            Title = "鬼武者",
            Platform = "PS2",
            Genre = "动作",
            Region = "日版",
            Language = "日语",
            ReleaseYear = 2001,
            Publisher = "Capcom",
            Developer = "Capcom",
            Status = GameStatus.NotPlayed,
            Tags = "经典",
            Notes = "旧备注",
            EmulatorId = 3,
        };

        new BatchEditChanges { ApplyStatus = true, Status = GameStatus.Finished }.ApplyTo(game);

        Assert.Equal(GameStatus.Finished, game.Status);
        Assert.Equal("PS2", game.Platform);
        Assert.Equal("动作", game.Genre);
        Assert.Equal("日版", game.Region);
        Assert.Equal("日语", game.Language);
        Assert.Equal(2001, game.ReleaseYear);
        Assert.Equal("Capcom", game.Publisher);
        Assert.Equal("Capcom", game.Developer);
        Assert.Equal("经典", game.Tags);
        Assert.Equal("旧备注", game.Notes);
        Assert.Equal(3, game.EmulatorId);
    }

    [Fact]
    public void ApplyTo_WritesAllCheckedFields()
    {
        var game = new Game();

        new BatchEditChanges
        {
            ApplyPlatform = true,
            Platform = "PS3",
            ApplyGenre = true,
            Genre = "冒险",
            ApplyRegion = true,
            Region = "美版",
            ApplyLanguage = true,
            Language = "英语,法语",
            ApplyReleaseYear = true,
            ReleaseYear = 2005,
            ApplyPublisher = true,
            Publisher = "Sony",
            ApplyDeveloper = true,
            Developer = "Team",
            ApplyStatus = true,
            Status = GameStatus.Playing,
            ApplyEmulator = true,
            EmulatorId = 42,
        }.ApplyTo(game);

        Assert.Equal("PS3", game.Platform);
        Assert.Equal("冒险", game.Genre);
        Assert.Equal("美版", game.Region);
        Assert.Equal("英语,法语", game.Language);
        Assert.Equal(2005, game.ReleaseYear);
        Assert.Equal("Sony", game.Publisher);
        Assert.Equal("Team", game.Developer);
        Assert.Equal(GameStatus.Playing, game.Status);
        Assert.Equal(42, game.EmulatorId);
    }

    [Fact]
    public void ApplyTo_NullEmulatorClearsBinding()
    {
        var game = new Game { EmulatorId = 7 };

        new BatchEditChanges { ApplyEmulator = true, EmulatorId = null }.ApplyTo(game);

        Assert.Null(game.EmulatorId);
    }

    [Fact]
    public void ApplyTo_AddsAndRemovesTagsCaseInsensitively()
    {
        var game = new Game { Tags = "动作,RPG" };

        new BatchEditChanges
        {
            ApplyTagsAdd = true,
            TagsAdd = "PS2经典，动作",
            ApplyTagsRemove = true,
            TagsRemove = "rpg",
        }.ApplyTo(game);

        Assert.Equal("动作,PS2经典", game.Tags);
    }

    [Fact]
    public void ApplyTo_AppendsNotes()
    {
        var game = new Game { Notes = "原备注" };

        new BatchEditChanges { ApplyNotes = true, NotesAppend = true, Notes = "补充" }.ApplyTo(game);

        Assert.Equal("原备注" + Environment.NewLine + "补充", game.Notes);
    }

    [Fact]
    public void ApplyTo_OverwritesNotes()
    {
        var game = new Game { Notes = "原备注" };

        new BatchEditChanges { ApplyNotes = true, NotesAppend = false, Notes = "覆盖" }.ApplyTo(game);

        Assert.Equal("覆盖", game.Notes);
    }

    [Fact]
    public void ApplyTo_EmptyChangesLeavesGameUntouched()
    {
        var game = new Game
        {
            Title = "鬼武者",
            Platform = "PS2",
            Status = GameStatus.NotPlayed,
            Tags = "经典",
            Notes = "备注",
        };

        var changes = new BatchEditChanges();
        changes.ApplyTo(game);

        Assert.False(changes.HasAny);
        Assert.Equal("鬼武者", game.Title);
        Assert.Equal("PS2", game.Platform);
        Assert.Equal(GameStatus.NotPlayed, game.Status);
        Assert.Equal("经典", game.Tags);
        Assert.Equal("备注", game.Notes);
    }
}
