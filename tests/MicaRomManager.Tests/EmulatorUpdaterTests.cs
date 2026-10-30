using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class EmulatorUpdaterTests
{
    [Fact]
    public void PickWindowsAsset_PrefersWin64ArchiveOverOthers()
    {
        var assets = new List<(string? Name, string? Url)>
        {
            ("emulator-3ds.zip", "https://x/3ds.zip"),
            ("emulator-win64.zip", "https://x/win64.zip"),
            ("emulator-ubuntu.tar.gz", "https://x/linux.tar.gz"),
        };

        var picked = EmulatorUpdater.PickWindowsAsset(assets);

        Assert.NotNull(picked);
        Assert.Equal("emulator-win64.zip", picked!.Value.Name);
    }

    [Fact]
    public void PickWindowsAsset_FallsBackToZipThen7z()
    {
        var zipFirst = EmulatorUpdater.PickWindowsAsset(new List<(string?, string?)>
        {
            ("emulator.7z", "https://x/a.7z"),
            ("emulator.zip", "https://x/a.zip"),
        });
        Assert.Equal("emulator.zip", zipFirst!.Value.Name);

        var only7z = EmulatorUpdater.PickWindowsAsset(new List<(string?, string?)>
        {
            ("emulator.7z", "https://x/a.7z"),
        });
        Assert.Equal("emulator.7z", only7z!.Value.Name);
    }

    [Fact]
    public void PickWindowsAsset_IgnoresAssetsWithoutUrlAndHandlesEmpty()
    {
        Assert.Null(EmulatorUpdater.PickWindowsAsset(Array.Empty<(string?, string?)>()));

        var picked = EmulatorUpdater.PickWindowsAsset(new List<(string?, string?)>
        {
            ("emulator-win.zip", "https://x/win.zip"),
            ("notes.txt", "https://x/notes.txt"),
        });
        Assert.Equal("emulator-win.zip", picked!.Value.Name);
    }

    [Theory]
    [InlineData("v0.10.0", "0.9.0", true)]   // 数字段比较：0.10 > 0.9（字典序会判错）
    [InlineData("v0.9.0", "0.10.0", false)]
    [InlineData("1.2.3", "1.2.3", false)]
    [InlineData("1.2.4", "1.2.3", true)]
    [InlineData("2.0", null, true)]
    [InlineData(null, "2.0", false)]
    [InlineData("v1.0.0-beta", "1.0.0", false)]
    public void IsNewer_ComparesByNumericSegments(string? latest, string? current, bool expected)
    {
        Assert.Equal(expected, EmulatorUpdater.IsNewer(latest, current));
    }

    [Theory]
    [InlineData("libretro/RetroArch", "libretro/RetroArch")]
    [InlineData("  https://github.com/stenzek/duckstation/releases  ", "stenzek/duckstation")]
    [InlineData("https://github.com/PCSX2/pcsx2", "PCSX2/pcsx2")]
    [InlineData("https://github.com/a/b.git", "a/b")]
    [InlineData("owner/repo/", "owner/repo")]
    [InlineData("only-one", "")]
    [InlineData("https://gitlab.com/a/b", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeRepository_AcceptsOwnerRepoAndGitHubUrls(string? input, string expected)
    {
        Assert.Equal(expected, EmulatorUpdater.NormalizeRepository(input));
    }

    [Theory]
    [InlineData("v0.25.4", "0.25.4")]
    [InlineData("V1.2", "1.2")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("  v2  ", "2")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void VersionFromTag_TrimsLeadingV(string? tag, string expected)
    {
        Assert.Equal(expected, EmulatorUpdater.VersionFromTag(tag));
    }

    [Theory]
    [InlineData("rpcs3-v0.0.43-20511-9508444d_win64_msvc.7z", "0.0.43-20511")]
    [InlineData("pcsx2-v2.0.2-windows-x64-Qt.7z", "2.0.2")]
    [InlineData("mGBA-0.10.3-win64.7z", "0.10.3")]
    [InlineData("RetroArch-Win64.7z", "")]
    [InlineData("RetroArch-Win64.zip", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void VersionFromAssetName_ExtractsDottedVersion(string? assetName, string expected)
    {
        Assert.Equal(expected, EmulatorUpdater.VersionFromAssetName(assetName));
    }

    [Theory]
    [InlineData("build-9508444d31338e89a4f4cee1d0cee1800c96d2c1",
        "rpcs3-v0.0.43-20511-9508444d_win64_msvc.7z", "0.0.43-20511")]
    [InlineData("v0.25.4", "whatever.zip", "0.25.4")]
    [InlineData(null, "app-1.2.3.zip", "1.2.3")]
    [InlineData("nightly", "RetroArch-Win64.7z", "")]
    [InlineData("build-x", null, "")]
    public void ResolveVersion_PrefersTagThenAssetVersion(string? tag, string? asset, string expected)
    {
        Assert.Equal(expected, EmulatorUpdater.ResolveVersion(tag, asset));
    }

    [Fact]
    public void ResolveVersion_FallsBackToPublishedDateForRollingRelease()
    {
        var published = new DateTimeOffset(2026, 10, 25, 2, 59, 23, TimeSpan.Zero);

        var version = EmulatorUpdater.ResolveVersion(
            "latest", "duckstation-windows-x64-release.zip", published);

        Assert.Equal("2026.10.25", version);
        Assert.True(EmulatorUpdater.IsNewer(version, "latest"));
        Assert.False(EmulatorUpdater.IsNewer(version, "2026.10.25"));
    }

    [Fact]
    public void PickWindowsAsset_DuckStation_PicksX64ReleaseNotArm64Symbols()
    {
        var assets = new List<(string?, string?)>
        {
            ("DuckStation-arm64.AppImage", "https://x/1"),
            ("DuckStation-armhf.AppImage", "https://x/2"),
            ("duckstation-mac-release.zip", "https://x/3"),
            ("duckstation-windows-arm64-installer.exe", "https://x/4"),
            ("duckstation-windows-arm64-release-symbols.7z", "https://x/5"),
            ("duckstation-windows-arm64-release.zip", "https://x/6"),
            ("duckstation-windows-x64-installer.exe", "https://x/7"),
            ("duckstation-windows-x64-release-symbols.7z", "https://x/8"),
            ("duckstation-windows-x64-release.zip", "https://x/9"),
            ("duckstation-windows-x64-sse2-release.zip", "https://x/10"),
            ("DuckStation-x64.AppImage", "https://x/11"),
        };

        var picked = EmulatorUpdater.PickWindowsAsset(assets);

        Assert.Equal("duckstation-windows-x64-release.zip", picked!.Value.Name);
    }

    [Fact]
    public void PickWindowsAsset_PCSX2_PicksQtArchiveNotSymbols()
    {
        var assets = new List<(string?, string?)>
        {
            ("pcsx2-v2.8.2-linux-appimage-x64-Qt.AppImage", "https://x/1"),
            ("pcsx2-v2.8.2-macos-Qt.tar.xz", "https://x/2"),
            ("pcsx2-v2.8.2-windows-x64-installer.exe", "https://x/3"),
            ("pcsx2-v2.8.2-windows-x64-Qt-symbols.7z", "https://x/4"),
            ("pcsx2-v2.8.2-windows-x64-Qt.7z", "https://x/5"),
        };

        var picked = EmulatorUpdater.PickWindowsAsset(assets);

        Assert.Equal("pcsx2-v2.8.2-windows-x64-Qt.7z", picked!.Value.Name);
    }

    [Fact]
    public void PickWindowsAsset_PPSSPP_PicksX64NotArm64()
    {
        var assets = new List<(string?, string?)>
        {
            ("ppsspp-1.20.4.tar.xz", "https://x/1"),
            ("PPSSPP-v1.20.4-anylinux-aarch64.AppImage", "https://x/2"),
            ("PPSSPP-v1.20.4-anylinux-x86_64.AppImage", "https://x/3"),
            ("PPSSPP-v1.20.4-Windows-ARM64.zip", "https://x/4"),
            ("PPSSPP-v1.20.4-Windows-x64.zip", "https://x/5"),
            ("PPSSPPSDL-macOS-v1.20.4.zip", "https://x/6"),
        };

        var picked = EmulatorUpdater.PickWindowsAsset(assets);

        Assert.Equal("PPSSPP-v1.20.4-Windows-x64.zip", picked!.Value.Name);
    }

    [Fact]
    public void PickWindowsAsset_MesenAndMgba_PickWindowsX64Archives()
    {
        var mesen = EmulatorUpdater.PickWindowsAsset(new List<(string?, string?)>
        {
            ("Mesen_2.1.1_Linux_ARM64.zip", "https://x/1"),
            ("Mesen_2.1.1_Linux_x64.zip", "https://x/2"),
            ("Mesen_2.1.1_macOS_x64_Intel.zip", "https://x/3"),
            ("Mesen_2.1.1_Windows.zip", "https://x/4"),
        });
        Assert.Equal("Mesen_2.1.1_Windows.zip", mesen!.Value.Name);

        var mgba = EmulatorUpdater.PickWindowsAsset(new List<(string?, string?)>
        {
            ("mGBA-0.10.5-3ds.7z", "https://x/1"),
            ("mGBA-0.10.5-switch.7z", "https://x/2"),
            ("mGBA-0.10.5-win32.7z", "https://x/3"),
            ("mGBA-0.10.5-win64.7z", "https://x/4"),
        });
        Assert.Equal("mGBA-0.10.5-win64.7z", mgba!.Value.Name);
    }

    [Fact]
    public void PickWindowsAsset_SourceOnlyReleaseReturnsNull()
    {
        var picked = EmulatorUpdater.PickWindowsAsset(new List<(string?, string?)>
        {
            ("retroarch-sourceonly-1.22.2.tar.xz", "https://x/source.tar.xz"),
        });

        Assert.Null(picked);
    }

    [Theory]
    [InlineData("0.0.43-20511", "build-9508444d", true)]   // 旧的非数字版本应提示更新一次
    [InlineData("build-9508444d", "0.0.43-20511", false)]
    [InlineData("nightly", "1.2.3", false)]
    public void IsNewer_NumericLatestBeatsNonNumericCurrent(string latest, string current, bool expected)
    {
        Assert.Equal(expected, EmulatorUpdater.IsNewer(latest, current));
    }
}
