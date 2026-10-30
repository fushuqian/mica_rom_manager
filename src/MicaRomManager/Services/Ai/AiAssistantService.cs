namespace MicaRomManager.Services.Ai;

using System.Text.Encodings.Web;
using System.Text.Json;

public sealed record AiTurnResult(string Text, AiPendingAction? Pending, string Error);

/// <summary>助手多轮工具调用循环；写操作返回 Pending，用户确认后再继续。</summary>
public sealed class AiAssistantService
{
    private const int MaxRounds = 5;
    private const int MaxHistory = 20;

    private readonly IAiChatClient _client;
    private readonly IAiToolExecutor _executor;
    private readonly AiReplyPolicy _policy;
    private readonly List<AiChatMessage> _history = new();
    private string? _pendingToolCallId;

    public AiAssistantService(
        IAiChatClient? client = null,
        IAiToolExecutor? executor = null,
        AiReplyPolicy? policy = null)
    {
        _client = client ?? AppServices.Ai;
        _executor = executor ?? new AiToolExecutor();
        _policy = policy ?? new AiReplyPolicy();
    }

    public bool HasPending => _pendingToolCallId != null;

    public void Reset()
    {
        _history.Clear();
        _pendingToolCallId = null;
    }

    public async Task<AiTurnResult> SendAsync(string userText, IAiAppHost host, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(userText))
            return new AiTurnResult("", null, "请输入内容。");
        if (!_client.Options.IsConfigured)
            return new AiTurnResult("", null, "还没有配置 AI，请先在 设置 → AI 里填写 BaseUrl、API Key 和模型。");
        if (HasPending)
            return new AiTurnResult("", null, "还有一个待确认操作，请先确认或取消。");

