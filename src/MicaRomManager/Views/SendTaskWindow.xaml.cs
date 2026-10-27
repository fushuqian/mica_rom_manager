using System.Windows;
using MicaRomManager.ViewModels;

namespace MicaRomManager.Views;

public partial class SendTaskWindow : Window
{
    public SendTaskWindow()
    {
        InitializeComponent();
        DataContext = new SendTasksViewModel();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
