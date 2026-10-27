namespace MicaRomManager.Services.Ai;

public sealed record AiTurnResult(string Text, AiPendingAction? Pending, string Error);

/// <summary>助手多轮工具调用循环；写操作返回 Pending，用户确认后再继续。</summary>
public sealed class AiAssistantService
{
    private const int MaxRounds = 5;
    private const int MaxHistory = 20;

    private readonly IAiChatClient _client;
    private readonly AiToolExecutor _executor = new();
    private readonly List<AiChatMessage> _history = new();
    private string? _pendingToolCallId;

    public AiAssistantService(IAiChatClient? client = null) => _client = client ?? AppServices.Ai;

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

    public void CancelPending()
    {
        if (_pendingToolCallId == null) return;
        AddHistory(new AiChatMessage
        {
            Role = "tool",
            ToolCallId = _pendingToolCallId,
            Content = "{\"ok\":false,\"message\":\"用户取消了该操作，不要执行。\"}",
        });
        _pendingToolCallId = null;
    }

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
                if (!nudged && IsPermissionAsk(content))
                {
                    nudged = true;
                    preface = content;
                    AddHistory(new AiChatMessage
                    {
                        Role = "user",
                        Content = "【系统提示】不要向我反问或征求同意。如果上面的回答涉及修改游戏、设置或发送，请立即调用对应工具生成确认卡片；否则直接给出结论，不要再问。",
                    });
                    continue;
                }

                return new AiTurnResult(MergePreface(preface, content), null, "");
            }

            var restart = false;
            for (var index = 0; index < calls.Count; index++)
            {
                var call = calls[index];
                var execution = await _executor.ExecuteAsync(call, host, token);

                if (execution.Pending != null)
                {
                    // 用户要的是介绍，但模型只甩了张卡片：让它先补介绍正文，再重新出卡片。
                    if (!introGuarded && preface.Length == 0 && content.Length < 30
                        && IsIntroRequest(FindLastUserText()))
                    {
                        introGuarded = true;
                        for (var rest = index; rest < calls.Count; rest++)
                        {
                            AddHistory(new AiChatMessage
                            {
                                Role = "tool",
                                ToolCallId = calls[rest].Id,
                                Content = rest == index
                                    ? "{\"ok\":false,\"message\":\"先不要出卡片：用户要的是游戏介绍。请先用正文写 3–5 句介绍（玩法、剧情、年份、平台），然后在同一轮重新调用 update_games 生成后续卡片；正文不能为空，也不要把介绍写进备注。\"}"
                                    : "{\"ok\":false,\"message\":\"未执行：等待重新生成。\"}",
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

                    return new AiTurnResult(MergePreface(preface, content), execution.Pending, "");
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

    private static readonly string[] PermissionMarkers =
    {
        "要我", "需要我", "要不要我", "需不需要我", "是否要我", "是否需要我", "需要帮你",
    };

    /// <summary>回答末尾是否在反问「要我…吗？」，这类反问会让用户不得不再打一次字。</summary>
    private static bool IsPermissionAsk(string text)
    {
        var trimmed = text.TrimEnd();
        if (trimmed.Length == 0) return false;
        if (!trimmed.EndsWith('？') && !trimmed.EndsWith('?')) return false;

        return PermissionMarkers.Any(marker => trimmed.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>把被拦下的反问句去掉，保留前面的分析内容，再与下一轮回复合并。</summary>
    private static string MergePreface(string preface, string content)
    {
        if (preface.Length == 0) return content;

        var kept = StripTrailingPermissionAsk(preface);
        if (kept.Length == 0) return content;
        return content.Length == 0 ? kept : kept + "\n\n" + content;
    }

    private static string StripTrailingPermissionAsk(string text)
    {
        var trimmed = text.TrimEnd();
        if (!IsPermissionAsk(trimmed)) return trimmed;

        var cut = -1;
        for (var index = trimmed.Length - 2; index >= 0; index--)
        {
            if ("。！？!?；;\n".IndexOf(trimmed[index]) >= 0)
            {
                cut = index;
                break;
            }
        }

        if (cut < 0) return "";
        var tail = trimmed[(cut + 1)..].Trim();
        return IsPermissionAsk(tail) ? trimmed[..cut].TrimEnd() : trimmed;
    }

    private static readonly string[] IntroMarkers =
    {
        "介绍", "说说", "讲讲", "聊聊", "科普", "好不好玩", "值得玩", "是什么游戏", "怎么样",
    };

    /// <summary>用户这一轮是不是在要游戏介绍（这种问题必须先有正文，不能只弹卡片）。</summary>
    private static bool IsIntroRequest(string text) =>
        text.Length > 0 && IntroMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));

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
