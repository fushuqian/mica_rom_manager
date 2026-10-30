using System.IO;
using System.Security.Cryptography;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class RomHasherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-hash-" + Guid.NewGuid().ToString("N"));

    public RomHasherTests() => Directory.CreateDirectory(_dir);

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

    private string CreateFile(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Compute_SmallFileReturnsFullSha256()
    {
        var path = CreateFile("small.sfc", "hello rom");
        var expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

        Assert.Equal(expected, RomHasher.Compute(path));
    }

    [Fact]
    public void Compute_LargeFileUsesSampledPrefixAndIsStable()
    {
        var path = CreateFile("big.chd", new string('x', 4096) + new string('y', 4096));

        var first = RomHasher.Compute(path, fullHashLimit: 16);
        var second = RomHasher.Compute(path, fullHashLimit: 16);

        Assert.StartsWith("fast:", first);
        Assert.Equal(first, second);
        Assert.NotEqual(first, RomHasher.Compute(path, fullHashLimit: 1024 * 1024));
    }

    [Fact]
    public void Compute_SameContentDifferentNamesMatch()
    {
        var a = CreateFile("a.nes", "same bytes");
        var b = CreateFile("b.nes", "same bytes");

        // 同内容不同文件名 → 指纹一致，用于库内去重
        Assert.Equal(RomHasher.Compute(a), RomHasher.Compute(b));
    }

    [Fact]
    public void Compute_MissingOrEmptyPathReturnsEmpty()
    {
        Assert.Equal("", RomHasher.Compute(Path.Combine(_dir, "nope.bin")));
        Assert.Equal("", RomHasher.Compute(""));
    }
}