        AddHistory(new AiChatMessage { Role = "user", Content = userText.Trim() });
        return await RunLoopAsync(host, token);
    }

    public async Task<AiTurnResult> CompletePendingAsync(string executionResult,
        IAiAppHost host, CancellationToken token)
    {
        if (_pendingToolCallId == null)
            return new AiTurnResult("", null, "没有待确认操作。");

        AddHistory(new AiChatMessage
        {
            Role = "tool",
            ToolCallId = _pendingToolCallId,
            Content = executionResult,
        });
        _pendingToolCallId = null;
        return await RunLoopAsync(host, token);
    }

    /// <summary>启动推荐等无需模型继续总结的操作：只回填 tool 结果，不再发起一轮请求。</summary>
    public void ResolvePending(string executionResult)
    {
        if (_pendingToolCallId == null) return;

        AddHistory(new AiChatMessage
        {
            Role = "tool",
            ToolCallId = _pendingToolCallId,
            Content = executionResult,
        });
        _pendingToolCallId = null;
    }

    public void CancelPending(string reason = "用户取消了该操作，不要执行。")
    {
        if (_pendingToolCallId == null) return;
        AddHistory(ToolResult(_pendingToolCallId, reason));
        _pendingToolCallId = null;
    }

    /// <summary>构造一条合法的 tool 响应消息（必须带 tool_call_id）。</summary>
    private static AiChatMessage ToolResult(string toolCallId, string message) => new()
    {
        Role = "tool",
        ToolCallId = toolCallId,
        Content = JsonSerializer.Serialize(new { ok = false, message }, ToolJsonOptions),
    };

    private static readonly JsonSerializerOptions ToolJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private async Task<AiTurnResult> RunLoopAsync(IAiAppHost host, CancellationToken token)
    {
        var nudged = false;
        var introGuarded = false;
        var preface = "";
        for (var round = 0; round < MaxRounds; round++)
        {
            token.ThrowIfCancellationRequested();

            var messages = new List<AiChatMessage>
            {
                new() { Role = "system", Content = BuildSystemPrompt(host) },
            };
            messages.AddRange(_history);

            var response = await _client.CompleteWithToolsAsync(messages, AiToolRegistry.All, token);
            if (!response.Success)
                return new AiTurnResult("", null, response.Error);

            var content = response.Content?.Trim() ?? "";
            var calls = response.ToolCalls;
            AddHistory(new AiChatMessage
            {
                Role = "assistant",
                Content = content.Length == 0 ? null : content,
                ToolCalls = calls.Count == 0 ? null : calls,
            });

            if (calls.Count == 0)
            {
                // 模型又在文字里反问「要我改吗？」：补一轮提示，逼它直接出卡片，避免用户打字回答两遍。
                if (!nudged && _policy.IsPermissionAsk(content))
                {
                    nudged = true;
                    preface = content;
                    AiLog.Write("policy nudge=permission");
                    AddHistory(new AiChatMessage { Role = "user", Content = AiReplyPolicy.PermissionNudge });
                    continue;
                }

                return new AiTurnResult(_policy.MergePreface(preface, content), null, "");
            }

            var restart = false;
            for (var index = 0; index < calls.Count; index++)
            {
                var call = calls[index];
                AiToolExecution execution;
                try
                {
                    execution = await _executor.ExecuteAsync(call, host, token);
                }
                catch (OperationCanceledException)
                {
                    // 取消发生在工具执行中：先补齐本轮所有 call 的 tool 响应，
                    // 否则历史里会留下「有 tool_calls、无 tool 响应」的非法消息，下一次请求必被端点拒绝。
                    for (var rest = index; rest < calls.Count; rest++)
                        AddHistory(ToolResult(calls[rest].Id, "已取消：用户中止了本轮操作。"));

                    throw;
                }

                if (execution.Pending != null)
                {
                    // 用户要的是介绍，但模型只甩了张卡片：让它先补介绍正文，再重新出卡片。
                    if (_policy.ShouldRequestIntroFirst(introGuarded, preface, content, FindLastUserText()))
                    {
                        introGuarded = true;
                        AiLog.Write($"policy nudge=intro tool={call.Name}");
                        for (var rest = index; rest < calls.Count; rest++)
                        {
                            AddHistory(new AiChatMessage
                            {
                                Role = "tool",
                                ToolCallId = calls[rest].Id,
                                Content = rest == index
                                    ? AiReplyPolicy.IntroNudge
                                    : AiReplyPolicy.NotExecutedPlaceholder,
                            });
                        }

                        restart = true;
                        break;
                    }

                    _pendingToolCallId = call.Id;
                    for (var rest = index + 1; rest < calls.Count; rest++)
                    {
                        AddHistory(new AiChatMessage
                        {
                            Role = "tool",
                            ToolCallId = calls[rest].Id,
                            Content = "{\"ok\":false,\"message\":\"未执行：等待用户确认前一个操作。\"}",
                        });
                    }

                    return new AiTurnResult(_policy.MergePreface(preface, content), execution.Pending, "");
                }

                AddHistory(new AiChatMessage
                {
                    Role = "tool",
                    ToolCallId = execution.ToolCallId,
                    Content = execution.ResultForModel,
                });
            }

            if (restart) continue;
        }

        return new AiTurnResult("", null, "步骤过多，请把需求拆成更具体的问题。");
    }

    private void AddHistory(AiChatMessage message)
    {
        _history.Add(message);
        if (_history.Count <= MaxHistory) return;

        var cut = _history.Count - MaxHistory;
        var firstUser = _history.FindIndex(cut, item => item.Role == "user");
        if (firstUser > 0) _history.RemoveRange(0, firstUser);
    }

    private string FindLastUserText()
    {
        for (var index = _history.Count - 1; index >= 0; index--)
        {
            var item = _history[index];
            if (item.Role != "user") continue;

            var text = item.Content ?? "";
            if (text.StartsWith("【系统提示】", StringComparison.Ordinal)) continue;
            return text;
        }

        return "";
    }

    private static string BuildSystemPrompt(IAiAppHost host)
    {
        var selected = host.SelectedGame == null
            ? "无"
            : $"「{host.SelectedGame.Title}」（{host.SelectedGame.Platform}，{host.SelectedGame.Status}）";
        var device = string.IsNullOrWhiteSpace(host.ConnectedDevice) ? "未连接" : host.ConnectedDevice;
        var platforms = string.Join("、", host.AllGames
            .Where(game => !string.IsNullOrWhiteSpace(game.Platform))
            .GroupBy(game => game.Platform)
            .OrderByDescending(group => group.Count())
            .Take(10)
            .Select(group => $"{group.Key}({group.Count()})"));

        return AiPromptCatalog.RenderSystem(AiPromptCatalog.AssistantId, new Dictionary<string, string>
        {
            ["GameCount"] = host.AllGames.Count.ToString(),
            ["Platforms"] = platforms.Length == 0 ? "暂无" : platforms,
            ["Selected"] = selected,
            ["FilteredCount"] = host.FilteredGames.Count.ToString(),
            ["Device"] = device,
        });
    }
}
