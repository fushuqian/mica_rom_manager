using MicaRomManager.Data;

namespace MicaRomManager.Services.Ai;

/// <summary>联网搜索配置；复用 AI 的 BaseUrl / API Key（仅 DeepSeek 官方端点支持 Search API）。</summary>
public sealed class AiSearchOptions
{
    public const string EnabledKey = "ai.search.enabled";
    public const string MaxResultsKey = "ai.search.max_results";
    public const int DefaultMaxResults = 5;

    public bool Enabled { get; init; } = false;
    public int MaxResults { get; init; } = DefaultMaxResults;

    public static AiSearchOptions Load(SettingsRepository settings)
    {
        var max = int.TryParse(settings.Get(MaxResultsKey), out var count)
            ? Math.Clamp(count, 1, 10)
            : DefaultMaxResults;
        return new AiSearchOptions
        {
            Enabled = settings.GetBool(EnabledKey),
            MaxResults = max,
        };
    }

    /// <summary>DeepSeek 官方 Search API 只在 api.deepseek.com 端点可用。</summary>
    public static bool IsDeepSeekEndpoint(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return false;
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.Trim().ToLowerInvariant();
        return host == "api.deepseek.com" || host.EndsWith(".deepseek.com", StringComparison.Ordinal);
    }
}
