using System.Windows;
using MicaRomManager.Models;
using MicaRomManager.ViewModels;

namespace MicaRomManager.Views;

public partial class GameEditWindow : Window
{
    public GameEditWindow(Game game, bool isNew)
    {
        InitializeComponent();
        var viewModel = new GameEditViewModel(game, isNew);
        DataContext = viewModel;
        viewModel.RequestClose += result => DialogResult = result;
    }
}
