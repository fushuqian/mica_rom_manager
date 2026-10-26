using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaRomManager.Data;
using MicaRomManager.Models;
using MicaRomManager.Services;
using MicaRomManager.Views;
using Microsoft.Win32;

namespace MicaRomManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public const string AllPlatforms = "全部平台";
    public const string AllStatuses = "全部状态";
    public const string AllTags = "全部标签";

    public ObservableCollection<Game> Games { get; } = new();
    public ICollectionView GamesView { get; }
    public ObservableCollection<string> Platforms { get; } = new();
    public ObservableCollection<string> Tags { get; } = new();
    public ObservableCollection<string> Regions { get; } = new();
    public ObservableCollection<string> Languages { get; } = new();
    public ObservableCollection<Emulator> Emulators { get; } = new();
    public IReadOnlyList<string> Statuses { get; } = new[] { AllStatuses }.Concat(GameStatus.All).ToList();
    public IReadOnlyList<string> EditStatuses { get; } = GameStatus.All;

    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string selectedPlatform = AllPlatforms;
    [ObservableProperty] private string selectedStatus = AllStatuses;
    [ObservableProperty] private string selectedTag = AllTags;
    [ObservableProperty] private bool onlyFavorites;
    [ObservableProperty] private bool onlyMissing;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "就绪";
    [ObservableProperty] private Game? selectedGame;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private int filteredCount;
    [ObservableProperty] private string lastRefreshText = "";

    public List<Game> SelectedGames { get; private set; } = new();

    public string CountText => $"共 {TotalCount} 款 / 已筛选 {FilteredCount} 款";
    public string DataPathText => $"数据：{AppServices.Paths.DbPath}";

    public MainViewModel()
    {
        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = FilterGame;

        AppServices.Launcher.SessionEnded += OnSessionEnded;
        LoadEmulators();
        LoadGames();
    }

    public void LoadGames()
    {
        Games.Clear();
        foreach (var game in AppServices.GameRepo.ListAll()) Games.Add(game);

        RefreshLookups();
        TotalCount = Games.Count;
        LastRefreshText = $"上次刷新：{DateTime.Now:HH:mm:ss}";
        ApplyFilter();
    }

    /// <summary>主界面行内编辑后落库；只追加新选项，避免清空下拉导致绑定被置空。</summary>
    public void SaveInline(Game game)
    {
        AppServices.GameRepo.Update(game);

        if (!string.IsNullOrWhiteSpace(game.Platform) && !Platforms.Contains(game.Platform))
            Platforms.Add(game.Platform);
        foreach (var tag in SplitTags(game.Tags))
            if (!Tags.Contains(tag)) Tags.Add(tag);
    }

    public void LoadEmulators()
    {
        Emulators.Clear();
        foreach (var emulator in AppServices.EmulatorRepo.ListAll()) Emulators.Add(emulator);
    }

    public void SetSelectedGames(IEnumerable<Game> games)
    {
        SelectedGames = games.Where(g => g != null).ToList();
        SelectedGame = SelectedGames.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedGames));
    }

    public List<Emulator> GetEmulatorsForSelection()
    {
        var game = SelectedGame;
        if (game == null) return Emulators.Where(e => e.Enabled).ToList();

        return Emulators
            .Where(e => e.Enabled && e.Platforms.Any(p => string.Equals(p, game.Platform, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private bool FilterGame(object item)
    {
        if (item is not Game game) return false;
        if (OnlyFavorites && !game.Favorite) return false;
        if (OnlyMissing && !game.IsMissing) return false;
        if (!string.IsNullOrEmpty(SelectedPlatform) && SelectedPlatform != AllPlatforms &&
            !string.Equals(game.Platform, SelectedPlatform, StringComparison.OrdinalIgnoreCase)) return false;
        if (SelectedStatus != AllStatuses &&
            !string.Equals(game.Status, SelectedStatus, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(SelectedTag) && SelectedTag != AllTags &&
            !SplitTags(game.Tags).Contains(SelectedTag, StringComparer.OrdinalIgnoreCase)) return false;

        var keyword = SearchText.Trim();
        if (keyword.Length == 0) return true;

        return Contains(game.Title, keyword)
               || Contains(game.RomPath, keyword)
               || Contains(game.Tags, keyword)
               || Contains(game.Platform, keyword)
               || Contains(game.Genre, keyword);

        static bool Contains(string value, string keyword) =>
            value?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true;
    }

    private void ApplyFilter()
    {
        GamesView.Refresh();
        FilteredCount = GamesView.Cast<object>().Count();
    }

    private void RefreshLookups()
    {
        Rebuild(Platforms, AllPlatforms, Games.Select(g => g.Platform), SelectedPlatform, value => SelectedPlatform = value);
        Rebuild(Tags, AllTags, Games.SelectMany(g => SplitTags(g.Tags)), SelectedTag, value => SelectedTag = value);
        if (!Statuses.Contains(SelectedStatus)) SelectedStatus = AllStatuses;

        Regions.Clear();
        foreach (var region in AppServices.Lookups.List(LookupRepository.Region)) Regions.Add(region);
        Languages.Clear();
        foreach (var language in AppServices.Lookups.List(LookupRepository.Language)) Languages.Add(language);

        // Rebuild 里 target.Clear() 会让 WPF 双向绑定把 SelectedXxx 临时置 null，
        // 上面的 setSelected 兜底只检查旧参数值，当前字段若已被绑定写 null 就漏掉了。
        // 这里再兜底一次，确保字段值在 ComboBox 选项里。
        if (!Platforms.Contains(SelectedPlatform)) SelectedPlatform = AllPlatforms;
        if (!Tags.Contains(SelectedTag)) SelectedTag = AllTags;
    }

    private static void Rebuild(ObservableCollection<string> target, string allValue, IEnumerable<string> values,
        string current, Action<string> setSelected)
    {
        target.Clear();
        target.Add(allValue);
        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v))
        {
            target.Add(value);
        }

        if (!target.Contains(current)) setSelected(allValue);
    }

    private static IEnumerable<string> SplitTags(string? tags) =>
        string.IsNullOrWhiteSpace(tags)
            ? Enumerable.Empty<string>()
            : tags.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void OnSessionEnded(Game game, long seconds)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        dispatcher.Invoke(() =>
        {
            var target = Games.FirstOrDefault(g => g.Id == game.Id);
            if (target == null) return;
            target.PlayCount += 1;
            target.PlaySeconds += seconds;
            target.LastPlayedAt = DateTime.Now.ToString("O");
            StatusMessage = $"「{target.Title}」已退出，本次 {TimeSpan.FromSeconds(seconds):mm\\:ss}";
            GamesView.Refresh();
        });
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedPlatformChanged(string value) => ApplyFilter();
    partial void OnSelectedStatusChanged(string value) => ApplyFilter();
    partial void OnSelectedTagChanged(string value) => ApplyFilter();
    partial void OnOnlyFavoritesChanged(bool value) => ApplyFilter();
    partial void OnOnlyMissingChanged(bool value) => ApplyFilter();
    partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(CountText));
    partial void OnFilteredCountChanged(int value) => OnPropertyChanged(nameof(CountText));

    [RelayCommand]
    private Task ImportAsync() => ShowImportWizardAsync(Array.Empty<string>());

    /// <summary>拖放等入口：带着初始来源打开向导。</summary>
    public Task ImportAsync(IEnumerable<string> inputs, bool recursive) =>
        ShowImportWizardAsync(inputs);

    private async Task ShowImportWizardAsync(IEnumerable<string> initialSources)
    {
        if (IsBusy) return;

        var libraryRoot = AppServices.Settings.Get("library.root") ?? "";
        var wizard = new ImportWizardWindow(
            initialSources,
            Games.Select(g => g.RomPath),
            libraryRoot,
            ReadDefaultImportMode())
        {
            Owner = Application.Current?.MainWindow,
        };

        if (wizard.ShowDialog() != true || wizard.Result == null)
        {
            StatusMessage = "已取消导入";
            return;
        }

        await ApplyImportAsync(wizard.Result, libraryRoot);
    }

    private static ImportMode ReadDefaultImportMode()
    {
        var raw = AppServices.Settings.Get("library.import_mode");
        if (Enum.TryParse<ImportMode>(raw, true, out var mode)) return mode;

        return AppServices.Settings.GetBool("library.copy_on_import") ? ImportMode.Copy : ImportMode.Link;
    }

    private async Task ApplyImportAsync(ImportWizardResult result, string libraryRoot)
    {
        IsBusy = true;
        StatusMessage = "正在导入 ROM…";

        var created = new List<Game>();
        var failed = 0;

        try
        {
            await Task.Run(() =>
            {
                foreach (var candidate in result.Candidates)
                {
                    try
                    {
                        var romPath = candidate.FilePath;
                        romPath = result.Mode switch
                        {
                            ImportMode.Copy => RomImporter.CopyToLibrary(romPath, libraryRoot, candidate.Platform),
                            ImportMode.Move => RomImporter.MoveToLibrary(romPath, libraryRoot, candidate.Platform),
                            _ => romPath,
                        };

                        var game = new Game
                        {
                            Title = candidate.Title.Trim(),
                            Platform = candidate.Platform,
                            RomPath = romPath,
                            FileSize = new FileInfo(romPath).Length,
                            Status = GameStatus.NotPlayed,
                        };
                        AppServices.GameRepo.Insert(game);
                        created.Add(game);
                    }
                    catch
                    {
                        failed++;
                    }
                }
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"导入失败：{ex.Message}";
            MessageBox.Show(StatusMessage, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
            IsBusy = false;
            return;
        }

        foreach (var game in created) Games.Add(game);

        RefreshLookups();
        TotalCount = Games.Count;
        ApplyFilter();
        LastRefreshText = $"上次刷新：{DateTime.Now:HH:mm:ss}";
        IsBusy = false;

        var action = result.Mode switch
        {
            ImportMode.Copy => "已复制入库",
            ImportMode.Move => "已移动入库",
            _ => "已链接入库",
        };
        StatusMessage = $"{action} {created.Count} 条" + (failed > 0 ? $"，失败 {failed} 条" : "");
    }

    [RelayCommand]
    private void NewGame()
    {
        var game = new Game { Title = "新建游戏", Status = GameStatus.NotPlayed };
        var window = new GameEditWindow(game, true) { Owner = Application.Current?.MainWindow };
        if (window.ShowDialog() != true) return;

        Games.Add(game);
        RefreshLookups();
        TotalCount = Games.Count;
        ApplyFilter();
        SelectedGame = game;
        StatusMessage = $"已新建「{game.Title}」";
    }

    [RelayCommand]
    private void EditSelected()
    {
        var game = SelectedGame;
        if (game == null) return;

        var working = game.Clone();
        var window = new GameEditWindow(working, false) { Owner = Application.Current?.MainWindow };
        if (window.ShowDialog() != true) return;

        game.ApplyFrom(working);
        game.RefreshFileState();
        RefreshLookups();
        ApplyFilter();
        StatusMessage = $"已保存「{game.Title}」";
    }

    [RelayCommand]
    private void LaunchSelected()
    {
        var game = SelectedGame;
        if (game == null) return;

        var emulator = EmulatorResolver.Resolve(game, Emulators.ToList(), AppServices.EmulatorRepo);
        if (emulator == null)
        {
            var message = $"还没有为平台「{game.Platform}」配置模拟器。\n\n现在打开模拟器管理？";
            if (MessageBox.Show(message, "无法启动", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                OpenSettings();
            }

            return;
        }

        if (!AppServices.Launcher.TryLaunch(game, emulator, PickLaunchPath(game) ?? game.RomPath, out var error))
        {
            MessageBox.Show(error, "无法启动", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusMessage = $"已用「{emulator.Name}」启动「{game.Title}」";
    }

    /// <summary>多碟游戏弹窗选碟；取消返回 null。</summary>
    private static string? PickLaunchPath(Game game)
    {
        if (!game.IsMultiDisc) return game.RomPath;

        var picker = new DiscSelectWindow(game) { Owner = Application.Current?.MainWindow };
        return picker.ShowDialog() == true ? picker.SelectedPath : null;
    }

    [RelayCommand]
    private void MergeSelected()
    {
        var games = SelectedGames.ToList();
        if (games.Count < 2)
        {
            MessageBox.Show("请先选中至少 2 个游戏条目（第一行作为主条目）。", "合并为多碟游戏",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var main = games[0];
        var discs = new List<string>(main.Discs);
        foreach (var other in games.Skip(1))
        {
            foreach (var disc in other.Discs)
                if (!discs.Contains(disc, StringComparer.OrdinalIgnoreCase)) discs.Add(disc);
        }

        if (discs.Count < 2) return;

        main.RomPath = discs[0];
        main.DiscPaths = string.Join("\n", discs.Skip(1));
        AppServices.GameRepo.Update(main);

        foreach (var other in games.Skip(1)) AppServices.GameRepo.Delete(other.Id);

        LoadGames();
        StatusMessage = $"已合并为多碟游戏「{main.Title}」，共 {discs.Count} 碟";
    }

    [RelayCommand]
    private void SplitMultiDisc()
    {
        var game = SelectedGame;
        if (game == null || !game.IsMultiDisc) return;

        var extra = game.Discs.Skip(1).ToList();
        game.DiscPaths = "";
        AppServices.GameRepo.Update(game);

        for (var i = 0; i < extra.Count; i++)
        {
            var path = extra[i];
            AppServices.GameRepo.Insert(new Game
            {
                Title = $"{game.Title}（碟{i + 2}）",
                Platform = game.Platform,
                Genre = game.Genre,
                Region = game.Region,
                Language = game.Language,
                ReleaseYear = game.ReleaseYear,
                Publisher = game.Publisher,
                Developer = game.Developer,
                Status = game.Status,
                EmulatorId = game.EmulatorId,
                RomPath = path,
                FileSize = File.Exists(path) ? new FileInfo(path).Length : 0,
            });
        }

        LoadGames();
        StatusMessage = $"已把「{game.Title}」拆分为 {extra.Count + 1} 个条目";
    }

    [RelayCommand]
    private void LaunchWith(Emulator? emulator)
    {
        var game = SelectedGame;
        if (game == null || emulator == null) return;

        var discPath = PickLaunchPath(game);
        if (discPath == null) return;

        if (!AppServices.Launcher.TryLaunch(game, emulator, discPath, out var error))
        {
            MessageBox.Show(error, "无法启动", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusMessage = $"已用「{emulator.Name}」启动「{game.Title}」";
    }

    [RelayCommand]
    private void ShareSelected()
    {
        var games = SelectedGames.ToList();
        if (games.Count == 0) return;

        var window = new SharePackWindow(games) { Owner = Application.Current?.MainWindow };
        window.ShowDialog();
        StatusMessage = window.Exported ? "分享包已生成" : "已取消分享";
    }

    [RelayCommand]
    private void OpenSelectedFolder()
    {
        var path = SelectedGame?.RomPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void CopySelectedPath()
    {
        var paths = SelectedGames.Select(g => g.RomPath).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (paths.Count == 0) return;

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, paths));
            StatusMessage = $"已复制 {paths.Count} 个路径";
        }
        catch
        {
            StatusMessage = "复制路径失败";
        }
    }

    [RelayCommand]
    private void ToggleFavorite()
    {
        if (SelectedGames.Count == 0) return;
        foreach (var game in SelectedGames)
        {
            game.Favorite = !game.Favorite;
            AppServices.GameRepo.Update(game);
        }

        ApplyFilter();
    }

    [RelayCommand]
    private void SetCover()
    {
        var game = SelectedGame;
        if (game == null) return;

        var dialog = new OpenFileDialog
        {
            Title = "选择封面图片",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif|所有文件|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            game.CoverPath = AppServices.Covers.Import(dialog.FileName);
            AppServices.GameRepo.Update(game);
            StatusMessage = $"已设置封面：{game.Title}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"设置封面失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void RefreshStatus()
    {
        foreach (var game in Games) game.RefreshFileState();
        ApplyFilter();
        LastRefreshText = $"上次刷新：{DateTime.Now:HH:mm:ss}";
        StatusMessage = "已刷新文件状态";
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedGames.Count == 0) return;
        var count = SelectedGames.Count;
        var answer = MessageBox.Show($"从库中移除 {count} 条记录？\n\n只删除数据库记录，不会动 ROM 文件。",
            "从库中移除", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        foreach (var game in SelectedGames.ToList())
        {
            AppServices.GameRepo.Delete(game.Id);
            Games.Remove(game);
        }

        RefreshLookups();
        TotalCount = Games.Count;
        ApplyFilter();
        SetSelectedGames(Array.Empty<Game>());
        StatusMessage = $"已移除 {count} 条记录";
    }

    [RelayCommand]
    private void RecycleSelected()
    {
        if (SelectedGames.Count == 0) return;
        var count = SelectedGames.Count;
        var answer = MessageBox.Show($"把 {count} 个 ROM 文件移到回收站并移除记录？\n\n文件可在 Windows 回收站找回。",
            "移到回收站", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        var moved = 0;
        foreach (var game in SelectedGames.ToList())
        {
            if (FileRecycleBin.MoveToRecycleBin(game.RomPath)) moved++;
            AppServices.GameRepo.Delete(game.Id);
            Games.Remove(game);
        }

        RefreshLookups();
        TotalCount = Games.Count;
        ApplyFilter();
        SetSelectedGames(Array.Empty<Game>());
        StatusMessage = $"已移到回收站 {moved} 个文件，并移除 {count} 条记录";
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = "";
        SelectedPlatform = AllPlatforms;
        SelectedStatus = AllStatuses;
        SelectedTag = AllTags;
        OnlyFavorites = false;
        OnlyMissing = false;
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new SettingsWindow { Owner = Application.Current?.MainWindow };
        window.ShowDialog();

        LoadEmulators();
        ApplyFilter();
    }

    public void FocusSearchRequested() { }

    public void NotifySearchFocus() { }
}
