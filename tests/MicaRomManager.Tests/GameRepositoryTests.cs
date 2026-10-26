using System.IO;
using MicaRomManager.Data;
using MicaRomManager.Models;
using Xunit;

namespace MicaRomManager.Tests;

public class GameRepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-games-" + Guid.NewGuid().ToString("N"));
    private readonly GameRepository _games;

    public GameRepositoryTests()
    {
        Directory.CreateDirectory(_dir);
        _games = new GameRepository(new Database(Path.Combine(_dir, "t.db")));
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

    [Fact]
    public void Rating_RoundTripsAndClamps()
    {
        var game = new Game { Title = "A", RomPath = Path.Combine(_dir, "a.chd"), Rating = 4 };
        _games.Insert(game);

        Assert.Equal(4, _games.ListAll().Single().Rating);

        game.Rating = 9;
        _games.Update(game);
        Assert.Equal(5, _games.ListAll().Single().Rating);
    }

    [Fact]
    public void AddPlayStats_MarksNotPlayedAsPlaying()
    {
        var game = new Game { Title = "B", RomPath = Path.Combine(_dir, "b.chd") };
        _games.Insert(game);

        _games.AddPlayStats(game.Id, 30);

        var stored = _games.ListAll().Single();
        Assert.Equal(1, stored.PlayCount);
        Assert.Equal(30L, stored.PlaySeconds);
        Assert.Equal(GameStatus.Playing, stored.Status);
    }

    [Fact]
    public void AddPlayStats_KeepsManualStatus()
    {
        var game = new Game
        {
            Title = "C",
            RomPath = Path.Combine(_dir, "c.chd"),
            Status = GameStatus.Finished,
        };
        _games.Insert(game);

        _games.AddPlayStats(game.Id, 10);

        Assert.Equal(GameStatus.Finished, _games.ListAll().Single().Status);
    }
}
