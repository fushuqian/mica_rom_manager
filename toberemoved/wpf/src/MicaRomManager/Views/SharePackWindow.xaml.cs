using System.Windows;
using MicaRomManager.Models;
using MicaRomManager.ViewModels;

namespace MicaRomManager.Views;

public partial class SharePackWindow : Window
{
    private readonly SharePackViewModel _viewModel;

    public SharePackWindow(IReadOnlyList<Game> games)
    {
        InitializeComponent();
        _viewModel = new SharePackViewModel(games);
        DataContext = _viewModel;
    }

    public bool Exported => _viewModel.Exported;
}
