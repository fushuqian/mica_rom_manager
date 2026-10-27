using System.Text.Json.Serialization;

namespace MicaRomManager.Models;

/// <summary>模型返回的受控查询计划；本地校验后才会应用。</summary>
public sealed class AiQueryPlan
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "filter";
    [JsonPropertyName("filter")] public AiQueryFilter Filter { get; set; } = new();
    [JsonPropertyName("sort_field")] public string SortField { get; set; } = "";
    [JsonPropertyName("sort_direction")] public string SortDirection { get; set; } = "asc";
    [JsonPropertyName("action")] public AiQueryAction? Action { get; set; }
    [JsonPropertyName("explanation")] public string Explanation { get; set; } = "";
}

public sealed class AiQueryFilter
{
    [JsonPropertyName("platform")] public string Platform { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("region")] public string Region { get; set; } = "";
    [JsonPropertyName("language")] public string Language { get; set; } = "";
    [JsonPropertyName("genre")] public string Genre { get; set; } = "";
    [JsonPropertyName("developer")] public string Developer { get; set; } = "";
    [JsonPropertyName("publisher")] public string Publisher { get; set; } = "";
    [JsonPropertyName("tag")] public string Tag { get; set; } = "";
    [JsonPropertyName("keyword")] public string Keyword { get; set; } = "";
    [JsonPropertyName("favorite")] public bool? Favorite { get; set; }
    [JsonPropertyName("only_missing")] public bool OnlyMissing { get; set; }
    [JsonPropertyName("year_min")] public int? YearMin { get; set; }
    [JsonPropertyName("year_max")] public int? YearMax { get; set; }
}

public sealed class AiQueryAction
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}

/// <summary>计划条件的本地匹配，供预览统计与主列表筛选共用。</summary>
public static class AiQueryMatcher
{
    public static bool Matches(Game game, AiQueryFilter filter)
    {
        if (!Empty(filter.Platform) && !Same(game.Platform, filter.Platform)) return false;
        if (!Empty(filter.Status) && !Same(game.Status, filter.Status)) return false;
        if (!Empty(filter.Region) && !Same(game.Region, filter.Region)) return false;
        if (!Empty(filter.Language) && !Same(game.Language, filter.Language)) return false;
        if (!Empty(filter.Genre) && !Same(game.Genre, filter.Genre)) return false;
        if (!Empty(filter.Developer) && !Same(game.Developer, filter.Developer)) return false;
        if (!Empty(filter.Publisher) && !Same(game.Publisher, filter.Publisher)) return false;
        if (!Empty(filter.Tag) && !SplitTags(game.Tags).Contains(filter.Tag, StringComparer.OrdinalIgnoreCase)) return false;
        if (filter.Favorite == true && !game.Favorite) return false;
        if (filter.Favorite == false && game.Favorite) return false;
        if (filter.OnlyMissing && !game.IsMissing) return false;
        if (filter.YearMin is int min && (game.ReleaseYear ?? int.MinValue) < min) return false;
        if (filter.YearMax is int max && (game.ReleaseYear ?? int.MaxValue) > max) return false;

        var keyword = filter.Keyword.Trim();
        if (keyword.Length == 0) return true;

        return Contains(game.Title, keyword) || Contains(game.RomPath, keyword)
            || Contains(game.Tags, keyword) || Contains(game.Platform, keyword)
            || Contains(game.Genre, keyword) || Contains(game.Developer, keyword);
    }

    public static IEnumerable<string> SplitTags(string? tags) =>
        string.IsNullOrWhiteSpace(tags)
            ? Enumerable.Empty<string>()
            : tags.Split(new[] { ',', '，', ';', '；' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool Empty(string? value) => string.IsNullOrWhiteSpace(value);

    private static bool Same(string left, string right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool Contains(string value, string keyword) =>
        value?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true;
}
