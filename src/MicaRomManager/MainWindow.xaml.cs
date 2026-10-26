using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using MicaRomManager.Models;
using MicaRomManager.ViewModels;

namespace MicaRomManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SetSelectedGames(GameGrid.SelectedItems.Cast<Game>());
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (EditModeToggle.IsChecked == true) return;
        if (FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject) == null) return;
        if (_viewModel.SelectedGame == null) return;

        _viewModel.EditSelectedCommand.Execute(null);
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
