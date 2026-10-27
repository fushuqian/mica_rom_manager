using System.ComponentModel;
using System.Windows;
using MicaRomManager.Models;
using MicaRomManager.ViewModels;

namespace MicaRomManager.Views;

public partial class DeviceSendWindow : Window
{
    private readonly DeviceSendViewModel _viewModel;

    public DeviceSendWindow(IReadOnlyList<Game> games)
    {
        InitializeComponent();
        _viewModel = new DeviceSendViewModel(games);
        DataContext = _viewModel;
    }

    public bool Sent => _viewModel.Sent;

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.IsBusy) return;

        e.Cancel = true;
        MessageBox.Show("正在发送文件，请等当前任务完成后再关闭。", "发送到设备",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
