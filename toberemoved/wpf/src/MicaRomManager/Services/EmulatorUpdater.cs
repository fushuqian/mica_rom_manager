using System.IO;
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
    private readonly NetworkClientProvider _network;

    public EmulatorUpdater(NetworkClientProvider network) => _network = network;

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
            using var response = await _network.ApiClient.GetAsync(url);
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
                    .Select(a => new
                    {
                        Name = a.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null,
                        Url = a.TryGetProperty("browser_download_url", out var downloadElement) ? downloadElement.GetString() : null,
                    })
                    .Where(a => !string.IsNullOrWhiteSpace(a.Url))
                    .ToList();

                // 优先 Windows 版压缩包（zip 优先于 7z），其它格式不自动安装
                var archives = candidates
                    .Where(a => a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true ||
                                a.Name?.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();
                var picked =
                    archives.FirstOrDefault(a => a.Name!.Contains("win64", StringComparison.OrdinalIgnoreCase)) ??
                    archives.FirstOrDefault(a => a.Name!.Contains("windows", StringComparison.OrdinalIgnoreCase)) ??
                    archives.FirstOrDefault(a => a.Name!.Contains("-win", StringComparison.OrdinalIgnoreCase)) ??
                    archives.FirstOrDefault(a => a.Name!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ??
                    archives.FirstOrDefault() ??
                    candidates.FirstOrDefault();
                if (picked != null)
                {
                    assetName = picked.Name;
                    assetUrl = picked.Url;
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
            return new UpdateCheckResult(false, "请求 GitHub 超时，请检查网络或设置代理。");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(false, $"检查更新失败：{ex.Message}");
        }
    }

    /// <summary>把 Release 资产流式下载到本地，先写 .part 再改名，避免留下半截文件。</summary>
    public async Task<(bool Success, string Message, long Bytes)> DownloadAsync(
        string url, string targetFile, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var partFile = targetFile + ".part";
        try
        {
            var directory = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            using var response = await _network.TransferClient.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"下载失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}", 0);
            }

            var total = response.Content.Headers.ContentLength ?? -1;
            if (total > 0) progress?.Report(0);
            else progress?.Report(-1);

            long received = 0;
            var buffer = new byte[81920];
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = File.Create(partFile))
            {
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    if (total > 0) progress?.Report(received * 100d / total);
                }
            }

            File.Move(partFile, targetFile, true);
            return (true, $"下载完成（{FormatSize(received)}）。", received);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, "下载超时，请检查网络或设置代理。", 0);
        }
        catch (Exception ex)
        {
            return (false, $"下载失败：{ex.Message}", 0);
        }
        finally
        {
            TryDeleteFile(partFile);
        }
    }

    private static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.0} MB" : $"{bytes / 1024d:0.0} KB";

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 临时文件清理失败可忽略
        }
    }

    private static string NormalizeVersion(string? version) =>
        string.IsNullOrWhiteSpace(version) ? "" : version.Trim().TrimStart('v', 'V');
}
