using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using MicaRomManager.Models;
using System.Windows;

namespace MicaRomManager.Services.Sending;

public sealed record SendQueueSummary(int Games, int Copied, int Skipped, int Failed);

/// <summary>
/// 发送任务队列：游戏按顺序在后台处理，进度回主界面线程；队列清空后触发 AllCompleted。
/// </summary>
public sealed class SendTaskQueue
{
    private readonly Action<Action> _post;
    private readonly ConcurrentQueue<(SendTaskItem Item, DeviceConnection Connection)> _pending = new();
    private readonly object _gate = new();

    public ObservableCollection<SendTaskItem> Tasks { get; } = new();

    public bool IsRunning { get; private set; }

    /// <summary>全部任务完成后触发（已切回 UI 线程）。</summary>
    public event Action<SendQueueSummary>? AllCompleted;

    public SendTaskQueue(Action<Action>? post = null)
    {
        _post = post ?? DefaultPost;
    }

    private static void DefaultPost(Action action)
    {
        var app = Application.Current;
        if (app == null || app.Dispatcher.CheckAccess()) action();
        else app.Dispatcher.BeginInvoke(action);
    }

    public void Enqueue(IEnumerable<Game> games, DeviceConnection connection)
    {
        var start = false;
        lock (_gate)
        {
            foreach (var game in games)
            {
                var item = new SendTaskItem
                {
                    Game = game,
                    SizeText = Game.FormatSize(game.Discs.Sum(SafeSize)),
                };

                Tasks.Add(item);
                _pending.Enqueue((item, connection));
            }

            if (!IsRunning)
            {
                IsRunning = true;
                start = true;
            }
        }

        if (start) _ = ProcessAsync();
    }

    public int ClearFinished()
    {
        var finished = Tasks.Where(item => item.IsFinished).ToList();
        foreach (var item in finished) Tasks.Remove(item);
        return finished.Count;
    }

    private async Task ProcessAsync()
    {
        var summary = new SendQueueSummary(0, 0, 0, 0);

        while (true)
        {
            while (_pending.TryDequeue(out var work))
            {
            var item = work.Item;
            var connection = work.Connection;
            summary = summary with { Games = summary.Games + 1 };

            _post(() =>
            {
                item.Status = "发送中";
                item.Progress = 0;
                item.ProgressText = "准备中…";
            });

            try
            {
                var outcome = await Task.Run(() => GameSender.Send(
                    item.Game, connection,
                    p =>
                    {
                        var percent = p.TotalBytes > 0 ? Math.Min(100, p.CopiedBytes * 100.0 / p.TotalBytes) : 0;
                        _post(() =>
                        {
                            item.Progress = percent;
                            item.ProgressText = p.FileCount > 0
                                ? $"{p.FileIndex}/{p.FileCount} 文件 · {percent:0}%"
                                : $"{percent:0}%";
                        });
                    }));

                summary = summary with
                {
                    Copied = summary.Copied + outcome.Copied,
                    Skipped = summary.Skipped + outcome.Skipped,
                    Failed = summary.Failed + outcome.Failed,
                };

                _post(() =>
                {
                    item.Progress = 100;
                    item.ProgressText = "100%";
                    item.Status = outcome.Failed > 0
                        ? (outcome.Copied > 0 ? "部分完成" : "失败")
                        : outcome.Copied > 0
                            ? "已完成"
                            : "已跳过";
                    item.Message = string.Join("；", outcome.Messages.Take(3));
                    item.TargetFolder = outcome.TargetFolder;
                });
            }
            catch (Exception ex)
            {
                summary = summary with { Failed = summary.Failed + 1 };
                _post(() =>
                {
                    item.Status = "失败";
                    item.Message = ex.Message;
                    item.ProgressText = "";
                });
            }
            }

            lock (_gate)
            {
                if (!_pending.IsEmpty) continue;
                IsRunning = false;
                break;
            }
        }

        _post(() => AllCompleted?.Invoke(summary));
    }

    private static long SafeSize(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }
}
