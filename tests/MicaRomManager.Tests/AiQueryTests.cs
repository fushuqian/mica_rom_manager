using MicaRomManager.Models;
using MicaRomManager.Services.Ai;
using Xunit;

namespace MicaRomManager.Tests;

public class AiQueryTests
{
    [Fact]
    public void AiJson_ParsesSnakeCaseQueryPlan()
    {
        var plan = AiJson.Parse<AiQueryPlan>(
            "{\"intent\":\"filter\",\"filter\":{\"platform\":\"PlayStation\",\"status\":\"已通关\",\"year_min\":1995,\"only_missing\":true},\"explanation\":\"测试\"}");

        Assert.NotNull(plan);
        Assert.Equal("filter", plan!.Intent);
        Assert.Equal("PlayStation", plan.Filter.Platform);
        Assert.Equal(1995, plan.Filter.YearMin);
        Assert.True(plan.Filter.OnlyMissing);
    }

    [Fact]
    public void Matcher_AppliesPlatformStatusAndYear()
    {
        var game = new Game
        {
            Title = "最终幻想7",
            Platform = "PlayStation",
            Status = GameStatus.Finished,
            ReleaseYear = 1997,
        };
        var filter = new AiQueryFilter { Platform = "PlayStation", Status = GameStatus.Finished, YearMin = 1995, YearMax = 2000 };

        Assert.True(AiQueryMatcher.Matches(game, filter));

        filter.YearMin = 2001;
        Assert.False(AiQueryMatcher.Matches(game, filter));
    }

    [Fact]
    public void Matcher_ExcludesFavoritesWhenFavoriteFalse()
    {
        var game = new Game { Title = "A", Favorite = true };

        Assert.False(AiQueryMatcher.Matches(game, new AiQueryFilter { Favorite = false }));
        Assert.True(AiQueryMatcher.Matches(game, new AiQueryFilter { Favorite = true }));
    }

    [Fact]
    public void Matcher_KeywordSearchesTitleAndTags()
    {
        var game = new Game { Title = "超级银河战士", Tags = "横版,探索" };

        Assert.True(AiQueryMatcher.Matches(game, new AiQueryFilter { Keyword = "银河" }));
        Assert.True(AiQueryMatcher.Matches(game, new AiQueryFilter { Tag = "探索" }));
        Assert.False(AiQueryMatcher.Matches(game, new AiQueryFilter { Tag = "射击" }));
    }
}
