using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaRomManager.Models;
using MicaRomManager.Services;
using MicaRomManager.Services.Sending;

namespace MicaRomManager.ViewModels;

public partial class ConnectDeviceViewModel : ObservableObject
{
    public ObservableCollection<DeviceProfile> Profiles { get; } = new();
    public ObservableCollection<DriveEntry> Drives { get; } = new();

    [ObservableProperty] private DeviceProfile? selectedProfile;
    [ObservableProperty] private DriveEntry? selectedDrive;
    [ObservableProperty] private bool overwriteSameName;
    [ObservableProperty] private string targetPreview = "";
    [ObservableProperty] private string statusText = "";

    public DeviceConnection? Connection { get; private set; }

    public event Action<bool>? RequestClose;

    public ConnectDeviceViewModel()
    {
        foreach (var profile in AppServices.DeviceProfiles.Load().Where(p => p.IsDrive))
            Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault();

        RefreshDrives();
    }

    partial void OnSelectedProfileChanged(DeviceProfile? value) => UpdatePreview();
    partial void OnSelectedDriveChanged(DriveEntry? value) => UpdatePreview();

    private void UpdatePreview()
    {
        if (SelectedProfile == null || SelectedDrive == null)
        {
            TargetPreview = "请选择设备配置与目标盘符。";
            return;
        }

        var rootFolder = SharePackager.SanitizeName(SelectedProfile.RootFolder);
        var root = string.IsNullOrWhiteSpace(rootFolder)
            ? SelectedDrive.RootPath
            : Path.Combine(SelectedDrive.RootPath, rootFolder);
        TargetPreview = $"发送到：{root}（目录规则：{SelectedProfile.FolderTemplate}）";
    }

    [RelayCommand]
    private void RefreshDrives()
    {
        var previous = SelectedDrive?.RootPath;
        Drives.Clear();
        foreach (var drive in DriveCatalog.List()) Drives.Add(drive);

        SelectedDrive = Drives.FirstOrDefault(d => string.Equals(d.RootPath, previous, StringComparison.OrdinalIgnoreCase))
                        ?? Drives.FirstOrDefault(d => d.Removable)
                        ?? Drives.FirstOrDefault();
        UpdatePreview();
    }

    [RelayCommand]
    private void Connect()
    {
        if (SelectedProfile == null)
        {
            StatusText = "没有可用的盘符设备配置，请先到设置 →「设备」新增。";
            return;
        }

        if (SelectedDrive == null)
        {
            StatusText = "请选择目标盘符。";
            return;
        }

        if (!Directory.Exists(SelectedDrive.RootPath))
        {
            StatusText = $"盘符不可用：{SelectedDrive.RootPath}";
            return;
        }

        var rootFolder = SharePackager.SanitizeName(SelectedProfile.RootFolder);
        Connection = new DeviceConnection
        {
            Profile = SelectedProfile,
            Drive = SelectedDrive,
            OverwriteSameName = OverwriteSameName,
            TargetRoot = string.IsNullOrWhiteSpace(rootFolder)
                ? SelectedDrive.RootPath
                : Path.Combine(SelectedDrive.RootPath, rootFolder),
        };

        RequestClose?.Invoke(true);
    }
}
