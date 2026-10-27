using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MicaRomManager.Models;
using MicaRomManager.Services.Recommendation;

namespace MicaRomManager.ViewModels;

/// <summary>本地相似推荐，不调用 AI。</summary>
public partial class RecommendViewModel : ObservableObject
{
    public ObservableCollection<RecommendationItem> Items { get; } = new();

    [ObservableProperty] private RecommendationItem? selectedItem;

    public string Header { get; }

    public RecommendViewModel(Game? seed, IReadOnlyList<Game> all)
    {
        Header = seed == null
            ? "未选中游戏，先按「在玩」或高评分未玩游戏作为种子。"
            : $"以「{seed.Title}」为种子，推荐相似且还没玩的游戏。";

        foreach (var item in RecommendationService.Recommend(seed, all)) Items.Add(item);
    }
}
