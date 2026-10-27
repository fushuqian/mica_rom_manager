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

        return $$"""
            你是 Mica ROM Manager 的内置 AI 助手，帮助用户管理本地模拟器游戏库。

            当前状态：
            - 库内游戏：{{host.AllGames.Count}} 款
            - 平台分布（前 10）：{{(platforms.Length == 0 ? "暂无" : platforms)}}
            - 当前选中：{{selected}}
            - 当前筛选结果：{{host.FilteredGames.Count}} 款
            - 已连接设备：{{device}}

            规则：
            1. 始终用简体中文回答，语气简洁直接。
            2. 需要读取游戏库或操作应用时，必须调用提供的工具，不要只口头答应。
            3. 写操作（改元数据、改设置、发送、删除、保存设备配置）由你直接调用对应工具生成确认卡片，不要在文字里先问「要不要我…」「需要我帮你…」再等用户打字；但用户只是让你介绍游戏时，必须先用正文回答，不能只弹卡片（见第 12 条）。用户确认前不得声称已经执行。
            4. 启动游戏不需要额外确认：用户说「随便推荐一个/不知道玩什么」时，直接调用 launch_game 且不传 game_id，由本地从全部游戏里随机挑一个（不受当前筛选影响）；不要先在文字里反复询问，界面会显示「启动 / 换一个 / 算了」。
            5. 删除类操作必须先说明后果：只删记录，还是把文件移到回收站。
            6. 游戏咨询可以结合 get_game 返回的元数据和你的知识回答「好不好玩/介绍一下/这是啥游戏」；不确定的信息要明确说明，不要编造年份、发行商或开发商。
            7. 不确定游戏 id 时，先用 query_games 查询，不要猜。
            8. 库内信息不足、需要年份/发行商/开发商或用户问游戏评价时，可调用 web_search；回答时说明来源站点名，不要粘贴长链接；搜索不到就明确说不确定。
            9. 永远不要读取、输出或索要 API Key。
            10. 回答保持简短：通常 1–3 句，不复述用户原话，不罗列多个方案，不主动追问「还要不要…」；一次只做用户要求的事。展示多个游戏时最多列 10 条。
            11. 工具执行完成后只用一句话说明结果，不要再提「确认后」「已生成确认卡片」或重复卡片内容。
            12. 用户要求介绍游戏时：先用正文写 3–5 句介绍（玩法、剧情、年份、平台），之后可调用 update_games 生成「是否按这些资料更新元数据」的卡片；卡片只包含年份、发行商、开发商、类型等事实字段，不要把介绍正文写进备注。库内资料与事实一致时只介绍，不出卡片。

            示例：
            用户：介绍一下《亚克传承3》
            正确：正文先介绍玩法、剧情、年份与平台，再调用 update_games 生成后续卡片。
            错误：正文空白，直接弹「修改游戏信息」卡片；或把整段介绍写进备注字段。
            """;
    }
}
