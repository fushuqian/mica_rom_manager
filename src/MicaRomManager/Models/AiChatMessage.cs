using CommunityToolkit.Mvvm.ComponentModel;
using MicaRomManager.Services.Ai;

namespace MicaRomManager.Models;

public enum AiChatKind
{
    User,
    Assistant,
    Tool,
    Error,
    Pending,
}

/// <summary>聊天窗口里的展示消息；Pending 类型携带确认卡片。</summary>
public partial class AiChatMessage : ObservableObject
{
    public AiChatKind Kind { get; init; }
    public DateTime Time { get; init; } = DateTime.Now;

    [ObservableProperty] private AiPendingAction? pending;
    [ObservableProperty] private string text = "";
    [ObservableProperty] private bool isResolved;

    public bool IsUser => Kind == AiChatKind.User;
    public bool IsAssistant => Kind == AiChatKind.Assistant;
    public bool IsTool => Kind == AiChatKind.Tool;
    public bool IsError => Kind == AiChatKind.Error;
    public bool IsPending => Kind == AiChatKind.Pending;
    public bool ShowPendingButtons => Kind == AiChatKind.Pending && !IsResolved;

    partial void OnIsResolvedChanged(bool value) => OnPropertyChanged(nameof(ShowPendingButtons));
}
