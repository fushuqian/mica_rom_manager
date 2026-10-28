using System.Windows;
using MicaRomManager.Models;
using MicaRomManager.ViewModels;

namespace MicaRomManager.Views;

public partial class BatchEditWindow : Window
{
    public BatchEditViewModel ViewModel { get; }
    public BatchEditChanges? Changes { get; private set; }

    public BatchEditWindow(int selectedCount)
    {
        InitializeComponent();
        ViewModel = new BatchEditViewModel(selectedCount);
        DataContext = ViewModel;
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var changes = ViewModel.BuildChanges();
        if (changes == null) return;

        Changes = changes;
        DialogResult = true;
    }
}
