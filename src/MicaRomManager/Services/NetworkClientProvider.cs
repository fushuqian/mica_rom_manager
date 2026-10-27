using System.Diagnostics;
using System.Net;
using System.Net.Http;
using MicaRomManager.Data;

namespace MicaRomManager.Services;

/// <summary>按设置构建带代理的 HttpClient，供 GitHub 检查更新与下载复用。</summary>
public sealed class NetworkClientProvider
{
    public const string ModeKey = "network.proxy.mode";
    public const string AddressKey = "network.proxy.address";
    public const string DefaultAddress = "127.0.0.1:7890";

    private readonly SettingsRepository _settings;
    private HttpClient _api = null!;
    private HttpClient _transfer = null!;

    public NetworkClientProvider(SettingsRepository settings)
    {
        _settings = settings;
        Rebuild();
    }

    /// <summary>用于 GitHub API 等短请求（30 秒超时）。</summary>
    public HttpClient ApiClient => _api;

    /// <summary>用于下载安装包（30 分钟超时）。</summary>
    public HttpClient TransferClient => _transfer;

    public void Rebuild()
    {
        _api?.Dispose();
        _transfer?.Dispose();
        _api = CreateClient(TimeSpan.FromSeconds(30));
        _transfer = CreateClient(TimeSpan.FromMinutes(30));
    }

    /// <summary>接受 127.0.0.1:7890 或 http://127.0.0.1:7890，端口 1–65535。</summary>
    public static bool TryParseProxyAddress(string? address, out string host, out int port)
    {
        host = "";
        port = 0;
        if (string.IsNullOrWhiteSpace(address)) return false;

        var text = address.Trim();
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) text = text[7..];
        else if (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) text = text[8..];
        text = text.TrimEnd('/');

        var separator = text.LastIndexOf(':');
        if (separator <= 0 || separator == text.Length - 1) return false;

        host = text[..separator].Trim();
        if (host.Length == 0 || host.Contains(':')) return false;

        return int.TryParse(text[(separator + 1)..].Trim(), out port) && port is >= 1 and <= 65535;
    }

    /// <summary>按当前代理设置创建 HttpClient，供 AI 等模块复用。</summary>
    public HttpClient CreateClient(TimeSpan timeout)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        };

        switch (GetMode())
        {
            case "none":
                handler.UseProxy = false;
                handler.Proxy = null;
                break;

            case "manual":
                if (TryParseProxyAddress(_settings.Get(AddressKey), out var host, out var port))
                {
                    handler.UseProxy = true;
                    handler.Proxy = new WebProxy($"http://{host}:{port}");
                }
                else
                {
                    Debug.WriteLine("代理地址无效，回退为跟随系统代理。");
                    handler.UseProxy = true;
                    handler.Proxy = null;
                }

                break;

            default: // system：UseProxy=true 且 Proxy=null 时 .NET 读取 Windows 系统代理
                handler.UseProxy = true;
                handler.Proxy = null;
                break;
        }

        var client = new HttpClient(handler) { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MicaRomManager/0.2");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        return client;
    }

    private string GetMode()
    {
        var mode = _settings.Get(ModeKey)?.Trim().ToLowerInvariant();
        return mode is "none" or "manual" ? mode : "system";
    }
}
