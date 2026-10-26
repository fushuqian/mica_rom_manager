using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace MicaRomManager.Services;

public sealed record UpdateCheckResult(
    bool Success,
    string Message,
    string? LatestTag = null,
    string? ReleaseUrl = null,
    string? AssetName = null,
    string? AssetUrl = null);

public sealed class EmulatorUpdater
{
    private readonly HttpClient _client;

    public EmulatorUpdater()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("MicaRomManager/0.1");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
    }

    /// <summary>调用 GitHub 公共 Release API（免费、无需 Token，未认证 60 次/小时）。</summary>
    public async Task<UpdateCheckResult> CheckAsync(string repository, string currentVersion)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            return new UpdateCheckResult(false, "未配置 GitHub 仓库（格式：owner/repo），无法检查更新。");
        }

        var parts = repository.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return new UpdateCheckResult(false, "GitHub 仓库格式不正确，应为 owner/repo。");
        }

        var url = $"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases/latest";
        try
        {
            using var response = await _client.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(false, "仓库不存在或没有已发布的 Release。");
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                return new UpdateCheckResult(false, "GitHub 接口触发了限流，请稍后再试。");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(false, $"GitHub 返回异常状态：{(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
            var releaseUrl = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;

            string? assetName = null;
            string? assetUrl = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                var candidates = assets.EnumerateArray()
                    .Where(a => a.TryGetProperty("browser_download_url", out _))
                    .ToList();
                var picked = candidates.FirstOrDefault(a =>
                    (a.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : "")?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);
                if (picked.ValueKind == JsonValueKind.Undefined) picked = candidates.FirstOrDefault();
                if (picked.ValueKind == JsonValueKind.Object)
                {
                    assetName = picked.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                    assetUrl = picked.TryGetProperty("browser_download_url", out var downloadElement) ? downloadElement.GetString() : null;
                }
            }

            var normalizedCurrent = NormalizeVersion(currentVersion);
            var normalizedLatest = NormalizeVersion(tag);
            var isNewer = !string.IsNullOrEmpty(normalizedLatest) &&
                          (string.IsNullOrEmpty(normalizedCurrent) || string.CompareOrdinal(normalizedLatest, normalizedCurrent) > 0);

            var message = string.IsNullOrWhiteSpace(currentVersion)
                ? $"最新版本：{tag ?? "未知"}。"
                : isNewer
                    ? $"发现新版本：{tag}（本地：{currentVersion}）。"
                    : $"已是最新版本（本地：{currentVersion}，最新：{tag}）。";

            return new UpdateCheckResult(true, message, tag, releaseUrl, assetName, assetUrl);
        }
        catch (TaskCanceledException)
        {
            return new UpdateCheckResult(false, "请求 GitHub 超时，请检查网络。");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, $"检查更新失败：{ex.Message}");
        }
    }

    private static string NormalizeVersion(string? version) =>
        string.IsNullOrWhiteSpace(version) ? "" : version.Trim().TrimStart('v', 'V');
}
