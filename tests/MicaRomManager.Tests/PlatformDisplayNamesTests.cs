using MicaRomManager.Models;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class PlatformDisplayNamesTests
{
    public PlatformDisplayNamesTests()
    {
        PlatformDisplayNames.Initialize(new[]
        {
            new PlatformPreset
            {
                Vendor = "Nintendo",
                Consoles = { "NES", "SNES", "GBA" },
                Display = { ["GBA"] = "Game Boy Advance" },
            },
            new PlatformPreset
            {
                Vendor = "Sega",
                Consoles = { "MD" },
            },
        });
        PlatformDisplayNames.SetNaming(PlatformNaming.Japanese);
    }

    [Fact]
    public void RegionalNamesFollowNamingPreference()
    {
        Assert.Equal("Famicom", PlatformDisplayNames.Of("NES"));
        Assert.Equal("Super Famicom", PlatformDisplayNames.Of("SNES"));
        Assert.Equal("Mega Drive", PlatformDisplayNames.Of("MD"));

        PlatformDisplayNames.SetNaming(PlatformNaming.Western);
        Assert.Equal("NES", PlatformDisplayNames.Of("nes"));
        Assert.Equal("SNES", PlatformDisplayNames.Of("snes"));
        Assert.Equal("Genesis", PlatformDisplayNames.Of("MD"));
    }

    [Fact]
    public void NonRegionalPlatformsKeepPresetDisplay()
    {
        PlatformDisplayNames.SetNaming(PlatformNaming.Western);
        Assert.Equal("Game Boy Advance", PlatformDisplayNames.Of("GBA"));
        Assert.Equal("未知平台", PlatformDisplayNames.Of("未知平台"));
    }

    [Fact]
    public void VendorComesFromPresetsAndCustomIsLast()
    {
        Assert.Equal("Nintendo", PlatformDisplayNames.VendorOf("NES"));
        Assert.Equal("Sega", PlatformDisplayNames.VendorOf("md"));
        Assert.Equal("自定义", PlatformDisplayNames.VendorOf("某个自建平台"));

        // 排序权重按「平台 → 厂商」算：预置清单顺序在前，自定义最后
        Assert.True(PlatformDisplayNames.VendorRank("NES") < PlatformDisplayNames.VendorRank("MD"));
        Assert.True(PlatformDisplayNames.VendorRank("MD") < PlatformDisplayNames.VendorRank("某个自建平台"));
    }

    [Fact]
    public void StoredNamingRoundTrips()
    {
        Assert.Equal(PlatformNaming.Western, PlatformDisplayNames.ParseNaming("west"));
        Assert.Equal(PlatformNaming.Japanese, PlatformDisplayNames.ParseNaming(null));
        Assert.Equal(PlatformNaming.Japanese, PlatformDisplayNames.ParseNaming("jp"));
        Assert.Equal("west", PlatformDisplayNames.StoreNaming(PlatformNaming.Western));
        Assert.Equal("jp", PlatformDisplayNames.StoreNaming(PlatformNaming.Japanese));
    }
}
