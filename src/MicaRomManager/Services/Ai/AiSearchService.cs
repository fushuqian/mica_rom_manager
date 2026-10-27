using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace MicaRomManager.Services.Ai;

public sealed record WebSearchResult(
    string Title,
    string Url,
    string Snippet,
    string Summary,
    string SiteName,
    string Date);

public sealed record WebSearchResponse(bool Success, List<WebSearchResult> Results, string Error);

/// <summary>调用 DeepSeek 官方 Search API；复用 AI Key，按成功请求计费。</summary>
public sealed class AiSearchService
{
    private const string Endpoint = "https://api.deepseek.com/v1/tools/search";

    public AiSearchOptions Options => AiSearchOptions.Load(AppServices.Settings);

    public bool CanSearch
    {
        get
        {
            var options = Options;
            var ai = AiOptions.Load(AppServices.Settings);
            return options.Enabled && ai.IsConfigured && AiSearchOptions.IsDeepSeekEndpoint(ai.BaseUrl);
        }
    }

    public async Task<WebSearchResponse> SearchAsync(string query, int? maxResults, CancellationToken token)
    {
        var options = Options;
        if (!options.Enabled)
            return Fail("还没有启用联网搜索，请到 设置 → AI 打开「允许联网搜索」。");

        var ai = AiOptions.Load(AppServices.Settings);
        if (!ai.IsConfigured)
            return Fail("还没有配置 AI，无法使用 DeepSeek 搜索。");
        if (!AiSearchOptions.IsDeepSeekEndpoint(ai.BaseUrl))
            return Fail("联网搜索目前使用 DeepSeek 官方 Search API，请把 BaseUrl 改为 https://api.deepseek.com/v1。");

        var trimmed = (query ?? "").Trim();
        if (trimmed.Length == 0) return Fail("搜索关键词为空。");
        if (trimmed.Length > 128) trimmed = trimmed[..128];

        var count = Math.Clamp(maxResults ?? options.MaxResults, 1, 10);
        var body = JsonSerializer.Serialize(new
        {
            query = trimmed,
            count,
            need_content = false,
        });

        using var client = AppServices.Network.CreateClient(Timeout.InfiniteTimeSpan);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ai.ApiKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            AiLog.Write($"SEARCH HTTP {(int)response.StatusCode} resultsHost=api.deepseek.com");

            if (!response.IsSuccessStatusCode)
                return Fail(DescribeError(response.StatusCode, text));

            var results = ParseResults(text);
            return new WebSearchResponse(true, results, "");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Fail("搜索已取消。");
        }
        catch (OperationCanceledException)
        {
            return Fail("搜索超时（30 秒），请检查网络或代理。");
        }
        catch (Exception ex)
        {
            AiLog.Write($"SEARCH 异常 type={ex.GetType().Name}");
            return Fail($"搜索失败：{ex.Message}");
        }
    }

    public static List<WebSearchResult> ParseResults(string json)
    {
        var results = new List<WebSearchResult>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("results", out var array)
                || array.ValueKind != JsonValueKind.Array) return results;

            foreach (var item in array.EnumerateArray())
            {
                results.Add(new WebSearchResult(
                    GetString(item, "title"),
                    GetString(item, "url"),
                    GetString(item, "snippet"),
                    GetString(item, "summary"),
                    GetString(item, "site_name"),
                    GetString(item, "date")));
            }
        }
        catch
        {
            // 非法响应由上层按空结果处理
        }

        return results;
    }

    /// <summary>把搜索结果压成给模型读的短文本。</summary>
    public static string BuildContext(IReadOnlyList<WebSearchResult> results, int maxChars = 3200)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < results.Count; index++)
        {
            var result = results[index];
            var body = result.Summary.Length > 0 ? result.Summary : result.Snippet;
            var line = $"{index + 1}. {result.Title}"
                       + (result.SiteName.Length == 0 ? "" : $"（{result.SiteName}）")
                       + (result.Date.Length == 0 ? "" : $" {result.Date[..Math.Min(10, result.Date.Length)]}")
                       + $"\n{body}\n";
            if (builder.Length + line.Length > maxChars) break;
            builder.AppendLine(line);
        }

        return builder.ToString().Trim();
    }

    private static string GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return "";
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim() ?? "",
            JsonValueKind.Number => value.ToString(),
            _ => "",
        };
    }

    private static string DescribeError(HttpStatusCode status, string body)
    {
        var detail = "";
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
                detail = message.GetString() ?? "";
        }
        catch
        {
            detail = AiJson.Snippet(body, 120);
        }

        var reason = status switch
        {
            HttpStatusCode.Unauthorized => "API Key 无效",
            HttpStatusCode.PaymentRequired => "DeepSeek 账户余额不足",
            HttpStatusCode.TooManyRequests => "搜索请求过于频繁（QPS 超限）",
            HttpStatusCode.BadRequest => "搜索参数不被接受",
            _ => $"搜索返回 HTTP {(int)status}",
        };

        return detail.Length > 0 ? $"{reason}：{detail}" : reason;
    }

    private static WebSearchResponse Fail(string error) => new(false, new List<WebSearchResult>(), error);
}
