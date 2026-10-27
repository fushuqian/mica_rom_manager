using System.Text.Json.Serialization;

namespace MicaRomManager.Services.Ai;

/// <summary>OpenAI 兼容对话消息；assistant 消息可携带 tool_calls。</summary>
public sealed class AiChatMessage
{
    [JsonPropertyName("role")] public string Role { get; init; } = "user";
    [JsonPropertyName("content")] public string? Content { get; init; }
    [JsonPropertyName("tool_call_id")] public string? ToolCallId { get; init; }
    [JsonPropertyName("tool_calls")] public List<AiToolCall>? ToolCalls { get; init; }
}

public sealed class AiToolCall
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "function";
    [JsonPropertyName("function")] public AiFunctionCall Function { get; init; } = new();

    [JsonIgnore] public string Name => Function.Name;
    [JsonIgnore] public string ArgumentsJson => Function.Arguments;
}

public sealed class AiFunctionCall
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("arguments")] public string Arguments { get; set; } = "{}";
}

public sealed class AiToolDefinition
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public object Parameters { get; init; } = new { type = "object", properties = new { } };
}

public sealed record AiToolChatResult(
    bool Success,
    string Content,
    string Error,
    List<AiToolCall> ToolCalls,
    int StatusCode,
    TimeSpan Elapsed);

/// <summary>便于助手服务注入假客户端做单元测试。</summary>
public interface IAiChatClient
{
    AiOptions Options { get; }

    Task<AiToolChatResult> CompleteWithToolsAsync(
        IReadOnlyList<AiChatMessage> messages,
        IReadOnlyList<AiToolDefinition> tools,
        CancellationToken token);
}
