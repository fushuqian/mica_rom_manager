using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaRomManager.Models;
using MicaRomManager.Services;
using MicaRomManager.Services.Devices;
using MicaRomManager.Services.Sending;

namespace MicaRomManager.ViewModels;

public partial class DeviceSendRow : ObservableObject
{
    public string Title { get; init; } = "";
    public string Platform { get; init; } = "";
    public string FileName { get; init; } = "";
    public string SizeText { get; init; } = "";
    public string? FilePath { get; init; }
    public Game? Game { get; init; }
    public bool Exists { get; init; }

    [ObservableProperty] private string note = "";
    [ObservableProperty] private bool supported = true;

    public bool Sendable => Exists && Supported && !string.IsNullOrWhiteSpace(FilePath);
}

public partial class DeviceSendViewModel : ObservableObject
{
    private readonly List<Game> _games;

    public ObservableCollection<DeviceProfile> Profiles { get; } = new();
    public ObservableCollection<DriveEntry> Drives { get; } = new();
    public ObservableCollection<DeviceSendRow> Items { get; } = new();

    [ObservableProperty] private DeviceProfile? selectedProfile;
    [ObservableProperty] private DriveEntry? selectedDrive;
    [ObservableProperty] private bool overwriteSameName;
    [ObservableProperty] private string estimateText = "";
    [ObservableProperty] private string spaceText = "";
    [ObservableProperty] private string targetPreview = "";
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private double progressValue;
    [ObservableProperty] private double progressMaximum = 1;

    public bool Sent { get; private set; }
    public string LastTargetRoot { get; private set; } = "";

    public bool CanStart => !IsBusy;

    public DeviceSendViewModel(IReadOnlyList<Game> games)
    {
        _games = games.ToList();

        foreach (var profile in AppServices.DeviceProfiles.Load().Where(p => p.IsDrive))
            Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault();

        RefreshDrives();

        BuildRows();
        ApplyProfileSupport();
        UpdateEstimate();
        UpdatePreview();
        UpdateSpace();
    }

    private void BuildRows()
    {
        foreach (var game in _games)
        {
            var discs = game.Discs.Count > 0 ? game.Discs : new[] { game.RomPath };
            foreach (var path in discs.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                var exists = File.Exists(path);
                Items.Add(new DeviceSendRow
                {
                    Title = game.Title,
                    Platform = game.Platform,
                    FileName = Path.GetFileName(path),
                    SizeText = exists ? Game.FormatSize(new FileInfo(path).Length) : "",
                    FilePath = path,
                    Game = game,
                    Exists = exists,
                    Note = exists ? "" : "文件缺失",
                });
            }
        }
    }

    private void ApplyProfileSupport()
    {
        foreach (var row in Items)
        {
            var supported = SelectedProfile == null
                            || SelectedProfile.AllowUnmappedPlatforms
                            || SendPathBuilder.IsPlatformMapped(SelectedProfile, row.Platform);
            row.Supported = supported;

            if (!row.Exists) row.Note = "文件缺失";
            else if (!supported) row.Note = "设备不支持该平台";
            else row.Note = "";
        }
    }

    private List<DeviceSendRow> SendableRows() => Items.Where(row => row.Sendable).ToList();

    private void UpdateEstimate()
    {
        var rows = SendableRows();
        var bytes = rows.Sum(row => SafeSize(row.FilePath!));
        var sizeText = bytes > 0 ? Game.FormatSize(bytes) : "0 B";
        var gameCount = rows.Select(row => row.Game).Where(g => g != null).Distinct().Count();
        EstimateText = $"准备发送 {gameCount} 个游戏 / {rows.Count} 个文件 · 共 {sizeText}"
                       + (Items.Count == rows.Count ? "" : $"（已排除 {Items.Count - rows.Count} 个不可发送文件）");
    }

    private static long SafeSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private void UpdateSpace()
    {
        if (SelectedDrive == null)
        {
            SpaceText = "请选择目标盘符。";
            return;
        }

        var needed = SendableRows().Sum(row => SafeSize(row.FilePath!));
        var enough = SelectedDrive.FreeBytes >= needed;
        var neededText = needed > 0 ? Game.FormatSize(needed) : "0 B";
        SpaceText = $"可用 {Game.FormatSize(SelectedDrive.FreeBytes)}，本次需要 {neededText}"
                    + (enough ? "" : "（空间不足）");
    }

