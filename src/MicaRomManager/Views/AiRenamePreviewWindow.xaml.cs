using System.Windows;
using MicaRomManager.Models;
using MicaRomManager.ViewModels;

namespace MicaRomManager.Views;

public partial class AiRenamePreviewWindow : Window
{
    private readonly AiRenamePreviewViewModel _viewModel;

    public int AppliedTitles { get; private set; }
    public int RenamedFiles { get; private set; }
    public int FailedFiles { get; private set; }

    public AiRenamePreviewWindow(IReadOnlyList<Game> games)
    {
        InitializeComponent();
        _viewModel = new AiRenamePreviewViewModel(games);
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.RunAsync();
        Closed += (_, _) => _viewModel.Cancel();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var count = _viewModel.Rows.Count(r => r.Apply && r.Error.Length == 0);
        if (count == 0)
        {
            MessageBox.Show("没有可应用的行（未勾选或全部有错误）。", "AI 标题翻译",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var message = _viewModel.RenameFiles
            ? $"将改写 {count} 条标题，并整理库内文件名（无法自动撤销），是否继续？"
            : $"将改写 {count} 条标题，不动任何文件，是否继续？";
        if (MessageBox.Show(message, "AI 标题翻译", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;

        var (titles, renamed, failed) = _viewModel.Apply();
        AppliedTitles = titles;
        RenamedFiles = renamed;
        FailedFiles = failed;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Cancel();
        DialogResult = false;
    }
}
