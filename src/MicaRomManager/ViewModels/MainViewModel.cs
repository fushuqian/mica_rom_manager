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
using MicaRomManager.Services.Ai;
using MicaRomManager.Services.Sending;
using MicaRomManager.Views;
using Microsoft.Win32;

namespace MicaRomManager.ViewModels;

/// <summary>左侧平台列表项：Name 为筛选值，Display 为显示文本。</summary>
public partial class PlatformFilterItem : ObservableObject
{
    public PlatformFilterItem(string name) => Name = name;

    public string Name { get; }

    public string Display => string.Equals(Name, MainViewModel.AllPlatforms, StringComparison.Ordinal)
        ? "所有游戏"
        : PlatformDisplayNames.Of(Name);

    [ObservableProperty] private int count;
}

public partial class MainViewModel : ObservableObject, IAiAppHost
{
    public const string AllPlatforms = "全部平台";
    public const string AllStatuses = "全部状态";
    public const string AllTags = "全部标签";
    public const string AllRegions = "全部区域";
    public const string AllLanguages = "全部语言";
    public const string AllDevelopers = "全部开发商";

    public ObservableCollection<Game> Games { get; } = new();
    public ICollectionView GamesView { get; }
    public ObservableCollection<string> Platforms { get; } = new();
    public ObservableCollection<string> Tags { get; } = new();
    public ObservableCollection<string> Regions { get; } = new();
    public ObservableCollection<string> Languages { get; } = new();
    public ObservableCollection<PlatformFilterItem> PlatformFilters { get; } = new();
    public ObservableCollection<string> RegionFilters { get; } = new();
    public ObservableCollection<string> LanguageFilters { get; } = new();
    public ObservableCollection<string> DeveloperFilters { get; } = new();
    public ObservableCollection<Emulator> Emulators { get; } = new();
    public IReadOnlyList<string> Statuses { get; } = new[] { AllStatuses }.Concat(GameStatus.All).ToList();
    public IReadOnlyList<string> EditStatuses { get; } = GameStatus.All;

    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string selectedPlatform = AllPlatforms;
    [ObservableProperty] private PlatformFilterItem? selectedPlatformFilter;
    [ObservableProperty] private string selectedStatus = AllStatuses;
    [ObservableProperty] private string selectedTag = AllTags;
    [ObservableProperty] private string selectedRegion = AllRegions;
    [ObservableProperty] private string selectedLanguage = AllLanguages;
    [ObservableProperty] private string selectedDeveloper = AllDevelopers;
    [ObservableProperty] private bool onlyFavorites;
    [ObservableProperty] private bool onlyMissing;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "就绪";
    [ObservableProperty] private Game? selectedGame;
    [ObservableProperty] private DeviceConnection? deviceConnection;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private int filteredCount;
    [ObservableProperty] private string lastRefreshText = "";

    /// <summary>ROM 所在盘未连接时的顶部提示；空串表示不显示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLibraryWarning))]
    private string libraryWarning = "";

    public bool HasLibraryWarning => LibraryWarning.Length > 0;

    private bool _syncingPlatformFilter;
    private SendTaskWindow? _taskWindow;

    // AI 查询产生的临时条件（仅本次会话有效，清除筛选时一并重置）
    private string _aiGenre = "";
    private string _aiPublisher = "";
    private int? _aiYearMin;
    private int? _aiYearMax;
    private bool _aiNotFavorite;

    public List<Game> SelectedGames { get; private set; } = new();

    public string CountText => $"共 {TotalCount} 款 / 已筛选 {FilteredCount} 款";
    public string DataPathText => $"数据：{AppServices.Paths.DbPath}";

    /// <summary>当前生效的筛选条件数量（用于「筛选」按钮上的徽标）。</summary>
    public int ActiveFilterCount
    {
        get
        {
            var count = 0;
            if (!string.IsNullOrWhiteSpace(SearchText)) count++;
            if (!string.Equals(SelectedStatus, AllStatuses, StringComparison.Ordinal)) count++;
            if (!string.Equals(SelectedRegion, AllRegions, StringComparison.Ordinal)) count++;
            if (!string.Equals(SelectedLanguage, AllLanguages, StringComparison.Ordinal)) count++;
            if (!string.Equals(SelectedDeveloper, AllDevelopers, StringComparison.Ordinal)) count++;
            if (!string.Equals(SelectedTag, AllTags, StringComparison.Ordinal)) count++;
            if (OnlyFavorites) count++;
            if (OnlyMissing) count++;
            return count;
        }
    }

