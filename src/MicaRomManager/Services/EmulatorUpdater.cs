using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MicaRomManager.Services;

public sealed record UpdateCheckResult(
    bool Success,
    string Message,
    string? LatestTag = null,
    string? ReleaseUrl = null,
    string? AssetName = null,
    string? AssetUrl = null,
    string? LatestVersion = null,
    bool IsNewer = false);

public sealed class EmulatorUpdater
{
    private readonly NetworkClientProvider _network;

    public EmulatorUpdater(NetworkClientProvider network) => _network = network;

    /// <summary>调用 GitHub 公共 Release API（免费、无需 Token，未认证 60 次/小时）。</summary>
    public async Task<UpdateCheckResult> CheckAsync(string repository, string currentVersion)
    {
        var repo = NormalizeRepository(repository);
        if (repo.Length == 0)
        {
            return new UpdateCheckResult(false, string.IsNullOrWhiteSpace(repository)
                ? "未配置 GitHub 仓库（格式：owner/repo），无法检查更新。"
                : "GitHub 仓库格式不正确，应为 owner/repo。");
        }

        var parts = repo.Split('/', StringSplitOptions.RemoveEmptyEntries);
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

            DateTimeOffset? publishedAt = null;
            if (root.TryGetProperty("published_at", out var publishedElement) &&
                DateTimeOffset.TryParse(publishedElement.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var parsedPublished))
            {
                publishedAt = parsedPublished;
            }

            string? assetName = null;
            string? assetUrl = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                var candidates = assets.EnumerateArray()
                    .Select(a => (
                        Name: a.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null,
                        Url: a.TryGetProperty("browser_download_url", out var downloadElement) ? downloadElement.GetString() : null))
                    .Where(a => !string.IsNullOrWhiteSpace(a.Url))
                    .ToList();

                var picked = PickWindowsAsset(candidates);
                if (picked != null)
                {
                    assetName = picked.Value.Name;
                    assetUrl = picked.Value.Url;
                }
            }

            var displayVersion = ResolveVersion(tag, assetName, publishedAt);
            var comparisonVersion = displayVersion.Length > 0 ? displayVersion : tag ?? "";
            var isNewer = IsNewer(comparisonVersion, currentVersion);
            var messageVersion = displayVersion.Length > 0 ? displayVersion : tag ?? "未知";

            var message = string.IsNullOrWhiteSpace(currentVersion)
                ? $"最新版本：{messageVersion}。"
                : isNewer
                    ? $"发现新版本：{messageVersion}（本地：{currentVersion}）。"
                    : $"已是最新版本（本地：{currentVersion}，最新：{messageVersion}）。";

            return new UpdateCheckResult(true, message, tag, releaseUrl, assetName, assetUrl,
                displayVersion, isNewer);
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