    private void UpdatePreview()
    {
        if (SelectedProfile == null || SelectedDrive == null)
        {
            TargetPreview = "";
            return;
        }

        var sample = Items.FirstOrDefault(row => row.Sendable && row.Game != null);
        TargetPreview = sample?.Game == null
            ? Path.Combine(SelectedDrive.RootPath, SelectedProfile.RootFolder)
            : SendPathBuilder.ResolveFolder(SelectedProfile, SelectedDrive.RootPath, sample.Game) + "（示例）";
    }

    private string ResolveTargetRoot()
    {
        if (SelectedProfile == null || SelectedDrive == null) return "";
        var folder = SharePackager.SanitizeName(SelectedProfile.RootFolder);
        return string.IsNullOrWhiteSpace(folder)
            ? SelectedDrive.RootPath
            : Path.Combine(SelectedDrive.RootPath, folder);
    }

    partial void OnSelectedProfileChanged(DeviceProfile? value)
    {
        ApplyProfileSupport();
        UpdateEstimate();
        UpdatePreview();
        UpdateSpace();
    }

    partial void OnSelectedDriveChanged(DriveEntry? value)
    {
        UpdateSpace();
        UpdatePreview();
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanStart));

    [RelayCommand]
    private void RefreshDrives()
    {
        var previous = SelectedDrive?.RootPath;
        Drives.Clear();
        foreach (var drive in DriveCatalog.List()) Drives.Add(drive);

        SelectedDrive = Drives.FirstOrDefault(d => string.Equals(d.RootPath, previous, StringComparison.OrdinalIgnoreCase))
                        ?? Drives.FirstOrDefault(d => d.Removable)
                        ?? Drives.FirstOrDefault();
        UpdateSpace();
        UpdatePreview();
    }

    [RelayCommand]
    private async Task StartSendAsync()
    {
        if (IsBusy) return;

        if (SelectedProfile == null)
        {
            MessageBox.Show("没有可用的设备配置。请到设置 →「设备」新增一个盘符设备配置。",
                "发送到设备", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SelectedDrive == null)
        {
            MessageBox.Show("请选择目标盘符。", "发送到设备", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var rows = SendableRows();
        if (rows.Count == 0)
        {
            MessageBox.Show("没有可发送的文件（ROM 缺失或平台不受该设备支持）。", "发送到设备",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var needed = rows.Sum(row => SafeSize(row.FilePath!));
        if (SelectedDrive.FreeBytes < needed)
        {
            MessageBox.Show($"目标盘空间不足：需要 {Game.FormatSize(needed)}，可用 {Game.FormatSize(SelectedDrive.FreeBytes)}。",
                "发送到设备", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var files = rows.Select(row => new SendFile(
            row.FilePath!, row.Platform, row.Title, row.Game?.ReleaseYear)).ToList();

        IsBusy = true;
        ProgressMaximum = files.Count;
        ProgressValue = 0;
        StatusText = "正在复制文件…";

        var policy = OverwriteSameName ? ConflictPolicy.Overwrite : ConflictPolicy.Skip;
        var progress = new Progress<SendProgress>(value =>
        {
            ProgressValue = value.Index;
            StatusText = $"正在复制 {value.Index}/{value.Total}：{value.FileName}";
        });

        try
        {
            var result = await Task.Run(() => new DriveSendTarget().Send(
                files, SelectedProfile, SelectedDrive.RootPath, policy, progress));

            Sent = true;
            LastTargetRoot = Path.GetFullPath(ResolveTargetRoot());
            StatusText = $"发送完成：复制 {result.Copied}，跳过 {result.Skipped}，失败 {result.Failed}";

            var message = $"已发送到：{LastTargetRoot}\n\n复制 {result.Copied} 个，跳过 {result.Skipped} 个，失败 {result.Failed} 个"
                          + (result.Warnings.Count > 0
                              ? $"\n\n提示（前 8 条）：\n" + string.Join("\n", result.Warnings.Take(8))
                              : "");
            MessageBox.Show(message, "发送到设备", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusText = $"发送失败：{ex.Message}";
            MessageBox.Show(StatusText, "发送到设备", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenTarget()
    {
        var path = LastTargetRoot;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
