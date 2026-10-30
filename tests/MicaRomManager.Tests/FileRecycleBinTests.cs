using System.IO;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

/// <summary>
/// 只测无副作用的守卫分支；真正把文件送进回收站会污染测试机，不在单测里做。
/// </summary>
public class FileRecycleBinTests
{
    [Fact]
    public void MoveToRecycleBin_MissingPathReturnsFalse()
    {
        var missing = Path.Combine(Path.GetTempPath(), "mica-missing-" + Guid.NewGuid().ToString("N") + ".bin");

        Assert.False(FileRecycleBin.MoveToRecycleBin(missing));
    }

    [Fact]
    public void MoveToRecycleBin_EmptyPathReturnsFalse()
    {
        Assert.False(FileRecycleBin.MoveToRecycleBin(""));
        Assert.False(FileRecycleBin.MoveToRecycleBin("   "));
    }
}
