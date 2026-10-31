using System.IO;
using MicaRomManager.Data;
using MicaRomManager.Services;
using Xunit;

namespace MicaRomManager.Tests;

public class PlatformOrderStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-order-" + Guid.NewGuid().ToString("N"));
    private readonly SettingsRepository _settings;

    public PlatformOrderStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _settings = new SettingsRepository(new Database(Path.Combine(_dir, "t.db")));
        PlatformOrderStore.Reset(_settings);
    }

    public void Dispose()
    {
        PlatformOrderStore.Reset(_settings);
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
    public void SavedOrderWinsAndUnknownsFallBack()
    {
        PlatformOrderStore.Save(_settings, new[] { "SNES", "NES" });

        Assert.True(PlatformOrderStore.Compare("SNES", "NES") < 0);
        // 没排进顺序里的主机退回「厂商 → 年代」兜底，排在自定义顺序之后
        Assert.True(PlatformOrderStore.Compare("NES", "GBA") < 0);
    }

    [Fact]
    public void ResetRestoresDefaultEraOrder()
    {
        PlatformOrderStore.Save(_settings, new[] { "SNES", "NES" });
        PlatformOrderStore.Reset(_settings);

        Assert.False(PlatformOrderStore.HasCustomOrder);
        Assert.True(PlatformOrderStore.Compare("NES", "SNES") < 0);
    }

    [Fact]
    public void OrderPersistsAcrossReload()
    {
        var saved = PlatformOrderStore.Save;
        saved(_settings, new[] { "GBA", "NES" });

        PlatformOrderStore.Initialize(_settings);

        Assert.True(PlatformOrderStore.Compare("GBA", "NES") < 0);
        Assert.Equal(new[] { "GBA", "NES" }, PlatformOrderStore.Current);
    }
}
