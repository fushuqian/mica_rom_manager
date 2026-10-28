using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaRomManager.Models;
using MicaRomManager.Services;
using MicaRomManager.Views;
using Microsoft.Win32;

namespace MicaRomManager.ViewModels;

public partial class EmulatorManagerViewModel : ObservableObject
{
    public ObservableCollection<Emulator> Emulators { get; } = new();

    [ObservableProperty] private Emulator? selectedEmulator;
    [ObservableProperty] private string statusMessage = "选择模拟器后可导入、调试或更新。";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private double downloadProgress;

    public EmulatorManagerViewModel()
    {
        Reload();
    }

    public void Reload()
    {
        var previousId = SelectedEmulator?.Id ?? 0;

        Emulators.Clear();
        foreach (var emulator in AppServices.EmulatorRepo.ListAll()) Emulators.Add(emulator);

        SelectedEmulator = Emulators.FirstOrDefault(e => e.Id == previousId) ?? Emulators.FirstOrDefault();
    }

    [RelayCommand]
    private void AddEmulator()
    {
        var name = "新模拟器";
        var index = 2;
        while (Emulators.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"新模拟器 {index++}";
        }

        var emulator = new Emulator { Name = name, Enabled = true };
        try
        {
            AppServices.EmulatorRepo.Save(emulator);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"新增失败：{ex.Message}", "新增模拟器", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Reload();
        SelectedEmulator = Emulators.FirstOrDefault(e => e.Id == emulator.Id);
        StatusMessage = $"已新增「{name}」，可用「编辑配置」改名称/官网，或直接导入程序文件。";
    }

    [RelayCommand]
    private void InstallFromFolder()
    {
        var emulator = SelectedEmulator;
        if (emulator == null)
        {
            MessageBox.Show("请先在左侧选择或新增一个模拟器。", "导入模拟器", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFolderDialog { Title = $"选择 {emulator.Name} 的程序文件夹" };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var result = AppServices.Installer.InstallFromFolder(dialog.FolderName, emulator.Name);
            FinishInstall(emulator, result);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入失败：{ex.Message}", "导入模拟器", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void InstallFromArchive()
    {
        var emulator = SelectedEmulator;
        if (emulator == null)
        {
            MessageBox.Show("请先在左侧选择或新增一个模拟器。", "导入模拟器", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"选择 {emulator.Name} 的压缩包",
            Filter = "压缩包|*.zip;*.7z;*.rar;*.tar.gz;*.tgz|所有文件|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var result = AppServices.Installer.InstallFromArchive(dialog.FileName, emulator.Name);
            FinishInstall(emulator, result);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导入失败：{ex.Message}", "导入模拟器", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FinishInstall(Emulator emulator, EmulatorInstaller.InstallResult result)
    {
        emulator.InstallDir = PortablePath.ToStored(result.InstallDir);
        if (result.ExeCandidates.Count == 0)
        {
            MessageBox.Show("已复制文件，但没有找到 .exe。请用「编辑配置」手动指定可执行文件。", "导入模拟器",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            var chosen = result.ExeCandidates.Count == 1
                ? result.ExeCandidates[0]
                : new ExePickWindow(result.ExeCandidates, result.InstallDir, emulator.Name).Pick();
            if (!string.IsNullOrWhiteSpace(chosen))
            {
                emulator.ExePath = Path.GetRelativePath(result.InstallDir, chosen);
            }
        }

        AppServices.EmulatorRepo.Save(emulator);
        Reload();
        StatusMessage = $"「{emulator.Name}」已导入到 {result.InstallDir}";
    }

    [RelayCommand]
    private void EditSelected()
    {
        var emulator = SelectedEmulator;
        if (emulator == null) return;

        var working = new Emulator
        {
            Id = emulator.Id,
            Name = emulator.Name,
            ExePath = emulator.ExePath,
            Version = emulator.Version,
            InstallDir = emulator.InstallDir,
            WebsiteUrl = emulator.WebsiteUrl,
            Notes = emulator.Notes,
            Enabled = emulator.Enabled,
        };

        var window = new EmulatorEditWindow(working, false) { Owner = Application.Current?.MainWindow };
        if (window.ShowDialog() != true) return;

        Reload();
        SelectedEmulator = Emulators.FirstOrDefault(e => e.Id == emulator.Id);
        StatusMessage = $"「{emulator.Name}」配置已保存";
    }

    [RelayCommand]
    private void OpenWebsite()
    {
        var emulator = SelectedEmulator;
        if (emulator == null) return;
        if (string.IsNullOrWhiteSpace(emulator.WebsiteUrl))
        {
            MessageBox.Show("该模拟器还没有配置官网地址，请先在「编辑配置」里填写。", "版本更新",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(emulator.WebsiteUrl) { UseShellExecute = true });
            StatusMessage = $"已打开「{emulator.Name}」的官网 / 发布页";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开网页失败：{ex.Message}", "版本更新", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void TestLaunch()
    {
        var emulator = SelectedEmulator;
        if (emulator == null) return;

        var exePath = EmulatorPaths.ResolveExe(emulator);
        if (string.IsNullOrWhiteSpace(exePath))
        {
            MessageBox.Show("可执行文件不存在，请先导入或编辑配置。", "运行调试",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exePath)
            {
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
                UseShellExecute = true,
            });
            StatusMessage = $"已启动 {Path.GetFileName(exePath)}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动失败：{ex.Message}", "运行调试", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void OpenInstallDir()
    {
        var directory = SelectedEmulator?.InstallDir;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{directory}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        var emulator = SelectedEmulator;
        if (emulator == null) return;

        var answer = MessageBox.Show($"从模拟器管理移除「{emulator.Name}」的配置？\n\n不会删除已安装的程序文件。",
            "删除模拟器", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        AppServices.EmulatorRepo.Delete(emulator.Id);

        if (!string.IsNullOrWhiteSpace(emulator.InstallDir) &&
            Directory.Exists(emulator.InstallDir) &&
            Path.GetFullPath(emulator.InstallDir).StartsWith(
                Path.GetFullPath(AppServices.Paths.EmulatorsDir), StringComparison.OrdinalIgnoreCase))
        {
            var recycle = MessageBox.Show($"是否把安装目录移到回收站？\n{emulator.InstallDir}",
                "删除模拟器", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (recycle == MessageBoxResult.Yes)
            {
                FileRecycleBin.MoveToRecycleBin(emulator.InstallDir);
            }
        }

        Reload();
        StatusMessage = $"已移除「{emulator.Name}」";
    }

    [RelayCommand]
    private void RefreshList()
    {
        Reload();
        StatusMessage = "已刷新模拟器列表";
    }
}
