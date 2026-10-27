using System.IO;
using MicaRomManager.Models;
using MicaRomManager.Services.Devices;
using Xunit;

namespace MicaRomManager.Tests;

public class DeviceProfilerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-device-" + Guid.NewGuid().ToString("N"));

    public DeviceProfilerTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Roms", "PS"));
        Directory.CreateDirectory(Path.Combine(_dir, "Roms", "SFC"));
        Directory.CreateDirectory(Path.Combine(_dir, "Roms", "A", "B", "C"));
        Directory.CreateDirectory(Path.Combine(_dir, "$RECYCLE.BIN"));
        File.WriteAllText(Path.Combine(_dir, "Roms", "PS", "game.bin"), "x");
        File.WriteAllText(Path.Combine(_dir, "Roms", "SFC", "game.sfc"), "x");
        File.WriteAllText(Path.Combine(_dir, "$RECYCLE.BIN", "trash.txt"), "x");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // 临时目录清理失败不影响测试
        }
    }

    [Fact]
    public async Task Scanner_CollectsDirectoriesWithoutFileNames()
    {
        var summary = await DriveTreeScanner.ScanAsync(_dir, null, CancellationToken.None);

        Assert.Contains(summary.Nodes, node => node.RelPath == "Roms");
        Assert.Contains(summary.Nodes, node => node.RelPath == Path.Combine("Roms", "PS"));
        Assert.DoesNotContain(summary.Nodes, node => node.RelPath.Contains("RECYCLE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(summary.Nodes, node => node.RelPath.Contains(Path.Combine("Roms", "A", "B", "C")));

        var prompt = summary.ToPromptText();
        Assert.DoesNotContain("game.bin", prompt);
        Assert.DoesNotContain("game.sfc", prompt);
        Assert.Contains(".bin×1", prompt);
    }

    [Fact]
    public void Validate_AcceptsKnownPlatformsAndDropsUnknown()
    {
        var summary = Summary();
        var draft = new DeviceProfile
        {
            Name = "测试掌机",
            RootFolder = "Roms",
            FolderTemplate = "{root}/{platform}",
            PlatformFolders = new Dictionary<string, string>
            {
                ["PlayStation"] = "PS",
                ["SNES"] = "SFC",
                ["Unknown"] = "PS",
            },
        };

        var result = AiDeviceProfiler.Validate(summary, draft, "high",
            new[] { "PlayStation", "SNES" });

        Assert.True(result.CanApply);
        Assert.Equal("Roms", result.Profile!.RootFolder);
        Assert.Equal("PS", result.Profile.PlatformFolders["PlayStation"]);
        Assert.Equal("SFC", result.Profile.PlatformFolders["SNES"]);
        Assert.Contains(result.Warnings, warning => warning.Contains("Unknown"));
    }

    [Fact]
    public void Validate_RejectsUnknownRootAndUnsafeTemplate()
    {
        var summary = Summary();

        var badRoot = AiDeviceProfiler.Validate(summary,
            new DeviceProfile { RootFolder = "Media" }, "high", new[] { "PlayStation" });
        Assert.False(badRoot.CanApply);
        Assert.Contains("不在扫描结果", badRoot.Error);

        var badTemplate = AiDeviceProfiler.Validate(summary,
            new DeviceProfile { RootFolder = "Roms", FolderTemplate = "../{root}" },
            "high", new[] { "PlayStation" });
        Assert.False(badTemplate.CanApply);
        Assert.Contains("..", badTemplate.Error);
    }

    [Fact]
    public void Validate_DropsMissingDirectoryMapping()
    {
        var summary = Summary();
        var draft = new DeviceProfile
        {
            RootFolder = "Roms",
            FolderTemplate = "{root}/{platform}",
            PlatformFolders = new Dictionary<string, string> { ["PlayStation"] = "XYZ" },
        };

        var result = AiDeviceProfiler.Validate(summary, draft, "medium", new[] { "PlayStation" });

        Assert.True(result.CanApply);
        Assert.Empty(result.Profile!.PlatformFolders);
        Assert.Contains(result.Warnings, warning => warning.Contains("XYZ"));
    }

    private static DeviceScanSummary Summary() => new()
    {
        DriveRoot = "E:\\",
        Nodes = new List<ScanNode>
        {
            new() { RelPath = "Roms", FileCount = 0 },
            new() { RelPath = Path.Combine("Roms", "PS"), FileCount = 3 },
            new() { RelPath = Path.Combine("Roms", "SFC"), FileCount = 2 },
        },
    };
}
