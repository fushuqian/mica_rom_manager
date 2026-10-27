using System.Text.Json.Serialization;
using MicaRomManager.Data;
using MicaRomManager.Models;
using MicaRomManager.Services.Ai;

namespace MicaRomManager.Services.Scraping;

/// <summary>用 OpenAI 兼容接口补全单条游戏元数据；只发送文本，不上传 ROM。</summary>
public sealed class AiMetadataProvider
{
    public async Task<ScrapeResult> ScrapeAsync(Game game, CancellationToken token)
    {
        var result = new ScrapeResult { GameId = game.Id, Source = "AI" };
        if (!AppServices.Ai.Options.IsConfigured)
        {
            result.Error = "还没有配置 AI，请先在 设置 → AI 里填写 BaseUrl、API Key 和模型。";
            return result;
        }

        var prompt = AiPrompts.MetadataUser(game,
            AppServices.Lookups.List(LookupRepository.Region),
            AppServices.Lookups.List(LookupRepository.Language),
            AppServices.Lookups.List(LookupRepository.Genre));

        var response = await AppServices.Ai.CompleteAsync(AiPrompts.MetadataSystem, prompt, token);
        if (!response.Success)
        {
            result.Error = response.Error;
            return result;
        }

        var payload = AiJson.Parse<AiMetadataResponse>(response.Content);
        if (payload == null)
        {
            result.Error = $"返回内容不是有效 JSON：{AiJson.Snippet(response.Content)}";
            return result;
        }

        result.Title = Clean(payload.Title);
        result.Genre = Clean(payload.Genre);
        result.Region = FileNameScraper.NormalizeRegion(payload.Region);
        result.Language = FileNameScraper.NormalizeLanguage(payload.Language);
        result.Publisher = Clean(payload.Publisher);
        result.Developer = Clean(payload.Developer);
        result.Summary = Clean(payload.Summary, 300);

        if (payload.ReleaseYear is int year && year >= 1970 && year <= DateTime.Now.Year + 2)
            result.ReleaseYear = year;

        if (result.Title.Length == 0 && result.Genre.Length == 0 && result.Summary.Length == 0)
            result.Error = "模型没有返回可用内容。";

        return result;
    }

    private static string Clean(string? value, int maxLength = 120)
    {
        var text = (value ?? "").Trim().Trim('"', '“', '”', '\'', '「', '」');
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private sealed class AiMetadataResponse
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("genre")] public string? Genre { get; set; }
        [JsonPropertyName("region")] public string? Region { get; set; }
        [JsonPropertyName("language")] public string? Language { get; set; }
        [JsonPropertyName("release_year")] public int? ReleaseYear { get; set; }
        [JsonPropertyName("publisher")] public string? Publisher { get; set; }
        [JsonPropertyName("developer")] public string? Developer { get; set; }
        [JsonPropertyName("summary")] public string? Summary { get; set; }
    }
}
