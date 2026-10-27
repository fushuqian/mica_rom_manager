using MicaRomManager.Models;
using MicaRomManager.Services.Ai;
using MicaRomManager.Services.Scraping;
using Xunit;

namespace MicaRomManager.Tests;

public class AiScraperTests
{
    [Fact]
    public void AiJson_ParsesMarkdownFenceAndTrailingComma()
    {
        var value = AiJson.Parse<Payload>("```json\n{\"title\":\"超级银河战士\",}\n```");

        Assert.NotNull(value);
        Assert.Equal("超级银河战士", value!.Title);
    }

    [Fact]
    public void AiJson_InvalidTextReturnsNullAndSnippet()
    {
        Assert.Null(AiJson.Parse<Payload>("模型说：我不知道"));
        Assert.Contains("我不知道", AiJson.Snippet("模型说：我不知道"));
    }

    [Fact]
    public void FileNameScraper_ParsesRegionVersionAndTitle()
    {
        var game = new Game { Title = "Super Metroid", RomPath = @"C:\roms\Super Metroid (Japan) (Rev 1).sfc" };

        var result = FileNameScraper.Scrape(game);

        Assert.Equal("Super Metroid", result.Title);
        Assert.Equal("日版", result.Region);
        Assert.Null(result.ReleaseYear);
        Assert.Contains("Rev 1", result.Summary);
    }

    [Fact]
    public void FileNameScraper_ParsesDiscAndRegion()
    {
        var game = new Game { RomPath = @"C:\roms\最终幻想7 (USA) (Disc 1).bin" };

        var result = FileNameScraper.Scrape(game);

        Assert.Equal("最终幻想7", result.Title);
        Assert.Equal("美版", result.Region);
    }

    [Fact]
    public void FileNameScraper_ParsesMultiLanguage()
    {
        var game = new Game { RomPath = @"C:\roms\Demo Game (Europe) (En,Fr,De).zip" };

        var result = FileNameScraper.Scrape(game);

        Assert.Equal("欧版", result.Region);
        Assert.Contains("英语", result.Language);
        Assert.Contains("法语", result.Language);
        Assert.Contains("德语", result.Language);
    }

    [Fact]
    public void Merge_FillEmptyKeepsExistingAndFillsMissing()
    {
        var game = new Game { Title = "旧标题", Region = "" };
        var local = new ScrapeResult { Title = "新标题", Region = "日版" };
        var ai = new ScrapeResult { Title = "AI 标题", Genre = "动作" };

        var merged = ScraperService.Merge(game, local, ai, overwrite: false);

        Assert.Equal("旧标题", merged.Title);
        Assert.Equal("日版", merged.Region);
        Assert.Equal("动作", merged.Genre);
        Assert.True(merged.Apply);
    }

    [Fact]
    public void Merge_OverwritePrefersAi()
    {
        var game = new Game { Title = "旧标题", Genre = "旧类型" };
        var local = new ScrapeResult { Title = "本地标题" };
        var ai = new ScrapeResult { Title = "AI 标题", Genre = "动作" };

        var merged = ScraperService.Merge(game, local, ai, overwrite: true);

        Assert.Equal("AI 标题", merged.Title);
        Assert.Equal("动作", merged.Genre);
    }

    private sealed class Payload
    {
        public string Title { get; set; } = "";
    }
}
