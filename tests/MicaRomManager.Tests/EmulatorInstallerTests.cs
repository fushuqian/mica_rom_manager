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

    [Fact]
    public void InstallFromZip_PreservesUserDataFromPreviousInstall()
    {
        var target = Path.Combine(_paths.EmulatorsDir, "DuckStation");
        Write(target + "\\", "settings.ini", "old settings");
        Write(target + "\\", @"userdata\save.dat", "save");

        var zipPath = Path.Combine(_dir, "duckstation.zip");
        var payload = Path.Combine(_dir, "duckstation-payload");
        Write(payload, "duckstation-qt-x64-ReleaseLTCG.exe");
        Write(payload, "Qt6Core.dll");
        System.IO.Compression.ZipFile.CreateFromDirectory(payload, zipPath);

        var result = _installer.InstallFromArchive(zipPath, "DuckStation");

        Assert.True(File.Exists(Path.Combine(result.InstallDir, "duckstation-qt-x64-ReleaseLTCG.exe")));
        Assert.Equal("old settings", File.ReadAllText(Path.Combine(result.InstallDir, "settings.ini")));
        Assert.Equal("save", File.ReadAllText(Path.Combine(result.InstallDir, "userdata", "save.dat")));
        Assert.NotEmpty(Directory.GetDirectories(_paths.BackupsDir));
    }

    [Fact]
    public void MergeMissingFiles_KeepsNewProgramFilesAndAddsOldUserData()
    {
        var backup = Path.Combine(_dir, "backup");
        Write(backup + "\\", "app.exe", "old");
        Write(backup + "\\", "save.dat", "save");

        var target = Path.Combine(_dir, "target");
        Write(target + "\\", "app.exe", "new");

        EmulatorInstaller.MergeMissingFiles(backup, target);

        Assert.Equal("new", File.ReadAllText(Path.Combine(target, "app.exe")));
        Assert.Equal("save", File.ReadAllText(Path.Combine(target, "save.dat")));
    }

    [Fact]
    public void PrepareTargetWithBackup_ReturnsDistinctBackupPaths()
    {
        Write(Path.Combine(_paths.EmulatorsDir, "RetroArch") + "\\", "old.exe", "old");

        var first = _installer.PrepareTargetWithBackup("RetroArch");
        var second = _installer.PrepareTargetWithBackup("RetroArch");

        Assert.NotNull(first.Backup);
        Assert.NotNull(second.Backup);
        Assert.NotEqual(first.Backup, second.Backup);
        Assert.True(Directory.Exists(first.Backup));
        Assert.True(Directory.Exists(second.Backup));
        Assert.True(File.Exists(Path.Combine(first.Backup!, "old.exe")));
    }

    [Fact]
    public void RollbackInstall_RemovesPartialTargetAndRestoresBackup()
    {
        var target = Path.Combine(_paths.EmulatorsDir, "RetroArch");
        Write(target + "\\", "partial.exe", "partial");

        var backup = Path.Combine(_paths.BackupsDir, "RetroArch-old");
        Write(backup + "\\", "retroarch.exe", "old");

        EmulatorInstaller.RollbackInstall(target, backup);

        Assert.True(File.Exists(Path.Combine(target, "retroarch.exe")));
        Assert.False(File.Exists(Path.Combine(target, "partial.exe")));
        Assert.False(Directory.Exists(backup));
    }

    [Fact]
    public void RollbackInstall_DoesNotThrowWithoutBackup()
    {
        var target = Path.Combine(_paths.EmulatorsDir, "RetroArch");
        Directory.CreateDirectory(target);

        EmulatorInstaller.RollbackInstall(target, null);

        Assert.False(Directory.Exists(target));
    }
}
