using MicaRomManager.Data;
using MicaRomManager.Models;

namespace MicaRomManager.Services.Ai;

/// <summary>把自然语言转换成受控筛选/操作计划，并做白名单校验。</summary>
public sealed class AiQueryService
{
    public sealed record Result(AiQueryPlan? Plan, string Error);

    private static readonly string[] ActionTypes =
        { "set_status", "toggle_favorite", "add_tag", "remove_tag" };

    public async Task<Result> BuildAsync(string request, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request))
            return new Result(null, "请输入要查询或操作的内容。");
        if (!AppServices.Ai.Options.IsConfigured)
            return new Result(null, "还没有配置 AI，请先在 设置 → AI 里填写 BaseUrl、API Key 和模型。");

        var platforms = AppServices.Lookups.List(LookupRepository.Platform);
        var regions = AppServices.Lookups.List(LookupRepository.Region);
        var languages = AppServices.Lookups.List(LookupRepository.Language);
        var genres = AppServices.Lookups.List(LookupRepository.Genre);
        var developers = AppServices.Lookups.List(LookupRepository.Developer);
        var tags = AppServices.Lookups.List(LookupRepository.Tag);

        var prompt = AiPrompts.QueryUser(request, platforms, GameStatus.All, regions, languages,
            genres, developers, tags);
        var response = await AppServices.Ai.CompleteAsync(AiPrompts.QuerySystem, prompt, token);
        if (!response.Success) return new Result(null, response.Error);

        var plan = AiJson.Parse<AiQueryPlan>(response.Content);
        if (plan == null)
            return new Result(null, $"无法理解这个请求，请换个说法。（返回：{AiJson.Snippet(response.Content, 120)}）");

        return Validate(plan, platforms, regions, languages, tags);
    }

    private static Result Validate(AiQueryPlan plan, IReadOnlyList<string> platforms,
        IReadOnlyList<string> regions, IReadOnlyList<string> languages, IReadOnlyList<string> tags)
    {
        plan.Intent = plan.Intent.Trim().ToLowerInvariant();
        if (plan.Intent is not ("filter" or "action"))
            return new Result(null, "无法理解这个请求，请换个说法。");

        var filter = plan.Filter ?? new AiQueryFilter();
        plan.Filter = filter;
        filter.Platform = filter.Platform.Trim();
        filter.Status = filter.Status.Trim();
        filter.Region = filter.Region.Trim();
        filter.Language = filter.Language.Trim();
        filter.Genre = filter.Genre.Trim();
        filter.Developer = filter.Developer.Trim();
        filter.Publisher = filter.Publisher.Trim();
        filter.Tag = filter.Tag.Trim();
        filter.Keyword = filter.Keyword.Trim();

        if (!MatchOrEmpty(filter.Platform, platforms, "平台", out var platformError)) return new Result(null, platformError);
        if (!MatchOrEmpty(filter.Status, GameStatus.All, "状态", out var statusError)) return new Result(null, statusError);
        if (!MatchOrEmpty(filter.Region, regions, "区域", out var regionError)) return new Result(null, regionError);
        if (!MatchOrEmpty(filter.Language, languages, "语言", out var languageError)) return new Result(null, languageError);
        if (!MatchOrEmpty(filter.Tag, tags, "标签", out var tagError)) return new Result(null, tagError);

        filter.Platform = Snap(filter.Platform, platforms);
        filter.Status = Snap(filter.Status, GameStatus.All);
        filter.Region = Snap(filter.Region, regions);
        filter.Language = Snap(filter.Language, languages);
        filter.Tag = Snap(filter.Tag, tags);

        if (filter.YearMin is < 1950 or > 2100 || filter.YearMax is < 1950 or > 2100)
            return new Result(null, "年份范围不合法。");
        if (filter.YearMin is int min && filter.YearMax is int max && min > max)
            return new Result(null, "年份范围不合法。");

        if (plan.Intent == "action")
        {
            var action = plan.Action;
            if (action == null || !ActionTypes.Contains(action.Type, StringComparer.Ordinal))
                return new Result(null, "这个操作不在允许范围内，只能修改状态、收藏或标签。");

            action.Value = action.Value.Trim();
            if (action.Type == "set_status" && !GameStatus.All.Contains(action.Value, StringComparer.Ordinal))
                return new Result(null, $"状态「{action.Value}」不在允许范围内。");
            if (action.Type is "add_tag" or "remove_tag"
                && (action.Value.Length == 0 || action.Value.Length > 20 || action.Value.Contains('\n')))
                return new Result(null, "标签长度需在 1–20 字之间。");
            if (action.Type == "toggle_favorite") action.Value = "";

            if (string.IsNullOrWhiteSpace(plan.Explanation))
                plan.Explanation = "按条件执行操作";
        }
        else
        {
            plan.Action = null;
            if (string.IsNullOrWhiteSpace(filter.Platform) && string.IsNullOrWhiteSpace(filter.Status)
                && string.IsNullOrWhiteSpace(filter.Region) && string.IsNullOrWhiteSpace(filter.Language)
                && string.IsNullOrWhiteSpace(filter.Genre) && string.IsNullOrWhiteSpace(filter.Developer)
                && string.IsNullOrWhiteSpace(filter.Publisher) && string.IsNullOrWhiteSpace(filter.Tag)
                && string.IsNullOrWhiteSpace(filter.Keyword) && filter.Favorite == null
                && !filter.OnlyMissing && filter.YearMin == null && filter.YearMax == null)
                return new Result(null, "没有识别出筛选条件，请换个说法。");
        }

        return new Result(plan, "");
    }

    private static bool MatchOrEmpty(string value, IEnumerable<string> allowed, string label, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(value)) return true;

        // 词典为空（例如还没有任何类型）时不限制自由文本
        if (!allowed.Any()) return true;
        if (allowed.Contains(value, StringComparer.OrdinalIgnoreCase)) return true;

        if (label is "类型" or "开发商" or "发行商") return true;

        error = $"无法识别{label}「{value}」。";
        return false;
    }

    private static string Snap(string value, IEnumerable<string> allowed)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        return allowed.FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)) ?? value;
    }
}
