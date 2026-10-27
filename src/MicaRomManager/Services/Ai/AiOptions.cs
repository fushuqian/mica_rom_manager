using MicaRomManager.Data;

namespace MicaRomManager.Services.Ai;

/// <summary>AI 接入配置；Key 只存本地 settings 表。</summary>
public sealed class AiOptions
{
    public const string BaseUrlKey = "ai.base_url";
    public const string ApiKeyKey = "ai.api_key";
    public const string ModelKey = "ai.model";
    public const string TimeoutKey = "ai.timeout_seconds";
    public const string BatchLimitKey = "ai.batch_limit";

    public const int DefaultTimeoutSeconds = 60;
    public const int DefaultBatchLimit = 50;

    public string BaseUrl { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "";
    public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;
    public int BatchLimit { get; init; } = DefaultBatchLimit;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Model);

    public static AiOptions Load(SettingsRepository settings)
    {
        var timeout = int.TryParse(settings.Get(TimeoutKey), out var seconds)
            ? Math.Clamp(seconds, 10, 600)
            : DefaultTimeoutSeconds;
        var limit = int.TryParse(settings.Get(BatchLimitKey), out var count)
            ? Math.Clamp(count, 1, 200)
            : DefaultBatchLimit;

        return new AiOptions
        {
            BaseUrl = settings.Get(BaseUrlKey)?.Trim() ?? "",
            ApiKey = settings.Get(ApiKeyKey)?.Trim() ?? "",
            Model = settings.Get(ModelKey)?.Trim() ?? "",
            TimeoutSeconds = timeout,
            BatchLimit = limit,
        };
    }
}
