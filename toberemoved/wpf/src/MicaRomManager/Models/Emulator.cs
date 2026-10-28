using CommunityToolkit.Mvvm.ComponentModel;

namespace MicaRomManager.Models;

public partial class Emulator : ObservableObject
{
    public long Id { get; set; }

    [ObservableProperty] private string name = "";
    [ObservableProperty] private string exePath = "";
    [ObservableProperty] private string argsTemplate = "\"{rom}\"";
    [ObservableProperty] private string corePath = "";
    [ObservableProperty] private string supportedExt = "";
    [ObservableProperty] private string version = "";
    [ObservableProperty] private string installDir = "";
    [ObservableProperty] private string configPath = "";
    [ObservableProperty] private string githubRepo = "";
    /// <summary>官网或发布页地址，用于「版本更新」跳转。</summary>
    [ObservableProperty] private string websiteUrl = "";
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private bool enabled = true;

    public List<string> Platforms { get; set; } = new();
    public HashSet<string> DefaultPlatforms { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string PlatformDisplay => Platforms.Count == 0 ? "" : string.Join(", ", Platforms);

    public string DefaultPlatformDisplay => DefaultPlatforms.Count == 0
        ? ""
        : string.Join(", ", Platforms.Where(DefaultPlatforms.Contains));

    public string EnabledDisplay => Enabled ? "启用" : "停用";

    public string ResolvedExeDisplay => Services.EmulatorPaths.ResolveExe(this) ?? ExePath;

    public void RefreshPlatformDisplay()
    {
        OnPropertyChanged(nameof(PlatformDisplay));
        OnPropertyChanged(nameof(DefaultPlatformDisplay));
    }
}
