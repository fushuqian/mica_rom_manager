using MicaRomManager.Models;
using MicaRomManager.Services.Ai;
using Xunit;

namespace MicaRomManager.Tests;

public class AiSearchTests
{
    [Fact]
    public void IsDeepSeekEndpoint_AcceptsOfficialHosts()
    {
        Assert.True(AiSearchOptions.IsDeepSeekEndpoint("https://api.deepseek.com/v1"));
        Assert.True(AiSearchOptions.IsDeepSeekEndpoint("https://api.deepseek.com"));
        Assert.False(AiSearchOptions.IsDeepSeekEndpoint("https://api.openai.com/v1"));
        Assert.False(AiSearchOptions.IsDeepSeekEndpoint("http://127.0.0.1:11434/v1"));
        Assert.False(AiSearchOptions.IsDeepSeekEndpoint(""));
    }

    [Fact]
    public void ParseResults_ReadsDeepSeekSearchResponse()
    {
        const string json = """
            {"results":[
              {"title":"超级银河战士","url":"https://example.com/super-metroid","snippet":"短摘要",
               "summary":"长摘要","site_name":"Example","date":"2026-01-02T00:00:00+00:00"},
              {"title":"SFC 游戏列表","url":"https://example.com/list","snippet":"列表",
               "summary":"列表摘要","site_name":"List","date":null}
            ]}
            """;

        var results = AiSearchService.ParseResults(json);

        Assert.Equal(2, results.Count);
        Assert.Equal("超级银河战士", results[0].Title);
        Assert.Equal("Example", results[0].SiteName);
        Assert.Equal("2026-01-02T00:00:00+00:00", results[0].Date);
        Assert.Equal("", results[1].Date);
    }

    [Fact]
    public void BuildContext_UsesSummaryAndRespectsLimit()
    {
        var results = new List<WebSearchResult>
        {
            new("标题一", "https://a", "短一", "摘要一", "站点一", "2026-01-01"),
            new("标题二", "https://b", "短二", "摘要二", "站点二", ""),
        };

        var text = AiSearchService.BuildContext(results, maxChars: 200);

        Assert.Contains("标题一", text);
        Assert.Contains("摘要一", text);
        Assert.DoesNotContain("https://a", text);
    }

    [Fact]
    public void MetadataPrompt_IncludesSearchContext()
    {
        var game = new Game
        {
            Title = "超级银河战士",
            Platform = "SNES",
            RomPath = @"C:\roms\Super Metroid (Japan).sfc",
        };

        var prompt = AiPrompts.MetadataUser(game, Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), "1. 超级银河战士（示例站）\n1994 年由任天堂发行。");

        Assert.Contains("联网搜索结果", prompt);
        Assert.Contains("1994 年由任天堂发行", prompt);
    }
}