    /// <summary>
    /// 把用户输入统一成 owner/repo：兼容完整 GitHub 网址、/releases 后缀与 .git 后缀。
    /// 允许留空（返回空串）；格式不合法也返回空串，由调用方给出中文提示。
    /// </summary>
    public static string NormalizeRepository(string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository)) return "";

        var text = repository.Trim();
        const string host = "github.com/";
        var index = text.IndexOf(host, StringComparison.OrdinalIgnoreCase);
        if (index >= 0) text = text[(index + host.Length)..];

        foreach (var suffix in new[] { "/releases/latest", "/releases", "/tags" })
        {
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                text = text[..^suffix.Length];
        }

        text = text.Trim().TrimEnd('/');
        if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) text = text[..^4];

        var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return "";

        foreach (var part in parts)
        {
            if (part.Length == 0) return "";
            if (part.Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '_' or '.'))) return "";
        }

        return $"{parts[0]}/{parts[1]}";
    }

    /// <summary>Release tag 转版本号文本（去掉前导 v/V）；空 tag 返回空串。</summary>
    public static string VersionFromTag(string? tag) =>
        string.IsNullOrWhiteSpace(tag) ? "" : tag.Trim().TrimStart('v', 'V');

    /// <summary>
    /// 从资产文件名里提取版本号。例如 RPCS3 的
    /// rpcs3-v0.0.43-20511-9508444d_win64_msvc.7z → 0.0.43-20511。
    /// 没有点分数字时返回空串。
    /// </summary>
    public static string VersionFromAssetName(string? assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName)) return "";

        // 去掉压缩包扩展名，避免把 RetroArch-Win64.7z 里的 “64.7” 误当成版本号。
        var name = Regex.Replace(assetName.Trim(), @"\.(zip|7z|rar|tar\.gz|tgz)$", "",
            RegexOptions.IgnoreCase);

        var match = Regex.Match(name, @"[vV]?(\d+(?:\.\d+)+(?:-\d+)?)");
        if (!match.Success) return "";

        return match.Groups[1].Value.Trim('-', '_', '.');
    }

    /// <summary>
    /// 写回与比较用的版本：tag 本身是版本号时用 tag；否则从资产名提取；
    /// 两者都没有时退回发布日期，避免把 "latest" 之类的标签写进版本号。
    /// </summary>
    public static string ResolveVersion(string? tag, string? assetName, DateTimeOffset? publishedAt = null)
    {
        var tagVersion = VersionFromTag(tag);
        if (LooksLikeVersion(tagVersion)) return tagVersion;

        var assetVersion = VersionFromAssetName(assetName);
        if (assetVersion.Length > 0) return assetVersion;

        return publishedAt?.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture) ?? "";
    }

    private static bool LooksLikeVersion(string? value) =>
        !string.IsNullOrWhiteSpace(value) && char.IsDigit(value[0]);

    private static string NormalizeVersion(string? version) => VersionFromTag(version);

    /// <summary>明显不是 Windows 可执行包的关键字：符号表、源码、其它平台架构。</summary>
    private static readonly string[] NonWindowsAssetTokens =
    {
        "symbols", "pdb", "dbg", "debug", "source", "arm64", "armhf", "aarch64", "armv7",
        "macos", "mac-", "-mac", "osx", "darwin", "linux", "appimage", "flatpak", "ubuntu",
        "android", "ios", "ipa", "switch", "vita", "wii", "3ds", "bsd",
    };

    /// <summary>
    /// 从 Release 资产里挑 Windows x64 包：
    /// 先排除符号表 / 源码 / ARM / macOS / Linux 资产，再优先 windows + x64 + release 的 zip/7z。
    /// 没有 Windows 标记时退回 zip 优先；一个可用压缩包都没有则返回 null（由界面提示打开官网）。
    /// </summary>
    internal static (string? Name, string? Url)? PickWindowsAsset(
        IReadOnlyList<(string? Name, string? Url)> assets)
    {
        var archives = assets
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Url))
            .Where(a => IsArchive(a.Name!))
            .Where(a => !NonWindowsAssetTokens.Any(token =>
                a.Name!.Contains(token, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (archives.Count == 0) return null;

        var windows = archives.Where(a => IsWindowsAsset(a.Name!)).ToList();
        var windows64 = windows.Where(a => Is64BitAsset(a.Name!)).ToList();

        var picked = windows64
            .OrderByDescending(a => ScoreAsset(a.Name!))
            .FirstOrDefault();
        if (picked.Name != null) return picked;

        picked = windows
            .OrderByDescending(a => ScoreAsset(a.Name!))
            .FirstOrDefault();
        if (picked.Name != null) return picked;

        // 有些仓库的 Windows 包名里没有 windows/win 标记，退回原来的 zip 优先策略。
        var zip = archives
            .OrderByDescending(a => ScoreAsset(a.Name!))
            .FirstOrDefault(a => a.Name!.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (zip.Name != null) return zip;

        return archives.OrderByDescending(a => ScoreAsset(a.Name!)).First();
    }

    private static bool IsArchive(string name) =>
        name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase);

    private static bool IsWindowsAsset(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.Contains("windows") || lower.Contains("win64") || lower.Contains("win32") ||
               lower.Contains("-win") || lower.Contains("_win") ||
               lower.Contains("win-") || lower.Contains("win_");
    }

    private static bool Is64BitAsset(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.Contains("x64") || lower.Contains("win64") || lower.Contains("amd64") ||
               lower.Contains("x86_64") || lower.Contains("x86-64") ||
               lower.Contains("64bit") || lower.Contains("64-bit");
    }

    private static int ScoreAsset(string name)
    {
        var lower = name.ToLowerInvariant();
        var score = 0;
        if (IsWindowsAsset(name)) score += 20;
        if (Is64BitAsset(name)) score += 20;
        if (lower.Contains("release")) score += 5;
        if (lower.EndsWith(".zip")) score += 3;
        else if (lower.EndsWith(".7z")) score += 2;
        if (lower.Contains("sse2")) score -= 2;
        if (lower.Contains("avx2")) score -= 3;
        if (lower.Contains("qtsdl")) score -= 1;
        if (lower.Contains("mxe")) score += 2;
        if (lower.Contains("msvc")) score -= 1;
        return score;
    }

    /// <summary>
    /// latest 是否比 current 新。按数字段比较，避免 "0.9" 与 "0.10" 的字典序误判
    /// （原实现用 CompareOrdinal，会把 0.9 判成比 0.10 新）。
    /// </summary>
    internal static bool IsNewer(string? latest, string? current)
    {
        var left = ParseVersion(latest);
        var right = ParseVersion(current);

        // latest 能解析成数字版本、current 不能（例如旧的 build-<hash>）时，
        // 视为有更新：让用户装一次，把版本号顺便修正成可比较的形式。
        if (left != null) return right == null || left > right;

        // latest 不是数字版本、current 是数字版本时不提示更新，避免 nightly/build 标签永远「更新」。
        if (right != null) return false;

        var leftText = NormalizeVersion(latest);
        if (leftText.Length == 0) return false;
        var rightText = NormalizeVersion(current);
        if (rightText.Length == 0) return true;
        return string.CompareOrdinal(leftText, rightText) > 0;
    }

    private static Version? ParseVersion(string? value)
    {
        var text = NormalizeVersion(value);
        if (text.Length == 0) return null;

        var parts = text.Split(new[] { '.', '-', '+', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        var numbers = new int[4];
        for (var index = 0; index < parts.Length && index < 4; index++)
        {
            var digits = new string(parts[index].TakeWhile(char.IsDigit).ToArray());
            if (digits.Length == 0)
            {
                if (index == 0) return null;
                break;
            }

            if (!int.TryParse(digits, out var parsed)) return null;
            numbers[index] = parsed;
        }

        return new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
    }
}
