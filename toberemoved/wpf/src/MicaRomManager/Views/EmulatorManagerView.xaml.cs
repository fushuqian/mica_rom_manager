using System.Windows.Controls;
using MicaRomManager.ViewModels;

namespace MicaRomManager.Views;

public partial class EmulatorManagerView : UserControl
{
    public EmulatorManagerView()
    {
        InitializeComponent();
        DataContext = new EmulatorManagerViewModel();
    }
}
