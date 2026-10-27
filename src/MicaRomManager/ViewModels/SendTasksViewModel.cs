using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaRomManager.Models;
using MicaRomManager.Services.Sending;

namespace MicaRomManager.ViewModels;

public partial class SendTasksViewModel : ObservableObject
{
    public ObservableCollection<SendTaskItem> Tasks => AppServices.SendQueue.Tasks;

    [ObservableProperty] private SendTaskItem? selectedTask;

    [RelayCommand]
    private void ClearFinished()
    {
        AppServices.SendQueue.ClearFinished();
        SelectedTask = null;
    }

    [RelayCommand]
    private void OpenTarget()
    {
        var folder = SelectedTask?.TargetFolder;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }
}
