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
                return new AiTurnResult(content, null, "");

            for (var index = 0; index < calls.Count; index++)
            {
                var call = calls[index];
                var execution = await _executor.ExecuteAsync(call, host, token);

                if (execution.Pending != null)
                {
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

                    return new AiTurnResult(content, execution.Pending, "");
                }

                AddHistory(new AiChatMessage
                {
                    Role = "tool",
                    ToolCallId = execution.ToolCallId,
                    Content = execution.ResultForModel,
                });
            }
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

    private static string BuildSystemPrompt(IAiAppHost host)
    {
        var selected = host.SelectedGame == null
            ? "无"
            : $"「{host.SelectedGame.Title}」（{host.SelectedGame.Platform}，{host.SelectedGame.Status}）";
        var device = string.IsNullOrWhiteSpace(host.ConnectedDevice) ? "未连接" : host.ConnectedDevice;

        return $$"""
            你是 Mica ROM Manager 的内置 AI 助手，帮助用户管理本地模拟器游戏库。

            当前状态：
            - 库内游戏：{{host.AllGames.Count}} 款
            - 当前选中：{{selected}}
            - 当前筛选结果：{{host.FilteredGames.Count}} 款
            - 已连接设备：{{device}}

            规则：
            1. 始终用简体中文回答，语气简洁直接。
            2. 需要读取游戏库或操作应用时，必须调用提供的工具，不要只口头答应。
            3. 所有写操作（改元数据、改设置、启动、发送、删除、保存设备配置）都会生成确认卡片；在用户确认前，不得声称已经执行。
            4. 删除类操作必须先说明后果：只删记录，还是把文件移到回收站。
            5. 游戏咨询可以结合 get_game 返回的元数据和你的知识回答「好不好玩/介绍一下/这是啥游戏」；不确定的信息要明确说明，不要编造年份、发行商或开发商。
            6. 不确定游戏 id 时，先用 query_games 查询，不要猜。
            7. 永远不要读取、输出或索要 API Key。
            8. 回答保持简短；需要展示多个游戏时最多列 10 条。
            """;
    }
}
