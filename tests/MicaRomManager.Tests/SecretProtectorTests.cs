using System.IO;
using MicaRomManager.Data;
using MicaRomManager.Services;
using MicaRomManager.Services.Ai;
using Xunit;

namespace MicaRomManager.Tests;

public class SecretProtectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-secret-" + Guid.NewGuid().ToString("N"));

    public SecretProtectorTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new Database(Path.Combine(_dir, "t.db"));
        _settings = new SettingsRepository(_db);
    }

    private readonly Database _db;
    private readonly SettingsRepository _settings;

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
    public void Protect_RoundTripsAndHidesPlainText()
    {
        var protectedValue = SecretProtector.Protect("sk-1234567890");

        Assert.True(SecretProtector.IsProtected(protectedValue));
        Assert.DoesNotContain("sk-1234567890", protectedValue);
        Assert.Equal("sk-1234567890", SecretProtector.Unprotect(protectedValue));
    }

    [Fact]
    public void Protect_IsIdempotentAndHandlesEmpty()
    {
        var once = SecretProtector.Protect("sk-abc");

        Assert.Equal(once, SecretProtector.Protect(once));
        Assert.Equal("", SecretProtector.Protect(""));
        Assert.Equal("", SecretProtector.Unprotect(""));
    }

    [Fact]
    public void Unprotect_ReturnsLegacyPlainTextAsIs()
    {
        // 老库里的明文 Key 不做处理，保证可以直接升级
        Assert.Equal("sk-legacy", SecretProtector.Unprotect("sk-legacy"));
    }

    [Fact]
    public void Unprotect_ReturnsEmptyForCorruptCipherText()
    {
        Assert.Equal("", SecretProtector.Unprotect("dpapi:not-base64!!"));
    }

    [Fact]
    public void AiOptions_LoadDecryptsStoredKey()
    {
        _settings.Set(AiOptions.BaseUrlKey, "https://api.deepseek.com/v1");
        _settings.Set(AiOptions.ModelKey, "deepseek-chat");
        _settings.Set(AiOptions.ApiKeyKey, SecretProtector.Protect("sk-stored"));

        var options = AiOptions.Load(_settings);

        Assert.Equal("sk-stored", options.ApiKey);
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void Migrate_EncryptsLegacyPlainTextOnceAndKeepsValue()
    {
        _settings.Set(AiOptions.ApiKeyKey, "sk-plain");

        SecretProtector.Migrate(_settings, AiOptions.ApiKeyKey);

        var stored = _settings.Get(AiOptions.ApiKeyKey)!;
        Assert.True(SecretProtector.IsProtected(stored));
        Assert.Equal("sk-plain", SecretProtector.Unprotect(stored));

        // 再跑一次不应重复加密
        SecretProtector.Migrate(_settings, AiOptions.ApiKeyKey);
        Assert.Equal(stored, _settings.Get(AiOptions.ApiKeyKey));
    }
}
