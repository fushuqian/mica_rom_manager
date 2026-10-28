using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaRomManager.Models;
using MicaRomManager.Services;
using Microsoft.Win32;

namespace MicaRomManager.ViewModels;

public sealed class SharePackRow
{
    public string Title { get; init; } = "";
    public string Platform { get; init; } = "";
    public string EmulatorName { get; init; } = "";
    public string Note { get; init; } = "";
    public Emulator? Emulator { get; init; }
    public Game? Game { get; init; }
    public string? RomPath { get; init; }
}

public partial class SharePackViewModel : ObservableObject
{
    private readonly List<Game> _games;
    private readonly SharePackager _packager = new();

    public ObservableCollection<SharePackRow> Items { get; } = new();

    [ObservableProperty] private string packageName = $"ROM分享包-{DateTime.Now:yyyyMMdd-HHmm}";
    [ObservableProperty] private string exportDir = AppServices.Paths.ExportsDir;
    [ObservableProperty] private bool includeEmulators = true;
    [ObservableProperty] private bool includeSaves;
    [ObservableProperty] private string saveDir = "";
    [ObservableProperty] private bool createZip;
    [ObservableProperty] private string estimateText = "";
    [ObservableProperty] private string warningText = "";
    [ObservableProperty] private string statusText = "选择目标位置后点击「开始导出」。";
    [ObservableProperty] private bool isBusy;

    public bool Exported { get; private set; }
    public string? LastPackageDir { get; private set; }

    public SharePackViewModel(IReadOnlyList<Game> games)
    {
        _games = games.ToList();
        BuildRows();
        UpdateEstimate();
    }

    private void BuildRows()
    {
        var all = AppServices.EmulatorRepo.ListAll();
        var warnings = new List<string>();

        foreach (var game in _games)
        {
            var emulator = EmulatorResolver.Resolve(game, all, AppServices.EmulatorRepo);
            var romExists = File.Exists(game.RomPath);
            if (emulator == null) warnings.Add($"「{game.Title}」未配置模拟器，无法生成启动器");
            if (!romExists) warnings.Add($"「{game.Title}」的 ROM 文件不存在");

            Items.Add(new SharePackRow
            {
                Title = game.Title,
                Platform = game.Platform,
                EmulatorName = emulator?.Name ?? "—",
                Note = romExists ? "" : "ROM 缺失",
                Emulator = emulator,
                Game = game,
                RomPath = game.RomPath,
            });
        }

        WarningText = string.Join("；", warnings);
    }

    private List<ShareItem> BuildItems()
    {
        return Items
            .Where(row => row.Emulator != null && row.Game != null && !string.IsNullOrWhiteSpace(row.RomPath))
            .Select(row => new ShareItem
            {
                Game = row.Game!,
                Emulator = row.Emulator!,
                RomPath = row.RomPath!,
            })
            .ToList();
    }

    private void UpdateEstimate()
    {
        var items = BuildItems();
        var size = _packager.EstimateSize(items, IncludeEmulators, IncludeSaves ? SaveDir : null);
        EstimateText = $"预计体积：{Game.FormatSize(size)}（{items.Count} 个游戏"
                       + (IncludeEmulators ? "，含模拟器" : "，不含模拟器")
                       + (IncludeSaves ? "，含存档" : "") + "）";
    }

    partial void OnIncludeEmulatorsChanged(bool value) => UpdateEstimate();
    partial void OnIncludeSavesChanged(bool value) => UpdateEstimate();
    partial void OnSaveDirChanged(string value) => UpdateEstimate();

    [RelayCommand]
    private void BrowseExportDir()
    {
        var dialog = new OpenFolderDialog { Title = "选择导出位置", InitialDirectory = ExportDir };
        if (dialog.ShowDialog() == true) ExportDir = dialog.FolderName;
    }

    [RelayCommand]
    private void BrowseSaveDir()
    {
        var dialog = new OpenFolderDialog { Title = "选择要打包的存档目录", InitialDirectory = SaveDir };
        if (dialog.ShowDialog() == true) SaveDir = dialog.FolderName;
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (IsBusy) return;
        if (string.IsNullOrWhiteSpace(ExportDir))
        {
            MessageBox.Show("请先选择导出位置。", "分享打包", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var items = BuildItems();
        if (items.Count == 0)
        {
            MessageBox.Show("没有可打包的游戏（需要已配置模拟器且 ROM 存在）。", "分享打包",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        StatusText = "正在复制文件，请稍候…";
        try
        {
            var result = await Task.Run(() => _packager.Export(
                ExportDir, PackageName, items, IncludeEmulators, IncludeSaves,
                string.IsNullOrWhiteSpace(SaveDir) ? null : SaveDir, CreateZip));

            Exported = true;
            LastPackageDir = result.PackageDir;
            StatusText = $"已导出到：{result.PackageDir}"
                         + (result.ZipPath != null ? $"\n压缩包：{result.ZipPath}" : "");

            var message = $"分享包已生成：\n{result.PackageDir}"
                          + (result.ZipPath != null ? $"\n\nZIP：{result.ZipPath}" : "")
                          + (result.Warnings.Count > 0
                              ? $"\n\n有 {result.Warnings.Count} 条提示：\n" + string.Join("\n", result.Warnings.Take(8))
                              : "");
            MessageBox.Show(message, "分享打包完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = $"导出失败：{ex.Message}";
            MessageBox.Show(StatusText, "分享打包失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenResult()
    {
        if (string.IsNullOrWhiteSpace(LastPackageDir) || !Directory.Exists(LastPackageDir)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{LastPackageDir}\"") { UseShellExecute = true });
    }
}
