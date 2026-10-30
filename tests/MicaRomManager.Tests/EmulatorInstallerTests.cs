using System.IO;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class EmulatorInstallerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-install-" + Guid.NewGuid().ToString("N"));
    private readonly EmulatorInstaller _installer;
    private readonly PathService _paths;

    public EmulatorInstallerTests()
    {
        Directory.CreateDirectory(_dir);
        _paths = new PathService(_dir);
        _installer = new EmulatorInstaller(_paths);
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

    private static void Write(string root, string relative, string content = "x")
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void InstallFromFolder_CopiesTreeAndRanksMainExeFirst()
    {
        var source = Path.Combine(_dir, "src");
        Write(source, "retroarch.exe");
        Write(source, @"tools\updater.exe");
        Write(source, @"configs\retroarch.cfg", "video_driver = d3d11");

        var result = _installer.InstallFromFolder(source, "RetroArch");

        Assert.Equal(Path.Combine(_paths.EmulatorsDir, "RetroArch"), result.InstallDir);
        Assert.True(File.Exists(Path.Combine(result.InstallDir, "retroarch.exe")));
        Assert.True(File.Exists(Path.Combine(result.InstallDir, "configs", "retroarch.cfg")));

        // updater.exe 被惩罚关键字压分，主程序必须排第一
        Assert.NotEmpty(result.ExeCandidates);
        Assert.Equal("retroarch.exe", Path.GetFileName(result.ExeCandidates[0]));
    }

    [Fact]
    public void PrepareTarget_MovesExistingInstallToBackupInsteadOfDeleting()
    {
        Write(Path.Combine(_paths.EmulatorsDir, "RetroArch") + "\\", "old.exe", "old");

        var target = _installer.PrepareTarget("RetroArch");

        Assert.True(Directory.Exists(target));
        Assert.False(File.Exists(Path.Combine(target, "old.exe")));

        var backups = Directory.GetDirectories(_paths.BackupsDir);
        Assert.Single(backups);
        Assert.True(File.Exists(Path.Combine(backups[0], "old.exe")));
    }

    [Fact]
    public void PrepareTarget_SanitizesEmulatorName()
    {
        var target = _installer.PrepareTarget("Dolphin: 模拟器?");

        Assert.True(Directory.Exists(target));
        Assert.DoesNotContain(':', Path.GetFileName(target));
        Assert.DoesNotContain('?', Path.GetFileName(target));
    }

    [Fact]
    public void InstallFromArchive_RejectsUnsupportedExtension()
    {
        var archive = Path.Combine(_dir, "setup.exe");
        File.WriteAllText(archive, "not an archive");

        Assert.Throws<NotSupportedException>(() => _installer.InstallFromArchive(archive, "RetroArch"));
    }

    [Fact]
    public void InstallFromZip_ExtractsIntoEmulatorsDir()
    {
        var zipPath = Path.Combine(_dir, "dolphin.zip");
        var payload = Path.Combine(_dir, "payload");
        Write(payload, "Dolphin.exe");
        Write(payload, @"Languages\en.po", "msgid \"\"");
        System.IO.Compression.ZipFile.CreateFromDirectory(payload, zipPath);

        var result = _installer.InstallFromArchive(zipPath, "Dolphin");

        Assert.True(File.Exists(Path.Combine(result.InstallDir, "Dolphin.exe")));
        Assert.True(File.Exists(Path.Combine(result.InstallDir, "Languages", "en.po")));
        Assert.Contains(result.ExeCandidates, path => Path.GetFileName(path) == "Dolphin.exe");
    }
}
