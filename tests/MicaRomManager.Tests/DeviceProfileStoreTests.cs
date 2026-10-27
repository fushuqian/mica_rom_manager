using System.IO;
using MicaRomManager.Models;
using MicaRomManager.Services.Devices;
using Xunit;

namespace MicaRomManager.Tests;

public class DeviceProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-device-" + Guid.NewGuid().ToString("N"));
    private readonly string _builtinFile;
    private readonly string _userDir;

    public DeviceProfileStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _userDir = Path.Combine(_dir, "devices");
        _builtinFile = Path.Combine(_dir, "builtin.json");
        File.WriteAllText(_builtinFile, """
            [
              {
                "id": "trimui",
                "name": "TrimUI Smart Pro",
                "brand": "TrimUI",
                "model": "Smart Pro",
                "transport": "drive",
                "rootFolder": "Roms",
                "folderTemplate": "{root}/{platform}"
              }
            ]
            """);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // 清理失败不影响测试
        }
    }

    [Fact]
    public void Load_ReadsBuiltInProfiles()
    {
        var store = new DeviceProfileStore(_userDir, _builtinFile);
        var profile = Assert.Single(store.Load());

        Assert.Equal("trimui", profile.Id);
        Assert.True(profile.IsBuiltIn);
        Assert.Equal("Roms", profile.RootFolder);
    }

    [Fact]
    public void Save_SameIdOverridesBuiltIn()
    {
        var store = new DeviceProfileStore(_userDir, _builtinFile);
        var profile = store.Load().Single();
        profile.Name = "我的 TrimUI";
        store.Save(profile);

        var reloaded = store.Load().Single();
        Assert.Equal("我的 TrimUI", reloaded.Name);
        Assert.False(reloaded.IsBuiltIn);
    }

    [Fact]
    public void Save_GeneratesId_AndRoundTrips()
    {
        var store = new DeviceProfileStore(_userDir, _builtinFile);
        var profile = new DeviceProfile { Name = "My Handheld", RootFolder = "Games" };

        var id = store.Save(profile);

        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.True(File.Exists(Path.Combine(_userDir, id + ".json")));
        Assert.Contains(store.Load(), p => p.Id == id && p.RootFolder == "Games");
    }

    [Fact]
    public void Delete_RemovesUserOverrideOnly()
    {
        var store = new DeviceProfileStore(_userDir, _builtinFile);
        var profile = store.Load().Single();
        profile.Name = "覆盖";
        store.Save(profile);

        Assert.True(store.Delete(profile));

        var reloaded = store.Load().Single();
        Assert.True(reloaded.IsBuiltIn);
        Assert.Equal("TrimUI Smart Pro", reloaded.Name);
    }

    [Fact]
    public void BuiltInProfiles_LoadAndContainTrimUI()
    {
        var builtin = Path.Combine(AppContext.BaseDirectory, "Resources", "DeviceProfiles.json");
        var store = new DeviceProfileStore(Path.Combine(_dir, "devices-builtin"), builtin);

        var profiles = store.Load();

        Assert.Equal(3, profiles.Count);
        var trimui = Assert.Single(profiles, p => p.Id == "trimui-smart-pro");
        Assert.True(trimui.IsBuiltIn);
        Assert.Equal("drive", trimui.Transport);
        Assert.Equal("PS", trimui.PlatformFolders["PlayStation"]);
        Assert.Equal("FC", trimui.PlatformFolders["NES"]);
        Assert.False(trimui.AllowUnmappedPlatforms);
    }

    [Fact]
    public void Load_ReadsUserFileWithChineseName()
    {
        Directory.CreateDirectory(_userDir);
        File.WriteAllText(Path.Combine(_userDir, "custom.json"), """
            {
              "id": "custom",
              "name": "我的掌机",
              "rootFolder": "Roms",
              "folderTemplate": "{root}/{platform}",
              "platformFolders": { "PlayStation": "PS" }
            }
            """);

        var store = new DeviceProfileStore(_userDir, _builtinFile);
        var profile = Assert.Single(store.Load(), p => p.Id == "custom");

        Assert.Equal("我的掌机", profile.Name);
        Assert.Equal("PS", profile.PlatformFolders["PlayStation"]);
        Assert.False(profile.IsBuiltIn);
    }
}
