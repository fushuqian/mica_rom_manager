using System.IO;
using MicaRomManager.Data;
using MicaRomManager.Models;
using Xunit;

namespace MicaRomManager.Tests;

public class LookupRepositoryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-lookup-" + Guid.NewGuid().ToString("N"));
    private readonly GameRepository _games;
    private readonly LookupRepository _lookups;
    private readonly PlatformRepository _platforms;

    public LookupRepositoryTests()
    {
        Directory.CreateDirectory(_dir);
        var db = new Database(Path.Combine(_dir, "t.db"));
        _games = new GameRepository(db);
        _lookups = new LookupRepository(db);
        _platforms = new PlatformRepository(db);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // 临时目录清理失败不影响测试结果
        }
    }

    [Fact]
    public void Rename_Region_CascadesToGames()
    {
        _games.Insert(new Game
        {
            Title = "A",
            RomPath = Path.Combine(_dir, "a.chd"),
            Region = "日版",
        });

        var result = _lookups.Rename(LookupRepository.Region, "日版", "日本版");

        Assert.True(result.Success);
        Assert.Equal(1, result.Affected);
        Assert.Equal("日本版", _games.ListAll().Single().Region);
        Assert.Contains("日本版", _lookups.List(LookupRepository.Region));
        Assert.DoesNotContain("日版", _lookups.List(LookupRepository.Region));
    }

    [Fact]
    public void Rename_Platform_CascadesToGamesAndConfig()
    {
        _lookups.Add(LookupRepository.Platform, "测试平台");
        var platform = _platforms.Get("测试平台")!;
        platform.EmulatorId = 5;
        _platforms.Save(platform);

        _games.Insert(new Game
        {
            Title = "A",
            Platform = "测试平台",
            RomPath = Path.Combine(_dir, "a.chd"),
        });

        var result = _lookups.Rename(LookupRepository.Platform, "测试平台", "测试平台2");

        Assert.True(result.Success);
        Assert.Equal("测试平台2", _games.ListAll().Single().Platform);
        Assert.NotNull(_platforms.Get("测试平台2"));
        Assert.Null(_platforms.Get("测试平台"));
        Assert.Equal(5, _platforms.Get("测试平台2")!.EmulatorId);
    }

    [Fact]
    public void Rename_Tag_CascadesToGames()
    {
        _games.Insert(new Game
        {
            Title = "A",
            RomPath = Path.Combine(_dir, "a.chd"),
            Tags = "动作,经典",
        });

        var result = _lookups.Rename(LookupRepository.Tag, "经典", "怀旧");

        Assert.True(result.Success);
        Assert.Equal("动作,怀旧", _games.ListAll().Single().Tags);
    }

    [Fact]
    public void Delete_ReferencedValue_IsRejected()
    {
        _games.Insert(new Game
        {
            Title = "A",
            RomPath = Path.Combine(_dir, "a.chd"),
            Region = "日版",
        });

        var result = _lookups.Delete(LookupRepository.Region, "日版");

        Assert.False(result.Success);
        Assert.Contains("1 条", result.Message);
        Assert.Contains("日版", _lookups.List(LookupRepository.Region));
    }

    [Fact]
    public void Delete_UnreferencedValue_Succeeds()
    {
        var result = _lookups.Delete(LookupRepository.Region, "韩版");

        Assert.True(result.Success);
        Assert.DoesNotContain("韩版", _lookups.List(LookupRepository.Region));
    }

    [Fact]
    public void PlatformList_ComesFromDictionary()
    {
        _lookups.Add(LookupRepository.Platform, "测试平台");

        var names = _platforms.ListAll().Select(p => p.Name).ToList();

        Assert.Contains("测试平台", names);
    }

    [Fact]
    public void Platform_ArgsTemplate_RoundTrips()
    {
        _lookups.Add(LookupRepository.Platform, "测试平台");
        var setting = _platforms.Get("测试平台")!;
        setting.ArgsTemplate = "\"{core}\" \"{rom}\"";
        _platforms.Save(setting);

        Assert.Equal("\"{core}\" \"{rom}\"", _platforms.Get("测试平台")!.ArgsTemplate);
    }
}
