using System.Windows;
using System.Windows.Controls;
using MicaRomManager.Models;
using MicaRomManager.ViewModels;

namespace MicaRomManager.Views;

public partial class AiQueryWindow : Window
{
    private readonly AiQueryViewModel _viewModel;
    private CancellationTokenSource? _cts;

    public AiQueryPlan? Plan => _viewModel.Plan;

    public AiQueryWindow(IReadOnlyList<Game> games)
    {
        InitializeComponent();
        _viewModel = new AiQueryViewModel(games);
        DataContext = _viewModel;
        Closed += (_, _) => _cts?.Cancel();
    }

    private async void Parse_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        await _viewModel.BuildAsync(_cts.Token);
    }

    private void Example_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text }) _viewModel.Request = text;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var plan = _viewModel.Plan;
        if (plan == null || !_viewModel.CanApply) return;

        var isAction = plan.Intent == "action";
        var message = isAction
            ? $"将对筛选出的 {_viewModel.HitCount} 条执行操作：\n{_viewModel.Summary}\n\n是否继续？"
            : $"按条件筛选出 {_viewModel.HitCount} 条并应用到主列表？";
        if (MessageBox.Show(message, "AI 查询", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;

        DialogResult = true;
    }
}
