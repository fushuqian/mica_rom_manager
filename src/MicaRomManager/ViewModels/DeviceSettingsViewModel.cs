using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaRomManager.Models;
using MicaRomManager.Services;
using MicaRomManager.Services.Devices;
using MicaRomManager.Views;

namespace MicaRomManager.ViewModels;

public sealed class PlatformFolderRow
{
    public string Platform { get; set; } = "";
    public string Folder { get; set; } = "";
}

public sealed record DriveChoice(string Root, string Display);

public partial class DeviceSettingsViewModel : ObservableObject
{
    public ObservableCollection<DeviceProfile> Profiles { get; } = new();
    public ObservableCollection<PlatformFolderRow> PlatformFolders { get; } = new();
    public ObservableCollection<DriveChoice> Drives { get; } = new();

    [ObservableProperty] private DeviceProfile? selectedProfile;
    [ObservableProperty] private PlatformFolderRow? selectedFolderRow;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private DriveChoice? selectedDrive;
    [ObservableProperty] private bool aiBusy;
    [ObservableProperty] private string aiStatus = "";

    public DeviceSettingsViewModel()
    {
        LoadDrives();
        Reload(null);
    }

    private void LoadDrives()
    {
        Drives.Clear();
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady) continue;

            string label;
            try
            {
                label = drive.VolumeLabel;
            }
            catch
            {
                label = "";
            }

