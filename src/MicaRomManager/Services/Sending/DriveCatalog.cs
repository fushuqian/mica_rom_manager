using System.IO;
using MicaRomManager.Models;

namespace MicaRomManager.Services.Sending;

public sealed record DriveEntry(
    string RootPath,
    string Display,
    long FreeBytes,
    long TotalBytes,
    bool Removable);

/// <summary>枚举可发送的盘符设备（固定盘 + 可移动盘）。</summary>
public static class DriveCatalog
{
    public static IReadOnlyList<DriveEntry> List()
    {
        var result = new List<DriveEntry>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady) continue;
                if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable) continue;

                var label = "";
                try
                {
                    label = drive.VolumeLabel;
                }
                catch
                {
                    // 卷标读取失败忽略
                }

                var type = drive.DriveType == DriveType.Removable ? "可移动" : "本地磁盘";
                var display = $"{drive.RootDirectory.FullName.TrimEnd('\\')}\\  "
                              + (string.IsNullOrWhiteSpace(label) ? "" : label + " · ")
                              + $"可用 {Game.FormatSize(drive.AvailableFreeSpace)} / {Game.FormatSize(drive.TotalSize)}（{type}）";

                result.Add(new DriveEntry(
                    drive.RootDirectory.FullName,
                    display,
                    drive.AvailableFreeSpace,
                    drive.TotalSize,
                    drive.DriveType == DriveType.Removable));
            }
            catch
            {
                // 单个盘不可用不影响其它盘
            }
        }

        return result
            .OrderByDescending(entry => entry.Removable)
            .ThenBy(entry => entry.RootPath)
            .ToList();
    }
}
