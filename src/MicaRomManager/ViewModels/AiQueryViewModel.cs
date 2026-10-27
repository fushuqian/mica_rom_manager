using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaRomManager.Models;
using MicaRomManager.Services.Ai;

namespace MicaRomManager.ViewModels;

/// <summary>自然语言查询：模型只产出受控计划，本地校验后再应用。</summary>
public partial class AiQueryViewModel : ObservableObject
{
    private readonly IReadOnlyList<Game> _games;
    private readonly AiQueryService _service = new();

    [ObservableProperty] private string request = "";
    [ObservableProperty] private string explanation = "";
    [ObservableProperty] private string summary = "";
    [ObservableProperty] private string error = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private int hitCount;
    [ObservableProperty] private bool canApply;
    [ObservableProperty] private string applyText = "应用到列表";

    public AiQueryPlan? Plan { get; private set; }

    public IReadOnlyList<string> Examples { get; } = new[]
    {
        "找出所有未通关的 PS1 游戏",
        "把 SNES 里没玩过的加标签 待补",
        "显示今年收藏的 RPG",
    };

    public AiQueryViewModel(IReadOnlyList<Game> games) => _games = games;

    public async Task BuildAsync(CancellationToken token)
    {
        if (IsBusy) return;

        IsBusy = true;
        Error = "";
        Explanation = "";
        Summary = "";
        Plan = null;
        CanApply = false;
        HitCount = 0;
        ApplyText = "应用到列表";

        try
        {
            var result = await _service.BuildAsync(Request, token);
            if (result.Plan == null)
            {
                Error = result.Error;
                return;
            }

            Plan = result.Plan;
            Explanation = result.Plan.Explanation;
            Summary = BuildSummary(result.Plan);
            HitCount = _games.Count(game => AiQueryMatcher.Matches(game, result.Plan.Filter));
            CanApply = true;
            ApplyText = result.Plan.Intent == "action" ? "执行操作" : "应用到列表";
        }
        catch (OperationCanceledException)
        {
            Error = "已取消。";
        }
        catch (Exception ex)
        {
            Error = $"解析失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string BuildSummary(AiQueryPlan plan)
    {
        var filter = plan.Filter;
        var parts = new List<string>();
        Add(parts, "平台", filter.Platform);
        Add(parts, "状态", filter.Status);
        Add(parts, "区域", filter.Region);
        Add(parts, "语言", filter.Language);
        Add(parts, "类型", filter.Genre);
        Add(parts, "开发商", filter.Developer);
        Add(parts, "发行商", filter.Publisher);
        Add(parts, "标签", filter.Tag);
        if (filter.YearMin is int min) parts.Add($"年份≥{min}");
        if (filter.YearMax is int max) parts.Add($"年份≤{max}");
        if (filter.Favorite == true) parts.Add("仅收藏");
        if (filter.Favorite == false) parts.Add("排除收藏");
        if (filter.OnlyMissing) parts.Add("仅缺失文件");
        if (filter.Keyword.Length > 0) parts.Add($"关键词「{filter.Keyword}」");

        var condition = parts.Count == 0 ? "全部游戏" : string.Join("，", parts);
        if (plan.Intent != "action" || plan.Action == null) return $"筛选条件：{condition}";

        var action = plan.Action.Type switch
        {
            "set_status" => $"修改状态为「{plan.Action.Value}」",
            "toggle_favorite" => "切换收藏",
            "add_tag" => $"追加标签「{plan.Action.Value}」",
            "remove_tag" => $"移除标签「{plan.Action.Value}」",
            _ => "执行操作",
        };
        return $"筛选条件：{condition}\n操作：{action}";
    }

    private static void Add(List<string> parts, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) parts.Add($"{label}={value}");
    }

    [RelayCommand]
    private async Task BuildCommandAsync() => await BuildAsync(CancellationToken.None);
}
