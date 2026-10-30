namespace MicaRomManager.Models;

/// <summary>模拟器页左栏的一行：已安装的模拟器，或内置模板里尚未安装的条目。</summary>
public sealed class EmulatorCatalogItem
{
    public string Name { get; init; } = "";
    public string PlatformDisplay { get; init; } = "";
    public string Extensions { get; init; } = "";
    public string GithubRepo { get; init; } = "";
    public string WebsiteUrl { get; init; } = "";
    public string Notes { get; init; } = "";
    public bool IsInstalled { get; init; }

    /// <summary>已安装时对应的实体，未安装为 null。</summary>
    public Emulator? Emulator { get; init; }

    /// <summary>未安装时的模板来源。</summary>
    public EmulatorTemplate? Template { get; init; }

    public string StatusText => IsInstalled ? "已安装" : "未安装";
    public string PlatformHint => PlatformDisplay;
}