            var display = $"{drive.Name}{(string.IsNullOrWhiteSpace(label) ? "" : " " + label)}";
            Drives.Add(new DriveChoice(drive.RootDirectory.FullName, display));
        }

        SelectedDrive = Drives.FirstOrDefault();
    }

    private void Reload(string? selectId)
    {
        var profiles = AppServices.DeviceProfiles.Load();
        Profiles.Clear();
        foreach (var profile in profiles) Profiles.Add(profile);

        SelectedProfile = Profiles.FirstOrDefault(p =>
                             string.Equals(p.Id, selectId, StringComparison.OrdinalIgnoreCase))
                         ?? Profiles.FirstOrDefault();
        StatusMessage = Profiles.Count == 0
            ? "还没有设备配置；可点「新增」创建，或把 JSON 放到 data\\devices 后点「刷新」。"
            : "";
    }

    partial void OnSelectedProfileChanged(DeviceProfile? value)
    {
        PlatformFolders.Clear();
        if (value == null) return;

        foreach (var pair in value.PlatformFolders)
            PlatformFolders.Add(new PlatformFolderRow { Platform = pair.Key, Folder = pair.Value });
    }

    [RelayCommand]
    private void Refresh() => Reload(SelectedProfile?.Id);

    [RelayCommand]
    private void NewProfile()
    {
        var profile = new DeviceProfile
        {
            Id = "",
            Name = "新设备",
            Brand = "",
            Model = "",
            Transport = "drive",
            RootFolder = "Roms",
            FolderTemplate = "{root}/{platform}",
            AllowUnmappedPlatforms = true,
            Notes = "",
        };

        Profiles.Add(profile);
        SelectedProfile = profile;
        StatusMessage = "已新增设备配置，填写名称与目录规则后点「保存」。";
    }

    [RelayCommand]
    private void SaveProfile()
    {
        var profile = SelectedProfile;
        if (profile == null) return;

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            StatusMessage = "设备名称不能为空。";
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.FolderTemplate))
        {
            StatusMessage = "目录模板不能为空，至少包含 {root}。";
            return;
        }

        if (!profile.FolderTemplate.Contains("{root}", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "目录模板必须包含 {root}，否则无法定位到盘内根目录。";
            return;
        }

        profile.PlatformFolders = BuildFolderMap();
        profile.Transport = string.IsNullOrWhiteSpace(profile.Transport) ? "drive" : profile.Transport;
        profile.RootFolder = profile.RootFolder.Trim();

        try
        {
            var id = AppServices.DeviceProfiles.Save(profile);
            StatusMessage = $"已保存「{profile.Name}」到 data\\devices\\{id}.json";
            Reload(profile.Id);
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        var profile = SelectedProfile;
        if (profile == null) return;

        if (profile.IsBuiltIn && !AppServices.DeviceProfiles.HasUserOverride(profile.Id))
        {
            StatusMessage = "内置配置不能直接删除；修改后保存会生成同名的自定义配置。";
            return;
        }

        var answer = MessageBox.Show($"删除设备配置「{profile.Name}」？\n\n只删除配置文件，不影响已发送的文件。",
            "删除设备配置", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        var removed = AppServices.DeviceProfiles.Delete(profile);
        StatusMessage = removed ? $"已删除「{profile.Name}」。" : "没有找到可删除的自定义配置文件。";
        Reload(null);
    }

    [RelayCommand]
    private async Task AiProfileAsync()
    {
        if (AiBusy) return;
        if (SelectedDrive == null)
        {
            AiStatus = "请先选择要识别的盘符。";
            return;
        }

        if (!AppServices.Ai.Options.IsConfigured)
        {
            AiStatus = "还没有配置 AI，请先到 设置 → AI 填写 BaseUrl、API Key 和模型。";
            return;
        }

        AiBusy = true;
        AiStatus = "正在扫描目录…";
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        try
        {
            var progress = new Progress<string>(message => AiStatus = message);
            var summary = await DriveTreeScanner.ScanAsync(SelectedDrive.Root, progress, cts.Token);
            AiStatus = $"扫描完成（{summary.Nodes.Count} 个目录），正在请求 AI 分析…";

            var result = await new AiDeviceProfiler().ProfileAsync(summary, cts.Token);
            if (!result.CanApply)
            {
                AiStatus = result.Error;
                var warnings = result.Warnings.Count == 0
                    ? ""
                    : "\n\n" + string.Join("\n", result.Warnings);
                MessageBox.Show(result.Error + warnings, "AI 识别设备结构",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var window = new AiDeviceProfileWindow(summary, result)
            {
                Owner = Application.Current?.Windows.OfType<SettingsWindow>().FirstOrDefault()
                        ?? Application.Current?.MainWindow,
            };
            if (window.ShowDialog() != true || window.Profile == null)
            {
                AiStatus = "已取消。";
                return;
            }

            ApplyAiProfile(window.Profile);
            AiStatus = "已填充建议配置，请核对后点「保存」。";
        }
        catch (OperationCanceledException)
        {
            AiStatus = "已取消。";
        }
        catch (Exception ex)
        {
            AiStatus = $"识别失败：{ex.Message}";
        }
        finally
        {
            AiBusy = false;
        }
    }

    /// <summary>用建议值替换当前表单对象，触发绑定刷新；仍由用户点「保存」落盘。</summary>
    private void ApplyAiProfile(DeviceProfile source)
    {
        var existing = SelectedProfile;
        var profile = new DeviceProfile
        {
            Id = existing?.Id ?? "",
            Name = source.Name,
            Brand = source.Brand,
            Model = source.Model,
            Transport = "drive",
            RootFolder = source.RootFolder,
            FolderTemplate = source.FolderTemplate,
            AllowUnmappedPlatforms = source.AllowUnmappedPlatforms,
            PlatformFolders = new Dictionary<string, string>(source.PlatformFolders, StringComparer.OrdinalIgnoreCase),
            Notes = source.Notes,
            IsBuiltIn = existing?.IsBuiltIn ?? false,
        };

        if (existing == null)
        {
            Profiles.Add(profile);
        }
        else
        {
            Profiles[Profiles.IndexOf(existing)] = profile;
        }

        SelectedProfile = profile;
    }

    [RelayCommand]
    private void RemoveFolderRow()
    {
        if (SelectedFolderRow == null) return;
        PlatformFolders.Remove(SelectedFolderRow);
        SelectedFolderRow = null;
    }

    private Dictionary<string, string> BuildFolderMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in PlatformFolders)
        {
            var platform = row.Platform?.Trim() ?? "";
            var folder = row.Folder?.Trim() ?? "";
            if (platform.Length == 0) continue;
            map[platform] = folder;
        }

        return map;
    }
}
