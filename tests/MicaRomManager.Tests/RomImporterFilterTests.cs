using System.IO;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class RomImporterFilterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-import-" + Guid.NewGuid().ToString("N"));

    public RomImporterFilterTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "game.chd"), "chd");
        File.WriteAllText(Path.Combine(_dir, "game.iso"), "iso");
        File.WriteAllText(Path.Combine(_dir, "note.txt"), "note");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // 临时目录清理失败不影响测试结果
        }
    }

    [Fact]
    public void Scan_WithAllowedExtensions_SkipsOtherFiles()
    {
        var result = RomImporter.Scan(new[] { _dir }, false, Array.Empty<string>(),
            null, new[] { ".chd" });

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("game.chd", Path.GetFileName(candidate.FilePath));
    }

    [Fact]
    public void Scan_WithAllowedExtensions_AcceptsCustomExtension()
    {
        File.WriteAllText(Path.Combine(_dir, "game.gdi"), "gdi");

        var result = RomImporter.Scan(new[] { _dir }, false, Array.Empty<string>(),
            null, new[] { ".gdi" });

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("game.gdi", Path.GetFileName(candidate.FilePath));
    }

    [Fact]
    public void Scan_WithPlatformFormats_AcceptsConfiguredCustomExtension()
    {
        File.WriteAllText(Path.Combine(_dir, "game.gdi"), "gdi");
        var formats = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dreamcast"] = new[] { ".gdi" },
        };

        var result = RomImporter.Scan(new[] { _dir }, false, Array.Empty<string>(),
            null, null, formats);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("Dreamcast", candidate.Platform);
        Assert.False(candidate.NeedsSpecify);
    }

    [Fact]
    public void Scan_WithPlatformFormats_AutoDetectsUniquePlatform()
    {
        File.WriteAllText(Path.Combine(_dir, "game.pbp"), "pbp");
        var formats = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["PS1"] = new[] { ".pbp" },
        };

        var result = RomImporter.Scan(new[] { _dir }, false, Array.Empty<string>(),
            null, new[] { ".pbp" }, formats);

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("PS1", candidate.Platform);
        Assert.False(candidate.NeedsSpecify);
    }
}
