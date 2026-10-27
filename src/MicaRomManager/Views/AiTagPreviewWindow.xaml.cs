using System.Windows;
using MicaRomManager.Models;
using MicaRomManager.ViewModels;

namespace MicaRomManager.Views;

public partial class AiTagPreviewWindow : Window
{
    private readonly AiTagPreviewViewModel _viewModel;

    public int AppliedCount { get; private set; }

    public AiTagPreviewWindow(IReadOnlyList<Game> games)
    {
        InitializeComponent();
        _viewModel = new AiTagPreviewViewModel(games);
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.RunAsync();
        Closed += (_, _) => _viewModel.Cancel();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        AppliedCount = _viewModel.Apply();
        if (AppliedCount == 0)
        {
            MessageBox.Show("没有可应用的行（未勾选或全部有错误）。", "AI 标签",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Cancel();
        DialogResult = false;
    }
}
