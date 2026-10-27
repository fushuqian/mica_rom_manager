using System.IO;
using MicaRomManager.Models;
using MicaRomManager.Services.Sending;
using Xunit;

namespace MicaRomManager.Tests;

public class SendTaskQueueTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mica-queue-" + Guid.NewGuid().ToString("N"));
    private readonly string _sourceDir;
    private readonly string _driveDir;

    public SendTaskQueueTests()
    {
        _sourceDir = Path.Combine(_dir, "source");
        _driveDir = Path.Combine(_dir, "drive");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_driveDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // 清理失败不影响测试
        }
    }

    private DeviceConnection Connection()
    {
        var profile = new DeviceProfile
        {
            Id = "test",
            Name = "测试设备",
            RootFolder = "Roms",
            FolderTemplate = "{root}/{platform}",
            PlatformFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["NES"] = "FC",
            },
        };

        return new DeviceConnection
        {
            Profile = profile,
            Drive = new DriveEntry(_driveDir, "测试盘", long.MaxValue, long.MaxValue, true),
        };
    }

    private Game CreateGame(string name, string content)
    {
        var path = Path.Combine(_sourceDir, name);
        File.WriteAllText(path, content);
        return new Game { Title = name, Platform = "NES", RomPath = path };
    }

    [Fact]
    public async Task Enqueue_ProcessesGamesSequentially_AndRaisesAllCompleted()
    {
        var queue = new SendTaskQueue(action => action());
        var completion = new TaskCompletionSource<SendQueueSummary>();
        queue.AllCompleted += summary => completion.TrySetResult(summary);

        var games = new[]
        {
            CreateGame("a.nes", "a"),
            CreateGame("b.nes", "b"),
        };
        queue.Enqueue(games, Connection());

        var summary = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, summary.Games);
        Assert.Equal(2, summary.Copied);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(2, queue.Tasks.Count);
        Assert.All(queue.Tasks, item => Assert.Equal("已完成", item.Status));
        Assert.True(File.Exists(Path.Combine(_driveDir, "Roms", "FC", "a.nes")));
        Assert.True(File.Exists(Path.Combine(_driveDir, "Roms", "FC", "b.nes")));
    }

    [Fact]
    public async Task Enqueue_RecordsFailureForMissingFile()
    {
        var queue = new SendTaskQueue(action => action());
        var completion = new TaskCompletionSource<SendQueueSummary>();
        queue.AllCompleted += summary => completion.TrySetResult(summary);

        var game = new Game
        {
            Title = "缺失",
            Platform = "NES",
            RomPath = Path.Combine(_sourceDir, "missing.nes"),
        };
        queue.Enqueue(new[] { game }, Connection());

        var summary = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, summary.Games);
        Assert.Equal(1, summary.Failed);
        Assert.Equal("失败", queue.Tasks.Single().Status);
    }

    [Fact]
    public void ClearFinished_RemovesCompletedTasksOnly()
    {
        var queue = new SendTaskQueue(action => action());
        var completed = new SendTaskItem { Game = new Game { Title = "A" }, Status = "已完成" };
        var queued = new SendTaskItem { Game = new Game { Title = "B" }, Status = "排队中" };
        queue.Tasks.Add(completed);
        queue.Tasks.Add(queued);

        var removed = queue.ClearFinished();

        Assert.Equal(1, removed);
        Assert.Single(queue.Tasks);
        Assert.Equal("排队中", queue.Tasks[0].Status);
    }
}
