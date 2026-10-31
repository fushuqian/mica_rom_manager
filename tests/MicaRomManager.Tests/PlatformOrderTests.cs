using MicaRomManager.Models;
using Xunit;

namespace MicaRomManager.Tests;

public class PlatformOrderTests
{
    [Fact]
    public void EraOrderFollowsReleaseYear()
    {
        Assert.Equal(1983, PlatformOrder.YearOf("NES"));
        Assert.Equal(1990, PlatformOrder.YearOf("SNES"));
        Assert.Equal(2001, PlatformOrder.YearOf("NGC"));

        // 同一厂商内：Famicom → Super Famicom → GameCube，不会按字母序把 GameCube 插到两代之间
        Assert.True(PlatformOrder.CompareByEra("NES", "SNES") < 0);
        Assert.True(PlatformOrder.CompareByEra("SNES", "NGC") < 0);
        Assert.True(PlatformOrder.CompareByEra("PCE", "PCECD") < 0);
    }

    [Fact]
    public void SameYearFallsBackToDisplayName()
    {
        // GBA 与 NGC 同为 2001 年，按展示名稳定排序，不会每次刷新乱跳
        var first = PlatformOrder.CompareByEra("GBA", "NGC");
        var second = PlatformOrder.CompareByEra("GBA", "NGC");
        Assert.Equal(first, second);
    }

    [Fact]
    public void UnknownPlatformsSortLast()
    {
        Assert.Equal(int.MaxValue, PlatformOrder.YearOf("某个自建平台"));
        Assert.True(PlatformOrder.CompareByEra("NGC", "某个自建平台") < 0);
    }
}
