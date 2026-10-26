using System.IO;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class LocalPathTests
{
    private static readonly string DataDir = Path.Combine(Path.GetTempPath(), "mica-localpath-tests", "data");

    [Fact]
    public void ToStored_RewritesPathUnderDataDir()
    {
        var absolute = Path.Combine(DataDir, "emulators", "PCSX2", "pcsx2.exe");

        var stored = LocalPath.ToStored(absolute, DataDir);

        Assert.Equal(Path.Combine(LocalPath.DataToken, "emulators", "PCSX2", "pcsx2.exe"), stored);
    }

    [Fact]
    public void ToStored_KeepsPathOutsideDataDir()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "elsewhere", "retroarch.exe");

        var stored = LocalPath.ToStored(absolute, DataDir);

        Assert.Equal(Path.GetFullPath(absolute), stored);
    }

    [Fact]
    public void ToStored_DoesNotMatchSiblingDirectoryWithSamePrefix()
    {
        var sibling = DataDir + "2";
        var absolute = Path.Combine(sibling, "emu.exe");

        var stored = LocalPath.ToStored(absolute, DataDir);

        Assert.Equal(Path.GetFullPath(absolute), stored);
    }

    [Fact]
    public void Expand_RestoresAbsolutePath()
    {
        var stored = Path.Combine(LocalPath.DataToken, "emulators", "Dolphin", "Dolphin.exe");

        var expanded = LocalPath.Expand(stored, DataDir);

        Assert.Equal(Path.GetFullPath(Path.Combine(DataDir, "emulators", "Dolphin", "Dolphin.exe")), expanded);
    }

    [Fact]
    public void Expand_LeavesAbsolutePathUntouched()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "x", "emu.exe");

        var expanded = LocalPath.Expand(absolute, DataDir);

        Assert.Equal(absolute, expanded);
    }

    [Fact]
    public void RoundTrip_PreservesPath()
    {
        var absolute = Path.Combine(DataDir, "emulators", "PPSSPP", "PPSSPPWindows64.exe");

        var restored = LocalPath.Expand(LocalPath.ToStored(absolute, DataDir), DataDir);

        Assert.Equal(Path.GetFullPath(absolute), restored);
    }
}
