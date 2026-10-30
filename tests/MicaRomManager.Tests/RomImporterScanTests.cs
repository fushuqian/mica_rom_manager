using System.IO;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class RomImporterScanTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-scan-" + Guid.NewGuid().ToString("N"));

    public RomImporterScanTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // 清理失败不影响测试结果
        }
    }

    private sealed class CollectingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = new();

        public void Report(T value) => Values.Add(value);
    }

    [Fact]
    public void Scan_ReportsProgressUpToTotal()
    {
        for (var index = 0; index < 3; index++)
            File.WriteAllText(Path.Combine(_dir, $"game{index}.nes"), $"rom{index}");
        File.WriteAllText(Path.Combine(_dir, "note.txt"), "note");

        var progress = new CollectingProgress<RomImporter.ScanProgress>();
        var result = RomImporter.Scan(new[] { _dir }, false, Array.Empty<string>(),
            progress: progress);

        Assert.Equal(3, result.Candidates.Count);
        Assert.NotEmpty(progress.Values);

        var last = progress.Values[^1];
        Assert.Equal(last.Total, last.Processed);
        Assert.Equal(4, last.Total); // 包含被格式过滤的 note.txt
        Assert.True(progress.Values.Zip(progress.Values.Skip(1))
            .All(pair => pair.First.Processed <= pair.Second.Processed));
    }

    [Fact]
    public void Scan_ThrowsWhenTokenAlreadyCancelled()
    {
        File.WriteAllText(Path.Combine(_dir, "game.nes"), "rom");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            RomImporter.Scan(new[] { _dir }, false, Array.Empty<string>(),
                cancellationToken: cts.Token));
    }

    [Fact]
    public void Scan_WithoutInputs_ReturnsEmptyResult()
    {
        var result = RomImporter.Scan(Array.Empty<string>(), false, Array.Empty<string>());

        Assert.Empty(result.Candidates);
        Assert.Equal(0, result.DuplicateCount);
        Assert.Equal(0, result.ExcludedCount);
    }
}
