using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using MicaRomManager.Models;

namespace MicaRomManager.Services.Sending;

public partial class SendTaskItem : ObservableObject
{
    public required Game Game { get; init; }

    public string Title => Game.Title;
    public string Platform => Game.Platform;

    public string FileText
    {
        get
        {
            var discs = Game.Discs;
            return discs.Count <= 1
                ? Path.GetFileName(Game.RomPath)
                : $"{discs.Count} 个文件";
        }
    }

    public string SizeText { get; init; } = "";

    [ObservableProperty] private string status = "排队中";
    [ObservableProperty] private double progress;
    [ObservableProperty] private string progressText = "";
    [ObservableProperty] private string message = "";
    [ObservableProperty] private string targetFolder = "";

    public bool IsFinished => Status is "已完成" or "已跳过" or "部分完成" or "失败";
}
