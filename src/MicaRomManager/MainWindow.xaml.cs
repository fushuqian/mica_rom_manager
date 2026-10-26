using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private const string ViewStateKey = "list_view_state";

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closing += (_, _) => SaveViewState();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestoreViewState();
        SearchBox.Focus();
        Dispatcher.BeginInvoke(CheckPortableLibrary, DispatcherPriority.ApplicationIdle);
    }

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
        if (string.IsNullOrWhiteSpace(json)) return;

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

        _viewModel.OnlyFavorites = state.OnlyFavorites;
        _viewModel.OnlyMissing = state.OnlyMissing;
        if (!string.IsNullOrEmpty(state.Platform) && _viewModel.Platforms.Contains(state.Platform))
            _viewModel.SelectedPlatform = state.Platform;
        if (!string.IsNullOrEmpty(state.Status) && _viewModel.Statuses.Contains(state.Status))
            _viewModel.SelectedStatus = state.Status;
        if (!string.IsNullOrEmpty(state.Tag) && _viewModel.Tags.Contains(state.Tag))
            _viewModel.SelectedTag = state.Tag;
        if (!string.IsNullOrEmpty(state.Search))
            _viewModel.SearchText = state.Search;

        foreach (var column in GameGrid.Columns)
        {
            if (column.Header is not string header) continue;
            if (!state.ColumnWidths.TryGetValue(header, out var width)) continue;
            if (width >= 24) column.Width = new DataGridLength(width);
        }

        RestoreSort(state);
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
            Search = _viewModel.SearchText,
            Platform = _viewModel.SelectedPlatform,
            Status = _viewModel.SelectedStatus,
            Tag = _viewModel.SelectedTag,
            OnlyFavorites = _viewModel.OnlyFavorites,
            OnlyMissing = _viewModel.OnlyMissing,
            ColumnWidths = GameGrid.Columns
                .Where(c => c.Header is string)
                .ToDictionary(c => (string)c.Header, c => c.ActualWidth),
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
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) == null) return;
        if (_viewModel.SelectedGame == null) return;

        _viewModel.LaunchSelectedCommand.Execute(null);
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
