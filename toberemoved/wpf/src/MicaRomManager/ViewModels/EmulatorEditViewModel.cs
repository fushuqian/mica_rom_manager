using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaRomManager.Models;
using MicaRomManager.Services;
using Microsoft.Win32;

namespace MicaRomManager.ViewModels;

public partial class EmulatorEditViewModel : ObservableObject
{
    public Emulator Model { get; }
    public bool IsNew { get; }

    [ObservableProperty] private string error = "";

    public event Action<bool>? RequestClose;

    public EmulatorEditViewModel(Emulator emulator, bool isNew)
    {
        Model = emulator;
        IsNew = isNew;
    }

    [RelayCommand]
    private void BrowseExe()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择模拟器可执行文件",
            Filter = "可执行文件|*.exe|所有文件|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        Model.ExePath = ToRelative(dialog.FileName);
    }

    [RelayCommand]
    private void BrowseInstallDir()
    {
        var dialog = new OpenFolderDialog { Title = "选择模拟器安装目录" };
        if (dialog.ShowDialog() == true) Model.InstallDir = LocalPath.ToStored(dialog.FolderName);
    }

    [RelayCommand]
    private void Save()
    {
        Error = "";

        if (string.IsNullOrWhiteSpace(Model.Name))
        {
            Error = "名称不能为空。";
            return;
        }

        if (!string.IsNullOrWhiteSpace(Model.WebsiteUrl))
        {
            var url = Model.WebsiteUrl.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                Error = "官网地址需要是 http:// 或 https:// 开头的完整网址。";
                return;
            }
        }

        try
        {
            AppServices.EmulatorRepo.Save(Model);
        }
        catch (Exception ex)
        {
            Error = $"保存失败：{ex.Message}";
            return;
        }

        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);

    private string ToRelative(string absolutePath)
    {
        var stored = LocalPath.ToStored(absolutePath);
        if (stored != absolutePath) return stored;
        if (string.IsNullOrWhiteSpace(Model.InstallDir)) return absolutePath;

        try
        {
            var installDir = LocalPath.Expand(Model.InstallDir);
            var relative = Path.GetRelativePath(installDir, absolutePath);
            return relative.StartsWith("..", StringComparison.Ordinal) ? absolutePath : relative;
        }
        catch
        {
            return absolutePath;
        }
    }
}
