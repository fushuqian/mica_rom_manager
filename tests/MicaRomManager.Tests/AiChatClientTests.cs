using System.IO;
using MicaRomManager.Data;
using MicaRomManager.Services;
using MicaRomManager.Services.Ai;
using Xunit;

namespace MicaRomManager.Tests;

public class AiChatClientTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-aiclient-" + Guid.NewGuid().ToString("N"));
    private readonly SettingsRepository _settings;
    private readonly NetworkClientProvider _network;

    public AiChatClientTests()
    {
        Directory.CreateDirectory(_dir);
        _settings = new SettingsRepository(new Database(Path.Combine(_dir, "t.db")));
        _network = new NetworkClientProvider(_settings);
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

    [Fact]
    public void Rebuild_IsSafeWhenCalledConcurrently()
    {
        var client = new AiChatClient(_settings, _network);

        // 改代理会触发 Rebuild，而标签/翻译是 2 路并发调用；这里压测替换过程不抛异常
        var error = Record.Exception(() => Parallel.For(0, 100, _ => client.Rebuild()));

        Assert.Null(error);
    }

    [Fact]
    public void Rebuild_KeepsOptionsReadable()
    {
        _settings.Set(AiOptions.BaseUrlKey, "https://api.deepseek.com/v1");
        _settings.Set(AiOptions.ModelKey, "deepseek-chat");
        _settings.Set(AiOptions.ApiKeyKey, SecretProtector.Protect("sk-x"));

        var client = new AiChatClient(_settings, _network);
        client.Rebuild();

        Assert.Equal("deepseek-chat", client.Options.Model);
        Assert.Equal("sk-x", client.Options.ApiKey);
    }
}