    public bool HasActiveFilters => ActiveFilterCount > 0;

    public bool HasConnection => DeviceConnection != null;

    public string ConnectButtonText => HasConnection ? "更换设备" : "连接到设备";

    public string ConnectionText => DeviceConnection == null
        ? "未连接设备"
        : $"已连接：{DeviceConnection.Summary}（{DeviceConnection.PolicyText}）";

    public MainViewModel()
    {
        GamesView = CollectionViewSource.GetDefaultView(Games);
        GamesView.Filter = FilterGame;

        AppServices.Launcher.SessionEnded += OnSessionEnded;
        AppServices.SendQueue.AllCompleted += OnSendQueueCompleted;
        LoadEmulators();
        LoadGames();
    }

    public void LoadGames()
    {
        Games.Clear();
        var list = AppServices.GameRepo.ListAll();
        ApplyEmulatorDisplay(list);
        foreach (var game in list) Games.Add(game);

        RefreshLookups();
        TotalCount = Games.Count;
        LastRefreshText = $"上次刷新：{DateTime.Now:HH:mm:ss}";
        ApplyFilter();
        RefreshLibraryWarning();
    }

    /// <summary>把「选用模拟器」换算成名称：优先游戏自带 id，否则回落到该平台的默认模拟器。</summary>
    private void ApplyEmulatorDisplay(IEnumerable<Game> games)
    {
        var byId = Emulators.Where(e => e.Id > 0).ToDictionary(e => e.Id, e => e.Name);
        var byPlatform = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var emulator in Emulators)
        {
            foreach (var platform in emulator.Platforms)
            {
                if (string.IsNullOrWhiteSpace(platform)) continue;
                if (!byPlatform.ContainsKey(platform)) byPlatform[platform] = emulator.Name;
            }
        }

