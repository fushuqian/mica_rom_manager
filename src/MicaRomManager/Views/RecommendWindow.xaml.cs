using System.Windows;
using System.Windows.Input;
using MicaRomManager.Models;
using MicaRomManager.Services.Recommendation;
using MicaRomManager.ViewModels;

namespace MicaRomManager.Views;

public partial class RecommendWindow : Window
{
    private readonly RecommendViewModel _viewModel;

    public RecommendationItem? SelectedRecommendation => _viewModel.SelectedItem;

    public RecommendWindow(Game? seed, IReadOnlyList<Game> all)
    {
        InitializeComponent();
        _viewModel = new RecommendViewModel(seed, all);
        DataContext = _viewModel;
    }

    private void RowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedItem == null) return;
        if (e.ChangedButton != MouseButton.Left) return;
        DialogResult = true;
    }
}
