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
    public ObservableCollection<EmulatorTemplate> Templates { get; } = new();

    [ObservableProperty] private EmulatorTemplate? selectedTemplate;
    [ObservableProperty] private Emulator? selectedEmulator;
    [ObservableProperty] private string statusMessage = "选择模拟器后可安装、配置或检查更新。";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private double downloadProgress;

    public EmulatorManagerViewModel()
    {
        Reload();
    }

    public void Reload()
    {
        var selectedId = SelectedEmulator?.Id;
        var previousId = selectedId ?? 0;

        foreach (var template in EmulatorTemplateStore.Load(AppServices.Paths))
        {
            if (Templates.All(t => !string.Equals(t.Name, template.Name, StringComparison.OrdinalIgnoreCase)))
            {
                Templates.Add(template);
            }
        }

        Emulators.Clear();
        foreach (var emulator in AppServices.EmulatorRepo.ListAll()) Emulators.Add(emulator);

        SelectedEmulator = Emulators.FirstOrDefault(e => e.Id == previousId) ?? Emulators.FirstOrDefault();
        SelectedTemplate ??= Templates.FirstOrDefault();
    }

    [RelayCommand]
    private void AddFromTemplate()
    {
        if (SelectedTemplate == null)
        {
            MessageBox.Show("请先在下拉框里选择一个模拟器模板。", "新增模拟器",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var template = SelectedTemplate;
        var name = template.Name;
        var index = 2;
        while (Emulators.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = $"{template.Name} {index++}";
        }

        var emulator = new Emulator
        {
            Name = name,
            ArgsTemplate = template.ArgsTemplate,
            CorePath = template.CorePath,
            SupportedExt = string.Join(",", template.Extensions),
            GithubRepo = template.GithubRepo,
            Notes = template.Notes,
            Platforms = template.Platforms.ToList(),
        };

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
        StatusMessage = $"已新增「{name}」，请点「安装（文件夹/zip）」导入程序文件。";
    }

    [RelayCommand]
    private void InstallFromFolder()
    {
        var emulator = SelectedEmulator;
        if (emulator == null)
        {
            MessageBox.Show("请先选择或新增一个模拟器。", "安装模拟器", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show($"安装失败：{ex.Message}", "安装模拟器", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void InstallFromZip()
    {
        var emulator = SelectedEmulator;
        if (emulator == null)
        {
            MessageBox.Show("请先选择或新增一个模拟器。", "安装模拟器", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"选择 {emulator.Name} 的压缩包",
            Filter = "压缩包|*.zip|所有文件|*.*",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var result = AppServices.Installer.InstallFromZip(dialog.FileName, emulator.Name);
            FinishInstall(emulator, result);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"安装失败：{ex.Message}", "安装模拟器", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void FinishInstall(Emulator emulator, EmulatorInstaller.InstallResult result)
    {
        emulator.InstallDir = LocalPath.ToStored(result.InstallDir);
        if (result.ExeCandidates.Count == 0)
        {
            MessageBox.Show("已复制文件，但没有找到 .exe。请手动编辑可执行文件路径。", "安装模拟器",
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
        StatusMessage = $"「{emulator.Name}」已安装到 {result.InstallDir}";
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
            ArgsTemplate = emulator.ArgsTemplate,
            CorePath = emulator.CorePath,
            SupportedExt = emulator.SupportedExt,
            Version = emulator.Version,
            InstallDir = emulator.InstallDir,
            ConfigPath = emulator.ConfigPath,
            GithubRepo = emulator.GithubRepo,
            Notes = emulator.Notes,
            Enabled = emulator.Enabled,
            Platforms = emulator.Platforms.ToList(),
            DefaultPlatforms = new HashSet<string>(emulator.DefaultPlatforms, StringComparer.OrdinalIgnoreCase),
        };

        var window = new EmulatorEditWindow(working, false) { Owner = Application.Current?.MainWindow };
        if (window.ShowDialog() != true) return;

        Reload();
        SelectedEmulator = Emulators.FirstOrDefault(e => e.Id == emulator.Id);
        StatusMessage = $"「{emulator.Name}」配置已保存";
    }

    [RelayCommand]
    private void SetDefault()
    {
        var emulator = SelectedEmulator;
        if (emulator == null) return;
        if (emulator.Platforms.Count == 0)
        {
            MessageBox.Show("该模拟器还没有绑定平台，请先在「编辑配置」里填写平台。", "设默认模拟器",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var platforms = string.Join(", ", emulator.Platforms);
        var answer = MessageBox.Show($"把「{emulator.Name}」设为以下平台的默认模拟器？\n\n{platforms}",
            "设默认模拟器", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        AppServices.EmulatorRepo.SetAsDefaultForBoundPlatforms(emulator.Id);
        Reload();
        StatusMessage = $"「{emulator.Name}」已设为默认模拟器";
    }

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        var emulator = SelectedEmulator;
        if (emulator == null) return;
        if (string.IsNullOrWhiteSpace(emulator.GithubRepo))
        {
            MessageBox.Show("该模拟器没有配置 GitHub 仓库（owner/repo），无法在线检查更新。",
                "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (IsBusy) return;

        IsBusy = true;
        try
        {
            StatusMessage = "正在请求 GitHub…";
            var result = await AppServices.Updater.CheckAsync(emulator.GithubRepo, emulator.Version);
            StatusMessage = result.Message;

            if (!result.Success)
            {
                MessageBox.Show(result.Message, "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (IsInstallableAsset(result.AssetName) && !string.IsNullOrWhiteSpace(result.AssetUrl))
            {
                var answer = MessageBox.Show(
                    $"{result.Message}\n\n是否下载「{result.AssetName}」并安装到本程序？",
                    "检查更新", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Yes)
                {
                    await DownloadAndInstallAsync(emulator, result);
                }

                return;
            }

            var target = result.ReleaseUrl ?? result.AssetUrl;
            var open = MessageBox.Show(
                $"{result.Message}\n\n没有可自动安装的 zip / 7z 资产。{ (target != null ? "\n\n是否打开 Release 页面手动下载？" : "") }",
                "检查更新", target != null ? MessageBoxButton.YesNo : MessageBoxButton.OK,
                MessageBoxImage.Information);
            if (open == MessageBoxResult.Yes && !string.IsNullOrWhiteSpace(target))
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
        }
        finally
        {
            IsBusy = false;
            DownloadProgress = 0;
        }
    }

    private static bool IsInstallableAsset(string? assetName) =>
        assetName?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true ||
        assetName?.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) == true;

    private async Task DownloadAndInstallAsync(Emulator emulator, UpdateCheckResult result)
    {
        var assetName = result.AssetName!;
        var extension = Path.GetExtension(assetName).ToLowerInvariant();
        var fileName = $"{RomImporter.SanitizeFolderName(emulator.GithubRepo.Replace('/', '-'))}-{result.LatestTag ?? "latest"}{extension}";
        var targetFile = Path.Combine(AppServices.Paths.DownloadsDir, fileName);

        DownloadProgress = 0;
        var progress = new Progress<double>(value =>
        {
            DownloadProgress = value < 0 ? 0 : value;
            StatusMessage = value < 0 ? "正在下载…" : $"下载中 {value:0}%";
        });

        var download = await AppServices.Updater.DownloadAsync(result.AssetUrl!, targetFile, progress, CancellationToken.None);
        StatusMessage = download.Message;
        if (!download.Success)
        {
            MessageBox.Show(download.Message, "下载安装包", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusMessage = "正在解压并安装…";
        try
        {
            var install = AppServices.Installer.InstallFromArchive(targetFile, emulator.Name);
            FinishInstall(emulator, install);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"安装失败：{ex.Message}", "下载安装", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var version = result.LatestTag?.TrimStart('v', 'V');
        if (!string.IsNullOrWhiteSpace(version))
        {
            emulator.Version = version;
            AppServices.EmulatorRepo.Save(emulator);
            Reload();
        }

        StatusMessage = $"「{emulator.Name}」已更新到 {version ?? "最新版本"}，安装包：{targetFile}";
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
    private void TestLaunch()
    {
        var emulator = SelectedEmulator;
        if (emulator == null) return;

        var exePath = EmulatorPaths.ResolveExe(emulator);
        if (string.IsNullOrWhiteSpace(exePath))
        {
            MessageBox.Show("可执行文件不存在，请先安装或编辑配置。", "测试启动",
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
            MessageBox.Show($"启动失败：{ex.Message}", "测试启动", MessageBoxButton.OK, MessageBoxImage.Error);
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
    private void RefreshList()
    {
        Reload();
        StatusMessage = "已刷新模拟器列表";
    }
}
