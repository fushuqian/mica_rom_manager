using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaRomManager.Models;
using MicaRomManager.Services.Ai;
using MicaRomManager.Services.Scraping;

namespace MicaRomManager.ViewModels;

/// <summary>刮削预览：本地解析 + 可选 AI，全部结果确认后才写库。</summary>
public partial class ScrapePreviewViewModel : ObservableObject
{
    private readonly IReadOnlyList<Game> _games;
    private readonly ScraperService _service = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<ScrapeResult> Rows { get; } = new();

    [ObservableProperty] private bool useAi;
    [ObservableProperty] private bool overwrite;
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private string hint = "";
    [ObservableProperty] private int applyCount;

    public bool AiConfigured => AppServices.Ai.Options.IsConfigured;

    public string AiHint => AiConfigured
        ? $"已配置模型「{AppServices.Ai.Options.Model}」，AI 只接收文件名、平台与现有标题等文本。"
        : "还没有配置 AI，当前只能按文件名解析；可到 设置 → AI 配置。";

    public ScrapePreviewViewModel(IReadOnlyList<Game> games)
    {
        _games = games;
        UseAi = AiConfigured;
    }

    public async Task RunAsync()
    {
        if (IsRunning) return;

        var options = AiOptions.Load(AppServices.Settings);
        if (UseAi && _games.Count > options.BatchLimit)
        {
            Hint = $"已选 {_games.Count} 条，超过单次批量上限 {options.BatchLimit} 条。请分批刮削，或到 设置 → AI 调整上限。";
            return;
        }

        if (UseAi && !AiConfigured)
        {
            Hint = "还没有配置 AI，本次只按文件名解析。";
            UseAi = false;
        }

        IsRunning = true;
        Hint = "";
        ProgressText = "正在刮削…";
        Rows.Clear();
        ApplyCount = 0;

        _cts = new CancellationTokenSource();
        var progress = new Progress<ScrapeProgress>(value =>
        {
            ProgressText = $"正在刮削（{value.Completed}/{value.Total}）：{value.Message}";
        });

        try
        {
            var results = await _service.ScrapeAsync(_games, UseAi, Overwrite, progress, _cts.Token);
            foreach (var result in results) Rows.Add(result);
            ApplyCount = results.Count(r => r.Apply && !r.HasError);
            ProgressText = $"完成：{results.Count} 条候选，其中 {ApplyCount} 条待应用。";
            if (results.All(r => r.Error.Length == 0))
                Hint = AiConfigured && UseAi ? "AI 结果与本地解析已合并，请核对后应用。" : "本地解析完成，请核对后应用。";
        }
        catch (OperationCanceledException)
        {
            ProgressText = "已取消。";
        }
        catch (Exception ex)
        {
            Hint = $"刮削失败：{ex.Message}";
            ProgressText = "";
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>把勾选且无错误的行写回游戏，返回成功条数。</summary>
    public int Apply()
    {
        var applied = 0;
        foreach (var row in Rows.Where(r => r.Apply && !r.HasError))
        {
            var game = _games.FirstOrDefault(g => g.Id == row.GameId);
            if (game == null) continue;

            row.ApplyTo(game);
            try
            {
                AppServices.GameRepo.Update(game);
                applied++;
            }
            catch
            {
                row.Error = "写库失败";
            }
        }

        return applied;
    }

    public void Cancel()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private async Task StartAsync() => await RunAsync();
}
