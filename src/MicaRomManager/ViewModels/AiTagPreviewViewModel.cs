using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaRomManager.Models;
using MicaRomManager.Services.Ai;

namespace MicaRomManager.ViewModels;

/// <summary>AI 标签预览：默认追加，确认后才写库。</summary>
public partial class AiTagPreviewViewModel : ObservableObject
{
    private readonly IReadOnlyList<Game> _games;
    private readonly AiTagService _service = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<TagSuggestion> Rows { get; } = new();

    /// <summary>true = 追加现有标签，false = 替换。</summary>
    [ObservableProperty] private bool appendMode = true;
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private string hint = "";
    [ObservableProperty] private int applyCount;

    public bool AiConfigured => AppServices.Ai.Options.IsConfigured;

    public string AiHint => AiConfigured
        ? $"将使用模型「{AppServices.Ai.Options.Model}」生成标签，只发送标题、平台、类型与现有标签。"
        : "还没有配置 AI，请先到 设置 → AI 填写 BaseUrl、API Key 和模型。";

    public AiTagPreviewViewModel(IReadOnlyList<Game> games) => _games = games;

    public async Task RunAsync()
    {
        if (IsRunning) return;
        if (!AiConfigured)
        {
            Hint = AiHint;
            return;
        }

        var options = AiOptions.Load(AppServices.Settings);
        if (_games.Count > options.BatchLimit)
        {
            Hint = $"已选 {_games.Count} 条，超过单次批量上限 {options.BatchLimit} 条。请分批处理。";
            return;
        }

        IsRunning = true;
        Hint = "";
        ProgressText = "正在生成标签…";
        Rows.Clear();
        ApplyCount = 0;
        _cts = new CancellationTokenSource();
        var progress = new Progress<TagProgress>(value =>
            ProgressText = $"正在生成标签（{value.Completed}/{value.Total}）：{value.Title}");

        try
        {
            var results = await _service.SuggestAsync(_games, progress, _cts.Token);
            foreach (var result in results) Rows.Add(result);
            ApplyCount = results.Count(r => r.Apply && !r.HasError);
            ProgressText = $"完成：{results.Count} 条建议，其中 {ApplyCount} 条可应用。";
        }
        catch (OperationCanceledException)
        {
            ProgressText = "已取消。";
        }
        catch (Exception ex)
        {
            Hint = $"生成标签失败：{ex.Message}";
            ProgressText = "";
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    public int Apply() => AiTagService.Apply(Rows, _games, AppendMode);

    public void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task StartAsync() => await RunAsync();
}
