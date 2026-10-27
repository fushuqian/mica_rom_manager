using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaRomManager.Models;
using MicaRomManager.Services;
using MicaRomManager.Services.Ai;

namespace MicaRomManager.ViewModels;

/// <summary>一行翻译/改名建议；新文件名统一由 LibraryOrganizer 生成。</summary>
public partial class RenameRow : ObservableObject
{
    public Game Game { get; init; } = null!;
    public long GameId => Game.Id;
    public string OldTitle { get; init; } = "";
    public string NewTitle { get; init; } = "";
    public string Note { get; init; } = "";
    public string OldFileName { get; init; } = "";
    public string NewFileName { get; set; } = "";
    public string Status { get; set; } = "";

    [ObservableProperty] private bool apply = true;
    [ObservableProperty] private string error = "";
}

/// <summary>标题翻译与可选的文件整理预览。</summary>
public partial class AiRenamePreviewViewModel : ObservableObject
{
    private readonly IReadOnlyList<Game> _games;
    private readonly AiTitleTranslator _translator = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<RenameRow> Rows { get; } = new();

    /// <summary>false = 只改标题；true = 同时整理库内文件名。</summary>
    [ObservableProperty] private bool renameFiles;
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private string hint = "";
    [ObservableProperty] private int applyCount;

    public bool AiConfigured => AppServices.Ai.Options.IsConfigured;

    public string AiHint => AiConfigured
        ? "只发送标题、平台与区域等文本；改名只作用于库内文件，不覆盖、不删除。"
        : "还没有配置 AI，请先到 设置 → AI 填写 BaseUrl、API Key 和模型。";

    public AiRenamePreviewViewModel(IReadOnlyList<Game> games) => _games = games;

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
        ProgressText = "正在翻译标题…";
        Rows.Clear();
        ApplyCount = 0;
        _cts = new CancellationTokenSource();
        var progress = new Progress<TranslateProgress>(value =>
            ProgressText = $"正在翻译（{value.Completed}/{value.Total}）：{value.Title}");

        try
        {
            var translations = await _translator.TranslateAsync(_games, progress, _cts.Token);
            foreach (var translation in translations)
            {
                var game = _games.FirstOrDefault(g => g.Id == translation.GameId);
                if (game == null) continue;
                Rows.Add(BuildRow(game, translation));
            }

            ApplyCount = Rows.Count(r => r.Apply && r.Error.Length == 0);
            ProgressText = $"完成：{Rows.Count} 条建议，其中 {ApplyCount} 条可应用。";
        }
        catch (OperationCanceledException)
        {
            ProgressText = "已取消。";
        }
        catch (Exception ex)
        {
            Hint = $"翻译失败：{ex.Message}";
            ProgressText = "";
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private static RenameRow BuildRow(Game game, TitleTranslation translation)
    {
        var row = new RenameRow
        {
            Game = game,
            OldTitle = game.Title,
            NewTitle = translation.NewTitle,
            Note = translation.Note,
            Error = translation.Error,
            Apply = translation.Success,
            OldFileName = game.FileName,
            NewFileName = "",
            Status = "",
        };

        if (!translation.Success) return row;

        var clone = game.Clone();
        clone.Title = translation.NewTitle;
        var firstDisc = game.Discs.FirstOrDefault() ?? "";
        var newName = firstDisc.Length == 0 ? "" : LibraryOrganizer.BuildFileName(clone, 0, firstDisc);
        row.NewFileName = game.IsMultiDisc ? $"{newName}（共 {game.DiscCount} 碟）" : newName;

        var dir = ResolvePlatformDir(game);
        var underLibrary = !string.IsNullOrWhiteSpace(dir)
                           && game.Discs.Any(d => File.Exists(d) && LibraryOrganizer.IsUnderDirectory(d, dir));
        if (!underLibrary)
        {
            row.Status = "库外跳过（只改标题）";
            return row;
        }

        var target = firstDisc.Length == 0 ? "" : Path.Combine(dir, newName);
        row.Status = target.Length > 0 && File.Exists(target)
                     && !string.Equals(target, firstDisc, StringComparison.OrdinalIgnoreCase)
            ? "可整理（重名加序号）"
            : "可整理";
        return row;
    }

    private static string ResolvePlatformDir(Game game)
    {
        var root = AppServices.LibraryRoot;
        if (string.IsNullOrWhiteSpace(root)) return "";

        var setting = AppServices.Platforms.Get(game.Platform);
        var folder = string.IsNullOrWhiteSpace(setting?.Folder) ? game.Platform : setting.Folder;
        return Path.Combine(root, RomImporter.SanitizeFolderName(folder));
    }

    public (int Titles, int Renamed, int Failed) Apply()
    {
        var rows = Rows.Where(r => r.Apply && r.Error.Length == 0).ToList();
        var titles = 0;

        foreach (var row in rows)
        {
            row.Game.Title = row.NewTitle;
            try
            {
                AppServices.GameRepo.Update(row.Game);
                titles++;
            }
            catch
            {
                row.Error = "写库失败";
            }
        }

        if (!RenameFiles) return (titles, 0, 0);

        var renamed = 0;
        var failed = 0;
        var groups = rows.Where(r => r.Error.Length == 0)
            .Select(r => r.Game)
            .GroupBy(ResolvePlatformDir)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key));

        foreach (var group in groups)
        {
            try
            {
                var result = LibraryOrganizer.Organize(AppServices.GameRepo, group, group.Key);
                renamed += result.Renamed;
                failed += result.Failed;
            }
            catch
            {
                failed += group.Count();
            }
        }

        return (titles, renamed, failed);
    }

    public void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task StartAsync() => await RunAsync();
}
