using System.Collections.ObjectModel;
using MicaRomManager.Models;
using MicaRomManager.Services.Devices;

namespace MicaRomManager.ViewModels;

/// <summary>AI 设备配置预览：展示扫描摘要、建议配置与校验警告。</summary>
public sealed class AiDeviceProfileViewModel
{
    public DeviceScanSummary Summary { get; }
    public DeviceProfileResult Result { get; }
    public ObservableCollection<ScanNode> Nodes { get; } = new();

    public AiDeviceProfileViewModel(DeviceScanSummary summary, DeviceProfileResult result)
    {
        Summary = summary;
        Result = result;
        foreach (var node in summary.Nodes.Take(60)) Nodes.Add(node);
    }

    public string Header =>
        $"盘符 {Summary.DriveRoot}　卷标 {(Summary.VolumeLabel.Length == 0 ? "无" : Summary.VolumeLabel)}"
        + $"　扫描目录 {Summary.Nodes.Count} 个";

    public string ConfidenceText => Result.Confidence switch
    {
        "high" => "置信度：高",
        "medium" => "置信度：中，建议核对",
        "low" => "置信度：低，请人工核对",
        _ => "置信度：未知",
    };

    public string WarningsText => Result.Warnings.Count == 0
        ? "校验通过。"
        : string.Join("\n", Result.Warnings.Select(item => "• " + item));

    public bool CanApply => Result.CanApply;

    public string Error => Result.Error;

    public DeviceProfile? Profile => Result.Profile;

    public string Name => Profile?.Name ?? "";
    public string BrandModel => $"{Profile?.Brand} {Profile?.Model}".Trim();
    public string RootFolder => Profile?.RootFolder ?? "";
    public string FolderTemplate => Profile?.FolderTemplate ?? "";
    public string PlatformFoldersText => Profile == null || Profile.PlatformFolders.Count == 0
        ? "（无）"
        : string.Join("，", Profile.PlatformFolders.Select(pair => $"{pair.Key} → {pair.Value}"));
    public string Notes => Profile?.Notes ?? "";
}
