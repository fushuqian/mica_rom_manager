using System.Windows;
using MicaRomManager.Models;
using MicaRomManager.ViewModels;

namespace MicaRomManager.Views;

public partial class EmulatorEditWindow : Window
{
    public EmulatorEditWindow(Emulator emulator, bool isNew)
    {
        InitializeComponent();
        var viewModel = new EmulatorEditViewModel(emulator, isNew);
        DataContext = viewModel;
        viewModel.RequestClose += result => DialogResult = result;
    }
}
