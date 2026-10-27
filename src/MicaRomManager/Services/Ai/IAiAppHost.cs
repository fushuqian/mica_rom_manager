using MicaRomManager.Models;

namespace MicaRomManager.Services.Ai;

/// <summary>AI 可执行的待确认操作；用户点确认后才会真正执行。</summary>
public sealed class AiPendingAction
{
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";
    public bool IsDestructive { get; init; }

    /// <summary>启动推荐卡片：显示「启动 / 换一个 / 算了」，不再额外确认。</summary>
    public bool IsLaunchSuggestion { get; init; }

    /// <summary>「换一个」时返回新的启动建议；null 表示没有其它候选。</summary>
    public Func<Task<AiPendingAction?>>? AlternativeAsync { get; init; }

    public Func<Task<string>> ExecuteAsync { get; init; } = () => Task.FromResult("");
}

/// <summary>MainViewModel 向 AI 工具暴露的应用能力。</summary>
public interface IAiAppHost
{
    IReadOnlyList<Game> AllGames { get; }
    IReadOnlyList<Game> FilteredGames { get; }
    Game? SelectedGame { get; }
    IReadOnlyList<Game> SelectedGames { get; }
    string ConnectedDevice { get; }

    void ApplyLibraryFilter(AiQueryFilter filter);
    void RefreshLibrary();

    int UpdateGames(IReadOnlyList<long> ids, GamePatch patch);
    int DeleteGames(IReadOnlyList<long> ids, bool recycleFiles);
    bool LaunchGame(long id, int discIndex, out string message);
    string SendGames(IReadOnlyList<long> ids);

    string GetSetting(string key);
    void SetSetting(string key, string value);
    void OpenWindow(string window);
}
