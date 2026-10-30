using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using MicaRomManager.Models;
using MicaRomManager.Services;
using MicaRomManager.ViewModels;
using MicaRomManager.Views;
using Microsoft.Win32;

namespace MicaRomManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AiAssistantViewModel _assistantViewModel;
    private bool _assistantOpen;

    private const string ViewStateKey = "list_view_state";
    private const string AssistantOpenKey = "ui.assistant_open_v2";

    public MainWindow()
    {
        InitializeComponent();
        WindowBackdrop.Apply(this, WindowBackdrop.BackdropKind.Acrylic);
        RootGrid.Opacity = 0;
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        FilterPopup.DataContext = _viewModel;
        _assistantViewModel = new AiAssistantViewModel(_viewModel);
        AssistantPanel.DataContext = _assistantViewModel;
        AssistantPanel.CloseRequested += (_, _) => SetAssistantOpen(false);
        Loaded += OnLoaded;
        Closing += (_, _) => SaveViewState();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestoreViewState();
        RestoreAssistantState();
        SearchBox.Focus();
        AnimateIn();
        PlatformSidebar.SelectionChanged += (_, _) => FadeTable();
        Dispatcher.BeginInvoke(CheckPortableLibrary, DispatcherPriority.ApplicationIdle);
    }

    private void AnimateIn()
    {
        RootGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void FadeTable()
    {
        GameGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void RestoreAssistantState() =>
        SetAssistantOpen(AppServices.Settings.GetBool(AssistantOpenKey), persist: false);

    private void ToggleAssistant_Click(object sender, RoutedEventArgs e) =>
        SetAssistantOpen(AssistantPanel.Visibility != Visibility.Visible);

    /// <summary>鼠标进入区块：投影层淡入，区块在窗口内浮起；离开则收回。</summary>
    private void SideHost_MouseEnter(object sender, MouseEventArgs e) => LiftPanel(SideShadow, 1, 140);

    private void SideHost_MouseLeave(object sender, MouseEventArgs e) => LiftPanel(SideShadow, 0, 180);

    private void MainHost_MouseEnter(object sender, MouseEventArgs e) => LiftPanel(MainShadow, 1, 140);

    private void MainHost_MouseLeave(object sender, MouseEventArgs e) => LiftPanel(MainShadow, 0, 180);

    private static void LiftPanel(UIElement shadow, double to, int milliseconds) =>
        shadow.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase
            {
                EasingMode = to > 0 ? EasingMode.EaseOut : EasingMode.EaseIn,
            },
        });

    /// <summary>工具栏「AI 工具」：把低频的 AI 动作收进菜单。</summary>
    private void AiTools_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = AiToolsButton,
            Placement = PlacementMode.Bottom,
        };

        AddMenuCommand(menu, "AI 刮削信息", _viewModel.ScrapeCommand);
        AddMenuCommand(menu, "AI 生成标签", _viewModel.AiTagsCommand);
        AddMenuCommand(menu, "AI 翻译", _viewModel.AiTranslateCommand);
        AddMenuCommand(menu, "推荐游戏", _viewModel.RecommendCommand);
        menu.IsOpen = true;
    }

    private static void AddMenuCommand(ContextMenu menu, string header, ICommand command) =>
        menu.Items.Add(new MenuItem { Header = header, Command = command });

    private void FilterButton_Click(object sender, RoutedEventArgs e) =>
        FilterPopup.IsOpen = !FilterPopup.IsOpen;

    private void CloseFilter_Click(object sender, RoutedEventArgs e) => FilterPopup.IsOpen = false;

    /// <summary>右键「AI 问问」：把选中游戏推进助手并直接咨询。</summary>
    private void AiAsk_Click(object sender, RoutedEventArgs e)
    {
        var game = _viewModel.SelectedGame;
        if (game == null)
        {
            MessageBox.Show("请先在列表中选中一款游戏。", "AI 问问",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SetAssistantOpen(true);
        _ = _assistantViewModel.AskAboutAsync(game);
    }

    private void SetAssistantOpen(bool open, bool persist = true)
    {
        _assistantOpen = open;

        if (open)
        {
            AssistantPanel.Visibility = Visibility.Visible;
            AssistantPanel.Opacity = 0;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            AssistantHost.BeginAnimation(WidthProperty, new DoubleAnimation(0, AssistantWidth,
                TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            AssistantPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1,
                TimeSpan.FromMilliseconds(200)) { EasingFunction = ease },
                HandoffBehavior.SnapshotAndReplace);
        }
        else if (AssistantPanel.Visibility == Visibility.Visible)
        {
            var collapse = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            };
            collapse.Completed += (_, _) =>
            {
                if (_assistantOpen) return;
                AssistantPanel.Visibility = Visibility.Collapsed;
                AssistantPanel.Opacity = 1;
            };

            AssistantHost.BeginAnimation(WidthProperty, collapse);
            AssistantPanel.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140)));
        }

        if (persist) AppServices.Settings.Set(AssistantOpenKey, open.ToString());

        if (!open) return;
        Dispatcher.BeginInvoke(() =>
        {
            AssistantPanel.FocusInput();
            AssistantPanel.Focus();
        }, DispatcherPriority.Input);
    }

    /// <summary>右侧助手栏展开宽度。</summary>
    private const double AssistantWidth = 300;

    /// <summary>便携模式下每次启动检查库目录，找不到时让用户重新指定或重建。</summary>
    private void CheckPortableLibrary()
    {
        if (!PortablePath.Enabled) return;

        var root = AppServices.LibraryRoot;
        if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root)) return;

        var message = (string.IsNullOrWhiteSpace(root)
                ? "便携模式已开启，但还没有设置库目录。"
                : $"便携模式下找不到库目录：\n{root}")
            + "\n\n「是」重新指定库目录（保留旧记录，并自动对到新位置）"
            + "\n「否」放弃旧库：指定新文件夹并清空游戏库（模拟器与设置保留）"
            + "\n「取消」退出程序";

        switch (MessageBox.Show(message, "便携模式：库目录不可用",
                    MessageBoxButton.YesNoCancel, MessageBoxImage.Warning))
        {
            case MessageBoxResult.Yes:
                new SettingsWindow { Owner = this }.ShowDialog();
                RefreshGames();
                break;
            case MessageBoxResult.No:
                RebuildLibrary();
                break;
            default:
                Application.Current.Shutdown();
                break;
        }
    }

    private void RebuildLibrary()
    {
        var dialog = new OpenFolderDialog { Title = "选择新的 ROM 库目录" };
        if (dialog.ShowDialog() != true) return;

        var newRoot = Path.GetFullPath(dialog.FolderName);
        var confirm = MessageBox.Show(
            $"将清空所有游戏记录（模拟器、平台设置、区域/语言词典都保留），并把库目录设为：\n{newRoot}\n\n是否继续？",
            "放弃旧库并新建", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            Directory.CreateDirectory(newRoot);
            AppServices.GameRepo.DeleteAll();
            AppServices.Settings.Set("library.root", PortablePath.ToStored(newRoot, true));
            RefreshGames();
            MessageBox.Show("新的游戏库已建立。", "便携模式", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"建立新库失败：{ex.Message}", "便携模式", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>还原上次的列宽、排序与筛选条件。</summary>
    private void RestoreViewState()
    {
        var json = AppServices.Settings.Get(ViewStateKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            ApplyHiddenColumns(new ListViewState
            {
                LayoutVersion = 5,
                HiddenColumns = ListViewState.DefaultHiddenColumns.ToList(),
            });
            return;
        }

        ListViewState? state;
        try
        {
            state = JsonSerializer.Deserialize<ListViewState>(json);
        }
        catch
        {
            return;
        }

        if (state == null) return;

        var legacyLayout = state.LayoutVersion < 5;

        // 换布局时直接套新默认列，避免旧布局的隐藏列（如类型/平台）残留下来
        if (legacyLayout)
        {
            state.HiddenColumns = ListViewState.DefaultHiddenColumns.ToList();
        }
        state.LayoutVersion = 5;

        _viewModel.OnlyFavorites = state.OnlyFavorites;
        _viewModel.OnlyMissing = state.OnlyMissing;
        if (!string.IsNullOrEmpty(state.Platform) && _viewModel.Platforms.Contains(state.Platform))
            _viewModel.SelectedPlatform = state.Platform;
        if (!string.IsNullOrEmpty(state.Status) && _viewModel.Statuses.Contains(state.Status))
            _viewModel.SelectedStatus = state.Status;
        if (!string.IsNullOrEmpty(state.Tag) && _viewModel.Tags.Contains(state.Tag))
            _viewModel.SelectedTag = state.Tag;
        if (!string.IsNullOrEmpty(state.Region) && _viewModel.RegionFilters.Contains(state.Region))
            _viewModel.SelectedRegion = state.Region;
        if (!string.IsNullOrEmpty(state.Language) && _viewModel.LanguageFilters.Contains(state.Language))
            _viewModel.SelectedLanguage = state.Language;
        if (!string.IsNullOrEmpty(state.Developer) && _viewModel.DeveloperFilters.Contains(state.Developer))
            _viewModel.SelectedDeveloper = state.Developer;
        if (!string.IsNullOrEmpty(state.Search))
            _viewModel.SearchText = state.Search;

        // 旧布局的列宽是给旧列/旧密度用的，直接套新默认宽度，避免状态列被压成一条
        if (!legacyLayout)
        {
            foreach (var column in GameGrid.Columns)
            {
                if (column.Header is not string header) continue;
                if (!state.ColumnWidths.TryGetValue(header, out var width)) continue;
                if (width >= 24) column.Width = new DataGridLength(width);
            }
        }

        ApplyHiddenColumns(state);

        RestoreSort(state);
    }

    /// <summary>还原列显隐；未知列名（改过列名）自动忽略，且至少保留一列。</summary>
    private void ApplyHiddenColumns(ListViewState state)
    {
        var headers = GameGrid.Columns
            .Where(column => column.Header is string)
            .Select(column => (string)column.Header)
            .ToList();

        state.PruneHidden(headers);

        foreach (var column in GameGrid.Columns)
        {
            if (column.Header is not string header) continue;
            column.Visibility = state.HiddenColumns.Contains(header, StringComparer.Ordinal)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    /// <summary>工具栏「列设置」：列出所有列，勾选切换显隐。</summary>
    private void ColumnMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = ColumnMenuButton,
            Placement = PlacementMode.Bottom,
        };

        foreach (var column in GameGrid.Columns.Where(column => column.Header is string))
        {
            var item = new MenuItem
            {
                Header = column.Header,
                IsCheckable = true,
                IsChecked = column.Visibility == Visibility.Visible,
                StaysOpenOnClick = true,
            };
            item.Click += (_, _) => ToggleColumn(column, item);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void ToggleColumn(DataGridColumn column, MenuItem item)
    {
        if (column.Visibility == Visibility.Visible)
        {
            var visibleCount = GameGrid.Columns.Count(entry => entry.Visibility == Visibility.Visible);
            if (visibleCount <= 1)
            {
                item.IsChecked = true;
                _viewModel.StatusMessage = "至少要保留一列。";
                return;
            }

            column.Visibility = Visibility.Collapsed;
        }
        else
        {
            column.Visibility = Visibility.Visible;
        }

        item.IsChecked = column.Visibility == Visibility.Visible;
        SaveViewState();
    }

    private void RestoreSort(ListViewState state)
    {
        if (string.IsNullOrWhiteSpace(state.SortColumn)) return;
        if (!Enum.TryParse<ListSortDirection>(state.SortDirection, out var direction)) return;

        var column = GameGrid.Columns.FirstOrDefault(c =>
            string.Equals(c.Header as string, state.SortColumn, StringComparison.Ordinal));
        if (column == null || string.IsNullOrWhiteSpace(column.SortMemberPath)) return;

        column.SortDirection = direction;
        _viewModel.GamesView.SortDescriptions.Clear();
        _viewModel.GamesView.SortDescriptions.Add(new SortDescription(column.SortMemberPath, direction));
    }

    private void SaveViewState()
    {
        var state = new ListViewState
        {
            LayoutVersion = 3,
            Search = _viewModel.SearchText,
            Platform = _viewModel.SelectedPlatform,
            Status = _viewModel.SelectedStatus,
            Tag = _viewModel.SelectedTag,
            Region = _viewModel.SelectedRegion,
            Language = _viewModel.SelectedLanguage,
            Developer = _viewModel.SelectedDeveloper,
            OnlyFavorites = _viewModel.OnlyFavorites,
            OnlyMissing = _viewModel.OnlyMissing,
            ColumnWidths = GameGrid.Columns
                .Where(c => c.Header is string)
                .ToDictionary(
                    c => (string)c.Header,
                    c => c.Visibility == Visibility.Visible
                        ? c.ActualWidth
                        : c.Width.IsAbsolute ? c.Width.Value : c.ActualWidth),
            HiddenColumns = GameGrid.Columns
                .Where(c => c.Header is string && c.Visibility != Visibility.Visible)
                .Select(c => (string)c.Header)
                .ToList(),
        };

        var sorted = GameGrid.Columns.FirstOrDefault(c => c.SortDirection != null);
        if (sorted != null)
        {
            state.SortColumn = sorted.Header as string ?? "";
            state.SortDirection = sorted.SortDirection!.Value.ToString();
        }

        AppServices.Settings.Set(ViewStateKey, JsonSerializer.Serialize(state));
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SetSelectedGames(GameGrid.SelectedItems.Cast<Game>());
    }

    /// <summary>供设置窗口在扫描补录后刷新列表。</summary>
    public void RefreshGames() => _viewModel.LoadGames();

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (EditModeToggle.IsChecked == true) return;
        var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell?.Column == RatingColumn) return;
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) == null) return;
        if (_viewModel.SelectedGame == null) return;

        _viewModel.LaunchSelectedCommand.Execute(null);
    }

    /// <summary>点击星标直接评分；再次点击同一颗星清空评分。</summary>
    private void RatingStar_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not Game game) return;
        if (!int.TryParse(element.Tag as string, out var star)) return;

        game.Rating = game.Rating == star ? 0 : star;
        e.Handled = true;
        _viewModel.SaveInline(game);
    }

    private void OnEditModeChanged(object sender, RoutedEventArgs e)
    {
        GameGrid.IsReadOnly = EditModeToggle.IsChecked != true;
    }

    private void OnCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not Game game) return;

        Dispatcher.BeginInvoke(() => _viewModel.SaveInline(game), DispatcherPriority.Background);
    }

    private void OnRowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        Dispatcher.BeginInvoke(_viewModel.RefreshFilterAfterEdit, DispatcherPriority.ApplicationIdle);
    }

    private void OnCurrentCellChanged(object sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(_viewModel.RefreshFilterAfterEdit, DispatcherPriority.ApplicationIdle);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        if (e.OriginalSource is TextBox || e.OriginalSource is ComboBox) return;
        if (EditModeToggle.IsChecked == true) return;

        switch (e.Key)
        {
            case Key.Enter when _viewModel.SelectedGame != null:
                _viewModel.LaunchSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F2 when _viewModel.SelectedGame != null:
                _viewModel.EditSelectedCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Delete when _viewModel.SelectedGame != null:
                _viewModel.RemoveSelectedCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var contextMenu = GameGrid.ContextMenu;
        if (contextMenu == null) return;

        var launchWith = contextMenu.Items.OfType<MenuItem>()
            .FirstOrDefault(item => (item.Header as string) == "用指定模拟器启动");
        if (launchWith == null) return;

        launchWith.Items.Clear();
        var emulators = _viewModel.GetEmulatorsForSelection();
        if (emulators.Count == 0)
        {
            launchWith.Items.Add(new MenuItem { Header = "（该平台暂无可用模拟器）", IsEnabled = false });
            return;
        }

        foreach (var emulator in emulators)
        {
            launchWith.Items.Add(new MenuItem
            {
                Header = emulator.Name,
                Command = _viewModel.LaunchWithCommand,
                CommandParameter = emulator,
            });
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        await _viewModel.ImportAsync(paths, true);
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T match) return match;
            element = element is Visual or Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return null;
    }
}
