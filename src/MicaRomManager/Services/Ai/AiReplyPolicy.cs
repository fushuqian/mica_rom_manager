namespace MicaRomManager.Services.Ai;

/// <summary>
/// 助手回复的行为策略：判断「模型在反问要不要我改」「用户问的是不是介绍」，
/// 以及补提示时使用的固定话术。
/// 这些规则以前散在多轮循环里直接硬判中文关键词，集中到这里才能单独测试、单独调整；
/// 改策略不再需要动状态机。
/// </summary>
public sealed class AiReplyPolicy
{
    /// <summary>正文短于这个长度就当作「没给正文」（介绍类提问必须先有正文再出卡片）。</summary>
    public int MinIntroBodyLength { get; init; } = 30;

    /// <summary>句尾出现这些词且以问号结束，视为在征求用户同意。</summary>
    public IReadOnlyList<string> PermissionMarkers { get; init; } = new[]
    {
        "要我", "需要我", "要不要我", "需不需要我", "是否要我", "是否需要我", "需要帮你",
    };

    /// <summary>用户这句话里出现这些词，视为在要游戏介绍。</summary>
    public IReadOnlyList<string> IntroMarkers { get; init; } = new[]
    {
        "介绍", "说说", "讲讲", "聊聊", "科普", "好不好玩", "值得玩", "是什么游戏", "怎么样",
    };

    /// <summary>被拦下反问后补的一轮提示。</summary>
    public const string PermissionNudge =
        "【系统提示】不要向我反问或征求同意。如果上面的回答涉及修改游戏、设置或发送，请立即调用对应工具生成确认卡片；否则直接给出结论，不要再问。";

    /// <summary>介绍类提问只出了卡片时，回给该 tool call 的要求。</summary>
    public const string IntroNudge =
        "{\"ok\":false,\"message\":\"先不要出卡片：用户要的是游戏介绍。请先用正文写 3–5 句介绍（玩法、剧情、年份、平台），然后在同一轮重新调用 update_games 生成后续卡片；正文不能为空，也不要把介绍写进备注。\"}";

    /// <summary>同一轮里后续未执行的 tool call 的占位响应。</summary>
    public const string NotExecutedPlaceholder =
        "{\"ok\":false,\"message\":\"未执行：等待重新生成。\"}";

    /// <summary>回答末尾是否在反问「要我…吗？」。这类反问会让用户不得不再打一次字。</summary>
    public bool IsPermissionAsk(string? text)
    {
        var trimmed = text?.TrimEnd() ?? "";
        if (trimmed.Length == 0) return false;
        if (!trimmed.EndsWith('？') && !trimmed.EndsWith('?')) return false;

        return PermissionMarkers.Any(marker => trimmed.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>把被拦下的反问句去掉，只保留前面的分析内容。</summary>
    public string StripTrailingPermissionAsk(string text)
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

    /// <summary>把拦下的正文（已去掉反问）与下一轮回复合并。</summary>
    public string MergePreface(string preface, string content)
    {
        if (preface.Length == 0) return content;

        var kept = StripTrailingPermissionAsk(preface);
        if (kept.Length == 0) return content;
        return content.Length == 0 ? kept : kept + "\n\n" + content;
    }

    /// <summary>用户这一轮是不是在要游戏介绍（这种问题必须先有正文，不能只弹卡片）。</summary>
    public bool IsIntroRequest(string? userText)
    {
        var text = userText ?? "";
        return text.Length > 0 && IntroMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal));
    }

    /// <summary>介绍类提问却只拿到卡片：正文为空或过短，且此前没拦过。</summary>
    public bool ShouldRequestIntroFirst(bool alreadyGuarded, string? preface, string? content, string? lastUserText) =>
        !alreadyGuarded
        && string.IsNullOrWhiteSpace(preface)
        && (content?.Trim().Length ?? 0) < MinIntroBodyLength
        && IsIntroRequest(lastUserText);
}
