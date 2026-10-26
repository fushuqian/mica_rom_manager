using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaRomManager.Models;
using Microsoft.Win32;

namespace MicaRomManager.ViewModels;

public partial class EmulatorEditViewModel : ObservableObject
{
    public Emulator Model { get; }
    public bool IsNew { get; }

    [ObservableProperty] private string platformsText = "";
    [ObservableProperty] private bool makeDefaultForPlatforms;
    [ObservableProperty] private string error = "";

    public event Action<bool>? RequestClose;

    public EmulatorEditViewModel(Emulator emulator, bool isNew)
    {
        Model = emulator;
        IsNew = isNew;
        PlatformsText = string.Join(", ", emulator.Platforms);
        MakeDefaultForPlatforms = emulator.DefaultPlatforms.Count > 0;
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
    private void BrowseCore()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择模拟器核心文件",
            Filter = "核心文件|*.dll;*.so;*.dylib|所有文件|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        Model.CorePath = ToRelative(dialog.FileName);
    }

    [RelayCommand]
    private void BrowseInstallDir()
    {
        var dialog = new OpenFolderDialog { Title = "选择模拟器安装目录" };
        if (dialog.ShowDialog() == true) Model.InstallDir = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseConfig()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择模拟器配置文件（目录可直接手工填写）",
            Filter = "所有文件|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        Model.ConfigPath = ToRelative(dialog.FileName);
    }

    private string ToRelative(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(Model.InstallDir)) return absolutePath;
        try
        {
            var relative = Path.GetRelativePath(Model.InstallDir, absolutePath);
            return relative.StartsWith("..", StringComparison.Ordinal) ? absolutePath : relative;
        }
        catch
        {
            return absolutePath;
        }
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

        if (string.IsNullOrWhiteSpace(Model.ExePath))
        {
            Error = "可执行文件路径不能为空（也可先保存，稍后安装后自动填入）。";
        }

        var platforms = PlatformsText
            .Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Model.Platforms = platforms;
        Model.DefaultPlatforms = MakeDefaultForPlatforms
            ? new HashSet<string>(platforms, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(Model.DefaultPlatforms.Where(platforms.Contains), StringComparer.OrdinalIgnoreCase);

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
}
