using System.Windows;
using MicaRomManager.Models;
using MicaRomManager.ViewModels;

namespace MicaRomManager.Views;

public partial class ConnectDeviceWindow : Window
{
    private readonly ConnectDeviceViewModel _viewModel;

    public ConnectDeviceWindow()
    {
        InitializeComponent();
        _viewModel = new ConnectDeviceViewModel();
        DataContext = _viewModel;
        _viewModel.RequestClose += result => DialogResult = result;
    }

    public DeviceConnection? Connection => _viewModel.Connection;
}
