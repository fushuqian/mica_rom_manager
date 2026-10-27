using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MicaRomManager.Models;
using MicaRomManager.Services.Ai;
using AiChatItem = MicaRomManager.Models.AiChatMessage;

namespace MicaRomManager.ViewModels;

/// <summary>右侧 AI 助手聊天面板。</summary>
public partial class AiAssistantViewModel : ObservableObject
{
    private readonly IAiAppHost _host;
    private readonly AiAssistantService _service;
    private readonly AiChatStore _store;
    private CancellationTokenSource? _cts;

    public ObservableCollection<AiChatItem> Messages { get; } = new();

    public IReadOnlyList<string> Suggestions { get; } = new[]
    {
        "筛选未通关的 PS1 游戏",
        "介绍一下选中的游戏",
        "扫描 E 盘并生成设备配置",
        "把选中的 SNES 游戏加标签 待补",
    };

    [ObservableProperty] private string input = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusText = "";

    public bool AiConfigured => AppServices.Ai.Options.IsConfigured;
    public string ModelText => AiConfigured ? $"模型：{AppServices.Ai.Options.Model}" : "未配置 AI（设置 → AI）";
    public bool HasMessages => Messages.Count > 0;
    public bool CanSend => !IsBusy && Input.Trim().Length > 0;

    public AiAssistantViewModel(IAiAppHost host)
    {
        _host = host;
        _service = new AiAssistantService();
        _store = new AiChatStore(AppServices.Paths.AiChatPath);

        foreach (var record in _store.Load())
        {
            Messages.Add(new AiChatItem
            {
                Kind = record.Role == "user" ? AiChatKind.User : AiChatKind.Assistant,
                Text = record.Text,
                Time = record.Time,
            });
        }

        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMessages));
    }

    partial void OnInputChanged(string value) => OnPropertyChanged(nameof(CanSend));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSend));

    [RelayCommand]
    private async Task SendAsync()
    {
        var text = Input.Trim();
        if (text.Length == 0 || IsBusy) return;

        Input = "";
        await SendTextAsync(text);
    }

    /// <summary>「AI 问问」：把游戏推进聊天框并直接向助手提问。</summary>
    public async Task AskAboutAsync(Game game)
    {
        var title = string.IsNullOrWhiteSpace(game.Title) ? "这款游戏" : game.Title;
        var text = $"介绍一下《{title}》这款游戏";

        if (!AiConfigured || IsBusy || _service.HasPending)
        {
            Input = text;
            if (!AiConfigured)
                AddError("还没有配置 AI，请先到 设置 → AI 填写 BaseUrl、API Key 和模型。");
            else
                AddError(IsBusy ? "助手正在回复上一条消息，请稍后再试。" : "请先确认或取消上一条操作。");
            return;
        }

        await SendTextAsync(text);
    }

    private async Task SendTextAsync(string text)
    {
        if (!AiConfigured)
        {
            AddError("还没有配置 AI，请先到 设置 → AI 填写 BaseUrl、API Key 和模型。");
            return;
        }

        if (_service.HasPending)
        {
            AddError("请先确认或取消上一条操作。");
            return;
        }

        Add(new AiChatItem { Kind = AiChatKind.User, Text = text });
        SaveHistory();

        IsBusy = true;
        StatusText = "正在思考…";
        _cts = new CancellationTokenSource();
        try
        {
            var result = await _service.SendAsync(text, _host, _cts.Token);
            HandleResult(result);
        }
        catch (OperationCanceledException)
        {
            Add(new AiChatItem { Kind = AiChatKind.Tool, Text = "已停止本轮生成。" });
        }
        catch (Exception ex)
        {
            AddError($"请求失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            StatusText = "";
            _cts?.Dispose();
            _cts = null;
        }
    }

    public async Task ConfirmAsync(AiChatItem message)
    {
        var pending = message.Pending;
        if (pending == null || message.IsResolved || IsBusy) return;

        if (pending.IsDestructive)
        {
            var answer = MessageBox.Show(
                $"{pending.Title}\n\n{pending.Summary}\n\n此操作不能自动撤销，是否继续？",
                "危险操作确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        message.IsResolved = true;
        IsBusy = true;
        StatusText = "正在执行…";
        _cts = new CancellationTokenSource();
        try
        {
            var execution = await pending.ExecuteAsync();
            Add(new AiChatItem
            {
                Kind = AiChatKind.Tool,
                Text = "已执行：" + Shorten(execution, 140),
            });

            if (pending.IsLaunchSuggestion)
            {
                _service.ResolvePending(execution);
                return;
            }

            var result = await _service.CompletePendingAsync(execution, _host, _cts.Token);
            HandleResult(result);
        }
        catch (OperationCanceledException)
        {
            _service.CancelPending();
            Add(new AiChatItem { Kind = AiChatKind.Tool, Text = "执行已取消。" });
        }
        catch (Exception ex)
        {
            _service.CancelPending();
            AddError($"执行失败：{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            StatusText = "";
            _cts?.Dispose();
            _cts = null;
            SaveHistory();
        }
    }

    /// <summary>「换一个」：本地换一个候选，不再请求模型。</summary>
    public async Task ShuffleAsync(AiChatItem message)
    {
        if (message.Pending?.AlternativeAsync == null || message.IsResolved || IsBusy) return;

        try
        {
            var next = await message.Pending.AlternativeAsync();
            if (next == null)
            {
                Add(new AiChatItem { Kind = AiChatKind.Tool, Text = "没有其它可选的游戏了。" });
                return;
            }

            message.Pending = next;
            message.Text = next.Title;
        }
        catch (Exception ex)
        {
            AddError($"换一个失败：{ex.Message}");
        }
    }

    public void CancelPending(AiChatItem message)
    {
        if (message.Pending == null || message.IsResolved) return;

        message.IsResolved = true;
        _service.CancelPending();
        Add(new AiChatItem { Kind = AiChatKind.Tool, Text = "已取消该操作。" });
        SaveHistory();
    }

    [RelayCommand]
    private void Clear()
    {
        _cts?.Cancel();
        _service.Reset();
        Messages.Clear();
        _store.Clear();
        OnPropertyChanged(nameof(HasMessages));
    }

    [RelayCommand]
    private void Stop() => _cts?.Cancel();

    public void FocusInput() { }

    private void HandleResult(AiTurnResult result)
    {
        if (result.Error.Length > 0) AddError(result.Error);
        if (result.Text.Length > 0)
            Add(new AiChatItem { Kind = AiChatKind.Assistant, Text = result.Text });
        if (result.Pending != null)
        {
            Add(new AiChatItem
            {
                Kind = AiChatKind.Pending,
                Text = result.Pending.Title,
                Pending = result.Pending,
            });
        }

        SaveHistory();
    }

    private void Add(AiChatItem message)
    {
        Messages.Add(message);
        SaveHistory();
    }

    private void AddError(string text) => Add(new AiChatItem { Kind = AiChatKind.Error, Text = text });

    private void SaveHistory()
    {
        var records = Messages
            .Where(message => message.Kind is AiChatKind.User or AiChatKind.Assistant)
            .Select(message => new AiChatRecord(
                message.Kind == AiChatKind.User ? "user" : "assistant",
                message.Text,
                message.Time))
            .ToList();
        _store.Save(records);
    }

    private static string Shorten(string text, int maxLength)
    {
        var value = (text ?? "").Replace('\n', ' ').Trim();
        return value.Length <= maxLength ? value : value[..maxLength] + "…";
    }
}
