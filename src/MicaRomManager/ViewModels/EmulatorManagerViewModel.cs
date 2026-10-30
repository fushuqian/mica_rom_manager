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
    [ObservableProperty] private bool isDownloading;
    [ObservableProperty] private double downloadProgress;

    private CancellationTokenSource? _updateCts;

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
        var existingNames = Emulators.Select(e => e.Name).ToList();
        var templates = EmulatorTemplateStore.Load(AppServices.Paths);
        var emulator = EmulatorTemplateFactory.CreateBlank(existingNames);
        var fromTemplate = false;

        if (templates.Count > 0)
        {
            var window = new EmulatorTemplateWindow(templates) { Owner = Application.Current?.MainWindow };
            if (window.ShowDialog() != true) return;

            if (window.SelectedTemplate != null)
            {
                emulator = EmulatorTemplateFactory.CreateFromTemplate(
                    window.SelectedTemplate,
                    existingNames,
                    AppServices.Platforms.ListAll().Select(platform => platform.Name).ToList());
                fromTemplate = true;
            }
        }

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
        StatusMessage = fromTemplate
            ? $"已按模板新增「{emulator.Name}」，平台绑定按同名匹配；改名过的平台请到「设置 → 平台」手动绑定。"
            : $"已新增「{emulator.Name}」，可用「编辑配置」改名称/官网，或直接导入程序文件。";
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
            MessageBox.Show("该模拟器还没有配置官网地址，请先在「编辑配置」里填写。", "打开官网",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (OpenUrl(emulator.WebsiteUrl)) StatusMessage = $"已打开「{emulator.Name}」的官网 / 发布页";
    }

    /// <summary>
    /// 用 GitHub Release 检查更新：确认后下载 Windows 资产并覆盖安装，版本号写回。
    /// 下载可取消；安装失败时安装器会自动回滚到旧目录。
    /// </summary>
    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        var emulator = SelectedEmulator;
        if (emulator == null)
        {
            MessageBox.Show("请先在左侧选择模拟器。", "检查更新",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (IsBusy) return;

        var repo = EmulatorUpdater.NormalizeRepository(
            string.IsNullOrWhiteSpace(emulator.GithubRepo) ? emulator.WebsiteUrl : emulator.GithubRepo);
        if (repo.Length == 0)
        {
            MessageBox.Show(
                "该模拟器还没有配置 GitHub 仓库。\n\n"
                + "请在「编辑配置」里填写 owner/repo（例如 libretro/RetroArch），"
                + "或点「打开官网」手动下载更新。",
                "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsBusy = true;
        DownloadProgress = 0;
        StatusMessage = $"正在检查「{emulator.Name}」的更新…";

        UpdateCheckResult result;
        try
        {
            result = await AppServices.Updater.CheckAsync(repo, emulator.Version);
        }
        finally
        {
            IsBusy = false;
        }

        StatusMessage = result.Message;
        if (!result.Success)
        {
            MessageBox.Show(result.Message, "检查更新", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var latestVersion = EmulatorUpdater.ResolveVersion(result.LatestTag, result.AssetName);
        if (!EmulatorUpdater.IsNewer(latestVersion, emulator.Version))
        {
            MessageBox.Show(result.Message, "检查更新", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(result.AssetUrl))
        {
            var open = MessageBox.Show(
                result.Message + "\n\n没有找到可自动安装的 Windows 资产，是否打开 Release 页面手动下载？",
                "检查更新", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (open == MessageBoxResult.Yes) OpenUrl(result.ReleaseUrl ?? emulator.WebsiteUrl);
            return;
        }

        var targetDir = Path.Combine(AppServices.Paths.EmulatorsDir, RomImporter.SanitizeFolderName(emulator.Name));
        var confirm = MessageBox.Show(
            $"{result.Message}\n\n"
            + $"下载资产：{result.AssetName}\n"
            + $"安装到：{targetDir}\n"
            + "旧版本会整体移到 data\\emulators\\.backup。\n\n现在下载并安装？",
            "检查更新", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        await DownloadAndInstallAsync(emulator, result, latestVersion);
    }

    private async Task DownloadAndInstallAsync(Emulator emulator, UpdateCheckResult result, string latestVersion)
    {
        var assetName = string.IsNullOrWhiteSpace(result.AssetName)
            ? $"{emulator.Name}-{latestVersion}"
            : result.AssetName;
        var fileName = RomImporter.SanitizeFolderName(assetName);
        if (Path.GetExtension(fileName).Length == 0) fileName += ".zip";

        var downloadPath = Path.Combine(AppServices.Paths.DownloadsDir, fileName);
        _updateCts = new CancellationTokenSource();
        IsDownloading = true;
        IsBusy = true;
        DownloadProgress = 0;
        StatusMessage = $"正在下载 {assetName}…";

        var progress = new Progress<double>(value => DownloadProgress = value < 0 ? 0 : value);

        try
        {
            var (downloaded, message, _) = await AppServices.Updater.DownloadAsync(
                result.AssetUrl!, downloadPath, progress, _updateCts.Token);
            if (!downloaded)
            {
                if (_updateCts?.IsCancellationRequested == true)
                {
                    StatusMessage = "已取消下载";
                    return;
                }

                StatusMessage = message;
                MessageBox.Show(message, "下载更新", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            IsDownloading = false;
            DownloadProgress = 100;
            StatusMessage = "下载完成，正在安装…";

            EmulatorInstaller.InstallResult install;
            try
            {
                install = await Task.Run(() => AppServices.Installer.InstallFromArchive(downloadPath, emulator.Name));
            }
            catch (Exception ex)
            {
                StatusMessage = $"安装失败：{ex.Message}";
                MessageBox.Show(
                    $"下载完成，但安装失败：{ex.Message}\n\n安装包保留在：{downloadPath}",
                    "安装更新", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (latestVersion.Length > 0) emulator.Version = latestVersion;
            if (string.IsNullOrWhiteSpace(emulator.WebsiteUrl) && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
                emulator.WebsiteUrl = result.ReleaseUrl!;

            FinishInstall(emulator, install);
            var updated = Emulators.FirstOrDefault(e => e.Id == emulator.Id);
            if (updated != null && EmulatorPaths.ResolveExe(updated) == null)
            {
                MessageBox.Show(
                    "安装完成，但没有自动找到可执行文件。请用「编辑配置」指定主程序。",
                    "安装更新", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            TryDeleteFile(downloadPath);
            StatusMessage = $"「{emulator.Name}」已更新到 {emulator.Version}";
            MessageBox.Show(StatusMessage, "更新完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已取消下载";
        }
        catch (Exception ex)
        {
            StatusMessage = $"更新失败：{ex.Message}";
            MessageBox.Show(StatusMessage, "更新失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsDownloading = false;
            IsBusy = false;
            DownloadProgress = 0;
            _updateCts?.Dispose();
            _updateCts = null;
        }
    }

    [RelayCommand]
    private void CancelUpdate()
    {
        if (!IsDownloading) return;
        StatusMessage = "正在取消下载…";
        _updateCts?.Cancel();
    }

    private bool OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开网页失败：{ex.Message}", "打开官网", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 清理下载包失败不影响更新结果
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
