using System.Windows.Controls;
using MicaRomManager.ViewModels;

namespace MicaRomManager.Views;

public partial class DeviceSettingsView : UserControl
{
    public DeviceSettingsView()
    {
        InitializeComponent();
        DataContext = new DeviceSettingsViewModel();
    }
}
