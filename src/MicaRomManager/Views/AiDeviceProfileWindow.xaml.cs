using System.Windows;
using MicaRomManager.Models;
using MicaRomManager.ViewModels;

namespace MicaRomManager.Views;

public partial class AiDeviceProfileWindow : Window
{
    private readonly AiDeviceProfileViewModel _viewModel;

    public AiDeviceProfileWindow(DeviceScanSummary summary,
        MicaRomManager.Services.Devices.DeviceProfileResult result)
    {
        InitializeComponent();
        _viewModel = new AiDeviceProfileViewModel(summary, result);
        DataContext = _viewModel;
    }

    public MicaRomManager.Models.DeviceProfile? Profile => _viewModel.Profile;

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanApply) return;
        DialogResult = true;
    }
}
