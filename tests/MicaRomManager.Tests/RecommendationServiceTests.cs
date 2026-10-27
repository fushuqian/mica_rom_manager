using MicaRomManager.Models;
using MicaRomManager.Services.Recommendation;
using Xunit;

namespace MicaRomManager.Tests;

public class RecommendationServiceTests
{
    [Fact]
    public void Recommend_PrefersSamePlatformUnplayedAndExcludesSeed()
    {
        var seed = new Game { Id = 1, Title = "种子", Platform = "PlayStation", Genre = "角色扮演", Developer = "Square" };
        var same = new Game
        {
            Id = 2, Title = "相似游戏", Platform = "PlayStation", Genre = "角色扮演",
            Developer = "Square", Status = GameStatus.NotPlayed,
        };
        var other = new Game { Id = 3, Title = "别的游戏", Platform = "SNES", Status = GameStatus.Finished };

        var result = RecommendationService.Recommend(seed, new[] { seed, same, other });

        Assert.Single(result);
        Assert.Equal(same.Id, result[0].Game.Id);
        Assert.Equal(5, result[0].Score);
        Assert.Contains("同平台", result[0].Reason);
        Assert.Contains("未玩过", result[0].Reason);
    }

    [Fact]
    public void Recommend_EmptyWhenNoCandidates()
    {
        var seed = new Game { Id = 1, Title = "种子", Platform = "NES" };
        Assert.Empty(RecommendationService.Recommend(seed, new[] { seed }));
    }
}