        foreach (var game in games)
        {
            if (game.EmulatorId is > 0 && byId.TryGetValue(game.EmulatorId.Value, out var name))
                game.EmulatorDisplay = name;
            else if (!string.IsNullOrWhiteSpace(game.Platform) && byPlatform.TryGetValue(game.Platform, out var fallback))
                game.EmulatorDisplay = fallback;
            else
                game.EmulatorDisplay = "";
        }
    }

    /// <summary>
    /// 检查 ROM 所在盘是否可用；外置盘没插时给出提示，避免把「盘没连」误当成「文件失效」。
    /// 不做自动清理，只提示。
    /// </summary>
    public void RefreshLibraryWarning()
    {
        var report = LibraryHealth.Inspect(Games);
        LibraryWarning = report.HasUnavailableRoots
            ? $"{string.Join("、", report.UnavailableRoots)} 未连接，{report.MissingGames} 条 ROM 暂不可用；"
              + "插好后点「刷新文件状态」重新检查。"
            : "";
    }

    /// <summary>主界面行内编辑后落库；只追加新选项，避免清空下拉导致绑定被置空。</summary>
    public void SaveInline(Game game)
    {
        AppServices.GameRepo.Update(game);

        // 刮削/编辑产生的新值自动补进字典库，下一次筛选就能选到
        var lookupAdded = LookupSync.SyncGame(game);

        if (!string.IsNullOrWhiteSpace(game.Platform) && !Platforms.Contains(game.Platform))
            Platforms.Add(game.Platform);
        foreach (var tag in SplitTags(game.Tags))
            if (!Tags.Contains(tag)) Tags.Add(tag);

        RefreshPlatformFilters();

        if (lookupAdded > 0) RefreshLookups();

        // 编辑状态/平台/标签等参与筛选的字段后，按当前条件立即刷新
        ApplyFilter();
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
        if (_aiNotFavorite && game.Favorite) return false;
        if (_aiGenre.Length > 0 && !string.Equals(game.Genre, _aiGenre, StringComparison.OrdinalIgnoreCase))
            return false;
        if (_aiPublisher.Length > 0 && !string.Equals(game.Publisher, _aiPublisher, StringComparison.OrdinalIgnoreCase))
            return false;
        if (_aiYearMin is int minYear && (game.ReleaseYear ?? int.MinValue) < minYear) return false;
        if (_aiYearMax is int maxYear && (game.ReleaseYear ?? int.MaxValue) > maxYear) return false;
        if (!string.IsNullOrEmpty(SelectedPlatform) && SelectedPlatform != AllPlatforms &&
            !string.Equals(game.Platform, SelectedPlatform, StringComparison.OrdinalIgnoreCase)) return false;
        if (SelectedStatus != AllStatuses &&
            !string.Equals(game.Status, SelectedStatus, StringComparison.OrdinalIgnoreCase)) return false;
        if (SelectedRegion != AllRegions &&
            !string.Equals(game.Region, SelectedRegion, StringComparison.OrdinalIgnoreCase)) return false;
        if (SelectedLanguage != AllLanguages &&
            !string.Equals(game.Language, SelectedLanguage, StringComparison.OrdinalIgnoreCase)) return false;
        if (SelectedDeveloper != AllDevelopers &&
            !string.Equals(game.Developer, SelectedDeveloper, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(SelectedTag) && SelectedTag != AllTags &&
            !SplitTags(game.Tags).Contains(SelectedTag, StringComparer.OrdinalIgnoreCase)) return false;

        var keyword = SearchText.Trim();
        if (keyword.Length == 0) return true;

        return Contains(game.Title, keyword)
               || Contains(game.RomPath, keyword)
               || Contains(game.Tags, keyword)
               || Contains(game.Platform, keyword)
               || Contains(game.Genre, keyword)
               || Contains(game.Developer, keyword);

        static bool Contains(string value, string keyword) =>
            value?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true;
    }

    private bool _refreshPending;

    private void ApplyFilter()
    {
        // DataGrid 行仍处于编辑事务时 Refresh 会抛异常，先记下待刷新，等提交后补刷新。
        if (GamesView is IEditableCollectionView editable && editable.IsEditingItem)
        {
            _refreshPending = true;
            return;
        }

        GamesView.Refresh();
        FilteredCount = GamesView.Cast<object>().Count();
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(HasActiveFilters));
        _refreshPending = false;
    }

    /// <summary>行编辑结束后补做挂起的筛选刷新。</summary>
    public void RefreshFilterAfterEdit()
    {
        if (_refreshPending) ApplyFilter();
    }

    private void RefreshLookups()
    {
        Rebuild(Platforms, AllPlatforms, Games.Select(g => g.Platform), SelectedPlatform, value => SelectedPlatform = value);
        Rebuild(Tags, AllTags, Games.SelectMany(g => SplitTags(g.Tags)), SelectedTag, value => SelectedTag = value);
        if (!Statuses.Contains(SelectedStatus)) SelectedStatus = AllStatuses;

        if (!Platforms.Contains(SelectedPlatform)) SelectedPlatform = AllPlatforms;
        if (!Tags.Contains(SelectedTag)) SelectedTag = AllTags;

        RefreshPlatformFilters();

        RebuildFilter(RegionFilters, AllRegions, AppServices.Lookups.List(LookupRepository.Region),
            Games.Select(g => g.Region));
        RebuildFilter(LanguageFilters, AllLanguages, AppServices.Lookups.List(LookupRepository.Language),
            Games.Select(g => g.Language));
        RebuildFilter(DeveloperFilters, AllDevelopers, AppServices.Lookups.List(LookupRepository.Developer),
            Games.Select(g => g.Developer));

        if (!RegionFilters.Contains(SelectedRegion)) SelectedRegion = AllRegions;
        if (!LanguageFilters.Contains(SelectedLanguage)) SelectedLanguage = AllLanguages;
        if (!DeveloperFilters.Contains(SelectedDeveloper)) SelectedDeveloper = AllDevelopers;

        // 行内编辑用的下拉：区域 / 语言。
        Regions.Clear();
        foreach (var region in AppServices.Lookups.List(LookupRepository.Region)) Regions.Add(region);
        Languages.Clear();
        foreach (var language in AppServices.Lookups.List(LookupRepository.Language)) Languages.Add(language);
    }

    /// <summary>左侧平台列表：字典顺序优先，再补库里出现但字典没有的平台；只列有游戏的平台。</summary>
    private void RefreshPlatformFilters()
    {
        var gamePlatforms = Games.Select(g => g.Platform)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ordered = AppServices.Lookups.List(LookupRepository.Platform)
            .Where(name => gamePlatforms.Contains(name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        foreach (var name in gamePlatforms.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
            if (!ordered.Contains(name, StringComparer.OrdinalIgnoreCase)) ordered.Add(name);

        // 增量同步而不是 Clear，避免 ListBox 选中项被清空、左侧平台状态被重置。
        var desired = new List<string> { AllPlatforms };
        desired.AddRange(ordered);

        for (var index = PlatformFilters.Count - 1; index >= 0; index--)
        {
            if (!desired.Contains(PlatformFilters[index].Name, StringComparer.OrdinalIgnoreCase))
                PlatformFilters.RemoveAt(index);
        }

        for (var index = 0; index < desired.Count; index++)
        {
            var name = desired[index];
            var count = string.Equals(name, AllPlatforms, StringComparison.Ordinal)
                ? Games.Count
                : Games.Count(g => string.Equals(g.Platform, name, StringComparison.OrdinalIgnoreCase));

            if (index < PlatformFilters.Count &&
                string.Equals(PlatformFilters[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                PlatformFilters[index].Count = count;
                continue;
            }

            var existing = PlatformFilters.FirstOrDefault(item =>
                string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                PlatformFilters.Move(PlatformFilters.IndexOf(existing), index);
                existing.Count = count;
            }
            else
            {
                PlatformFilters.Insert(index, new PlatformFilterItem(name) { Count = count });
            }
        }

        var current = SelectedPlatform;
        _syncingPlatformFilter = true;
        SelectedPlatformFilter = PlatformFilters
            .FirstOrDefault(item => string.Equals(item.Name, current, StringComparison.OrdinalIgnoreCase))
            ?? PlatformFilters[0];
        _syncingPlatformFilter = false;

        if (!string.Equals(SelectedPlatform, SelectedPlatformFilter.Name, StringComparison.OrdinalIgnoreCase))
            SelectedPlatform = SelectedPlatformFilter.Name;
    }

    /// <summary>筛选下拉：全部值 + 字典值 + 游戏中实际出现的值，增量同步避免绑定被置空。</summary>
    private static void RebuildFilter(ObservableCollection<string> target, string allValue,
        IEnumerable<string> dictionaryValues, IEnumerable<string> gameValues)
    {
        var desired = new List<string> { allValue };
        desired.AddRange(dictionaryValues.Where(v => !string.IsNullOrWhiteSpace(v)));
        desired.AddRange(gameValues.Where(v => !string.IsNullOrWhiteSpace(v)));
        desired = desired.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        for (var index = target.Count - 1; index >= 0; index--)
        {
            if (!desired.Contains(target[index], StringComparer.OrdinalIgnoreCase)) target.RemoveAt(index);
        }

        foreach (var value in desired)
        {
            if (!target.Contains(value, StringComparer.OrdinalIgnoreCase)) target.Add(value);
        }
    }

    /// <summary>增量同步下拉集合，避免 Clear 触发绑定把当前筛选值置空。</summary>
    private static void Rebuild(ObservableCollection<string> target, string allValue, IEnumerable<string> values,
        string current, Action<string> setSelected)
    {
        var desired = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .ToList();

        if (target.Count == 0) target.Add(allValue);
        else if (!string.Equals(target[0], allValue, StringComparison.Ordinal)) target.Insert(0, allValue);

        for (var index = target.Count - 1; index >= 1; index--)
        {
            if (!desired.Contains(target[index], StringComparer.OrdinalIgnoreCase))
                target.RemoveAt(index);
        }

        foreach (var value in desired)
        {
            if (!target.Contains(value, StringComparer.OrdinalIgnoreCase))
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
            if (target.Status == GameStatus.NotPlayed) target.Status = GameStatus.Playing;
            StatusMessage = $"「{target.Title}」已退出，本次 {TimeSpan.FromSeconds(seconds):mm\\:ss}";
            GamesView.Refresh();
        });
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedPlatformChanged(string value)
    {
        ApplyFilter();

        if (_syncingPlatformFilter) return;
        var match = PlatformFilters
            .FirstOrDefault(item => string.Equals(item.Name, value, StringComparison.OrdinalIgnoreCase));
        if (match == null || ReferenceEquals(match, SelectedPlatformFilter)) return;

        _syncingPlatformFilter = true;
        SelectedPlatformFilter = match;
        _syncingPlatformFilter = false;
    }

    partial void OnSelectedPlatformFilterChanged(PlatformFilterItem? value)
    {
        if (_syncingPlatformFilter || value == null) return;
        SelectedPlatform = value.Name;
    }

    partial void OnSelectedStatusChanged(string value) => ApplyFilter();
    partial void OnSelectedTagChanged(string value) => ApplyFilter();
    partial void OnSelectedRegionChanged(string value) => ApplyFilter();
    partial void OnSelectedLanguageChanged(string value) => ApplyFilter();
    partial void OnSelectedDeveloperChanged(string value) => ApplyFilter();
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

        var libraryRoot = AppServices.LibraryRoot;
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
                        var platformSetting = AppServices.Platforms.Get(candidate.Platform);
                        if (platformSetting == null) AppServices.Platforms.EnsureExists(candidate.Platform);

                        var folder = platformSetting?.Folder;
                        romPath = result.Mode switch
                        {
                            ImportMode.Copy => RomImporter.CopyToLibrary(romPath, libraryRoot, candidate.Platform, folder),
                            ImportMode.Move => RomImporter.MoveToLibrary(romPath, libraryRoot, candidate.Platform, folder),
                            _ => romPath,
                        };

                        var game = new Game
                        {
                            Title = candidate.Title.Trim(),
                            Platform = candidate.Platform,
                            RomPath = romPath,
                            RomHash = string.IsNullOrEmpty(candidate.RomHash)
                                ? RomHasher.Compute(romPath)
                                : candidate.RomHash,
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
    private void BatchEdit()
    {
        var games = SelectedGames.ToList();
        if (games.Count == 0)
        {
            MessageBox.Show("请先在列表中选中要批量编辑的游戏（可按住 Ctrl / Shift 多选）。",
                "批量编辑", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = new BatchEditWindow(games.Count) { Owner = Application.Current?.MainWindow };
        if (window.ShowDialog() != true || window.Changes == null) return;

        foreach (var game in games)
        {
            window.Changes.ApplyTo(game);
            AppServices.GameRepo.Update(game);
        }

        RefreshLookups();
        ApplyFilter();
        StatusMessage = $"已批量更新 {games.Count} 条";
    }

    [RelayCommand]
    private void AiQuery()
    {
        if (!AppServices.Ai.Options.IsConfigured)
        {
            var answer = MessageBox.Show("还没有配置 AI。\n\n现在打开设置 → AI 配置？",
                "AI 查询", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes) OpenSettings();
            return;
        }

        var window = new AiQueryWindow(Games.ToList()) { Owner = Application.Current?.MainWindow };
        if (window.ShowDialog() != true || window.Plan == null)
        {
            StatusMessage = "已取消 AI 查询";
            return;
        }

        ApplyAiFilter(window.Plan);
        if (window.Plan.Intent == "action")
        {
            var affected = ExecuteAiAction(window.Plan);
            StatusMessage = $"AI 操作完成：影响 {affected} 条";
        }
        else
        {
            StatusMessage = $"AI 查询已应用：筛选出 {FilteredCount} 条";
        }
    }

    [RelayCommand]
    private void AiTags()
    {
        var games = SelectedGames.ToList();
        if (games.Count == 0)
        {
            MessageBox.Show("请先在列表中选中要生成标签的游戏（可按住 Ctrl / Shift 多选）。",
                "AI 标签", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!AppServices.Ai.Options.IsConfigured)
        {
            var answer = MessageBox.Show("还没有配置 AI。\n\n现在打开设置 → AI 配置？",
                "AI 标签", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes) OpenSettings();
            return;
        }

        var options = AiOptions.Load(AppServices.Settings);
        if (games.Count > options.BatchLimit)
        {
            MessageBox.Show($"已选 {games.Count} 条，超过单次批量上限 {options.BatchLimit} 条。请分批处理。",
                "AI 标签", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var window = new AiTagPreviewWindow(games) { Owner = Application.Current?.MainWindow };
        if (window.ShowDialog() != true)
        {
            StatusMessage = "已取消 AI 标签";
            return;
        }

        RefreshLookups();
        ApplyFilter();
        StatusMessage = $"已应用 {window.AppliedCount} 条标签建议";
    }

    [RelayCommand]
    private void AiTranslate()
    {
        var games = SelectedGames.ToList();
        if (games.Count == 0)
        {
            MessageBox.Show("请先在列表中选中要翻译标题的游戏（可按住 Ctrl / Shift 多选）。",
                "AI 标题翻译", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!AppServices.Ai.Options.IsConfigured)
        {
            var answer = MessageBox.Show("还没有配置 AI。\n\n现在打开设置 → AI 配置？",
                "AI 标题翻译", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes) OpenSettings();
            return;
        }

        var options = AiOptions.Load(AppServices.Settings);
        if (games.Count > options.BatchLimit)
        {
            MessageBox.Show($"已选 {games.Count} 条，超过单次批量上限 {options.BatchLimit} 条。请分批处理。",
                "AI 标题翻译", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var window = new AiRenamePreviewWindow(games) { Owner = Application.Current?.MainWindow };
        if (window.ShowDialog() != true)
        {
            StatusMessage = "已取消标题翻译";
            return;
        }

        RefreshLookups();
        ApplyFilter();
        StatusMessage = $"已改写 {window.AppliedTitles} 条标题"
                        + (window.RenamedFiles > 0 ? $"，重命名 {window.RenamedFiles} 个文件" : "")
                        + (window.FailedFiles > 0 ? $"，失败 {window.FailedFiles} 个" : "");
    }

    [RelayCommand]
    private void Recommend()
    {
        if (Games.Count == 0)
        {
            MessageBox.Show("游戏库还是空的，先导入游戏吧。", "推荐游戏",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var seed = SelectedGame
                   ?? Games.FirstOrDefault(g => g.Status == GameStatus.Playing)
                   ?? Games.FirstOrDefault(g => g.Status == GameStatus.NotPlayed)
                   ?? Games.FirstOrDefault();

        var window = new RecommendWindow(seed, Games.ToList()) { Owner = Application.Current?.MainWindow };
        if (window.ShowDialog() != true || window.SelectedRecommendation == null)
        {
            StatusMessage = "已关闭推荐";
            return;
        }

        var game = window.SelectedRecommendation.Game;
        SearchText = game.Title;
        ApplyFilter();
        StatusMessage = $"已定位推荐游戏：{game.Title}";
    }

    /// <summary>把受控查询计划映射到现有筛选条件。</summary>
    public void ApplyAiFilter(AiQueryPlan plan) => ApplyQueryFilter(plan.Filter);

    public void ApplyQueryFilter(AiQueryFilter filter)
    {
        _aiGenre = filter.Genre;
        _aiPublisher = filter.Publisher;
        _aiYearMin = filter.YearMin;
        _aiYearMax = filter.YearMax;
        _aiNotFavorite = filter.Favorite == false;

        OnlyFavorites = filter.Favorite == true;
        OnlyMissing = filter.OnlyMissing;
        SelectedPlatform = string.IsNullOrWhiteSpace(filter.Platform) ? AllPlatforms : filter.Platform;
        SelectedStatus = string.IsNullOrWhiteSpace(filter.Status) ? AllStatuses : filter.Status;
        SelectedRegion = string.IsNullOrWhiteSpace(filter.Region) ? AllRegions : filter.Region;
        SelectedLanguage = string.IsNullOrWhiteSpace(filter.Language) ? AllLanguages : filter.Language;
        SelectedDeveloper = string.IsNullOrWhiteSpace(filter.Developer) ? AllDevelopers : filter.Developer;
        SelectedTag = string.IsNullOrWhiteSpace(filter.Tag) ? AllTags : filter.Tag;
        SearchText = filter.Keyword;
        ApplyFilter();
    }

    /// <summary>只对当前筛选结果执行白名单操作。</summary>
    public int ExecuteAiAction(AiQueryPlan plan)
    {
        var action = plan.Action;
        if (action == null) return 0;

        var targets = GamesView.Cast<Game>().ToList();
        foreach (var game in targets)
        {
            switch (action.Type)
            {
                case "set_status":
                    game.Status = action.Value;
                    break;
                case "toggle_favorite":
                    game.Favorite = !game.Favorite;
                    break;
                case "add_tag":
                    game.Tags = ChangeTags(game.Tags, action.Value, add: true);
                    break;
                case "remove_tag":
                    game.Tags = ChangeTags(game.Tags, action.Value, add: false);
                    break;
                default:
                    continue;
            }

            AppServices.GameRepo.Update(game);
        }

        RefreshLookups();
        ApplyFilter();
        return targets.Count;
    }

    private static string ChangeTags(string tags, string tag, bool add)
    {
        var list = SplitTags(tags).ToList();
        if (add)
        {
            if (!list.Contains(tag, StringComparer.OrdinalIgnoreCase)) list.Add(tag);
        }
        else
        {
            list.RemoveAll(item => string.Equals(item, tag, StringComparison.OrdinalIgnoreCase));
        }

        return string.Join(",", list);
    }

    [RelayCommand]
    private void Scrape()
    {
        var games = SelectedGames.ToList();
        if (games.Count == 0)
        {
            MessageBox.Show("请先在列表中选中要刮削的游戏（可按住 Ctrl / Shift 多选）。",
                "AI 刮削", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (games.Count > 10 && MessageBox.Show(
                $"已选 {games.Count} 条，刮削可能产生较多 AI 请求与费用。\n\n是否继续？",
                "AI 刮削", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var window = new ScrapePreviewWindow(games) { Owner = Application.Current?.MainWindow };
        if (window.ShowDialog() != true)
        {
            StatusMessage = "已取消刮削";
            return;
        }

        RefreshLookups();
        ApplyFilter();
        StatusMessage = $"已应用 {window.AppliedCount} 条刮削结果";
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
        var answer = MessageBox.Show(
            $"把选中的 {games.Count} 个条目合并为多碟游戏？\n\n第一行「{main.Title}」作为主条目，其余 {games.Count - 1} 条记录会被删除（ROM 文件本身不动）。",
            "合并为多碟游戏", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

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
                SpecialNote = game.SpecialNote,
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
    private void ConnectDevice()
    {
        if (ShowConnectDialog()) StatusMessage = $"已连接：{DeviceConnection!.Summary}";
    }

    private bool ShowConnectDialog()
    {
        var window = new ConnectDeviceWindow { Owner = Application.Current?.MainWindow };
        if (window.ShowDialog() != true || window.Connection == null) return false;

        DeviceConnection = window.Connection;
        return true;
    }

    [RelayCommand]
    private void DisconnectDevice()
    {
        if (DeviceConnection == null) return;
        DeviceConnection = null;
        StatusMessage = "已断开设备连接";
    }

    [RelayCommand]
    private void SendSelected()
    {
        var games = SelectedGames.ToList();
        if (games.Count == 0)
        {
            MessageBox.Show("请先在列表中选中要发送的游戏（可按住 Ctrl / Shift 多选）。",
                "发送到设备", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (DeviceConnection == null && !ShowConnectDialog()) return;

        AppServices.SendQueue.Enqueue(games, DeviceConnection!);
        ShowTaskWindow();
        StatusMessage = $"已加入发送队列：{games.Count} 个游戏";
    }

    [RelayCommand]
    private void OpenTaskList() => ShowTaskWindow();

    private void ShowTaskWindow()
    {
        if (_taskWindow == null)
        {
            _taskWindow = new SendTaskWindow { Owner = Application.Current?.MainWindow };
            _taskWindow.Closed += (_, _) => _taskWindow = null;
        }

        if (!_taskWindow.IsVisible) _taskWindow.Show();
        _taskWindow.Activate();
    }

    private void OnSendQueueCompleted(SendQueueSummary summary)
    {
        if (summary.Games == 0) return;

        StatusMessage = $"发送任务完成：复制 {summary.Copied}，跳过 {summary.Skipped}，失败 {summary.Failed}";
        MessageBox.Show(StatusMessage, "发送任务完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    partial void OnDeviceConnectionChanged(DeviceConnection? value)
    {
        OnPropertyChanged(nameof(HasConnection));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(ConnectionText));
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
        RefreshLibraryWarning();
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
        SelectedStatus = AllStatuses;
        SelectedTag = AllTags;
        SelectedRegion = AllRegions;
        SelectedLanguage = AllLanguages;
        SelectedDeveloper = AllDevelopers;
        OnlyFavorites = false;
        OnlyMissing = false;
        _aiGenre = "";
        _aiPublisher = "";
        _aiYearMin = null;
        _aiYearMax = null;
        _aiNotFavorite = false;
    }

    // ---------- AI 助手宿主能力 ----------

    public IReadOnlyList<Game> AllGames => Games;

    IReadOnlyList<Game> IAiAppHost.SelectedGames => SelectedGames;

    public IReadOnlyList<Game> FilteredGames => GamesView.Cast<Game>().ToList();

    public string ConnectedDevice => DeviceConnection?.Summary ?? "";

    public void ApplyLibraryFilter(AiQueryFilter filter) => ApplyQueryFilter(filter);

    public void RefreshLibrary()
    {
        RefreshLookups();
        TotalCount = Games.Count;
        ApplyFilter();
        LastRefreshText = $"上次刷新：{DateTime.Now:HH:mm:ss}";
    }

    public int UpdateGames(IReadOnlyList<long> ids, GamePatch patch)
    {
        if (!patch.HasAny) return 0;

        var targets = Games.Where(game => ids.Contains(game.Id)).ToList();
        foreach (var game in targets)
        {
            patch.ApplyTo(game);
            AppServices.GameRepo.Update(game);
        }

        RefreshLibrary();
        StatusMessage = $"AI 已更新 {targets.Count} 条游戏信息";
        return targets.Count;
    }

    public int DeleteGames(IReadOnlyList<long> ids, bool recycleFiles)
    {
        var targets = Games.Where(game => ids.Contains(game.Id)).ToList();
        foreach (var game in targets)
        {
            if (recycleFiles)
                foreach (var disc in game.Discs)
                    FileRecycleBin.MoveToRecycleBin(disc);

            AppServices.GameRepo.Delete(game.Id);
            Games.Remove(game);
        }

        TotalCount = Games.Count;
        RefreshLibrary();
        SetSelectedGames(Array.Empty<Game>());
        StatusMessage = recycleFiles
            ? $"AI 已移到回收站并移除 {targets.Count} 条记录"
            : $"AI 已从库中移除 {targets.Count} 条记录";
        return targets.Count;
    }

    public bool LaunchGame(long id, int discIndex, out string message)
    {
        message = "";
        var game = Games.FirstOrDefault(item => item.Id == id);
        if (game == null)
        {
            message = "没有找到该游戏。";
            return false;
        }

        var emulator = EmulatorResolver.Resolve(game, Emulators.ToList(), AppServices.EmulatorRepo);
        if (emulator == null)
        {
            message = $"还没有为平台「{game.Platform}」配置模拟器。";
            return false;
        }

        var discs = game.Discs;
        if (discIndex < 0 || discIndex >= discs.Count)
        {
            message = $"碟片序号 {discIndex + 1} 超出范围（共 {discs.Count} 碟）。";
            return false;
        }

        if (!AppServices.Launcher.TryLaunch(game, emulator, discs[discIndex], out message))
            return false;

        StatusMessage = $"AI 已用「{emulator.Name}」启动「{game.Title}」";
        return true;
    }

    public string SendGames(IReadOnlyList<long> ids)
    {
        if (DeviceConnection == null)
            return "还没有连接设备，请先点击工具栏「连接到设备」。";

        var games = Games.Where(game => ids.Contains(game.Id)).ToList();
        if (games.Count == 0) return "没有找到要发送的游戏。";

        AppServices.SendQueue.Enqueue(games, DeviceConnection);
        ShowTaskWindow();
        StatusMessage = $"AI 已加入发送队列：{games.Count} 个游戏";
        return $"已加入发送队列：{games.Count} 个游戏。";
    }

    public string GetSetting(string key) => AppServices.Settings.Get(key) ?? "";

    public void SetSetting(string key, string value)
    {
        AppServices.Settings.Set(key, value);
        if (key is NetworkClientProvider.ModeKey or NetworkClientProvider.AddressKey)
            AppServices.RebuildNetwork();

        StatusMessage = $"AI 已修改设置：{key}";
    }

    public void OpenWindow(string window)
    {
        switch (window.Trim().ToLowerInvariant())
        {
            case "settings":
            case "device_settings":
                OpenSettings();
                break;
            case "import":
                ImportCommand.Execute(null);
                break;
            case "share":
                ShareSelectedCommand.Execute(null);
                break;
            case "task_list":
                OpenTaskListCommand.Execute(null);
                break;
            case "connect_device":
                ConnectDeviceCommand.Execute(null);
                break;
            case "scrape":
                ScrapeCommand.Execute(null);
                break;
            case "ai_tags":
                AiTagsCommand.Execute(null);
                break;
            case "ai_translate":
                AiTranslateCommand.Execute(null);
                break;
            case "recommend":
                RecommendCommand.Execute(null);
                break;
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new SettingsWindow { Owner = Application.Current?.MainWindow };
        window.ShowDialog();

        LoadEmulators();
        LoadGames();
    }

    public void FocusSearchRequested() { }

    public void NotifySearchFocus() { }
}
