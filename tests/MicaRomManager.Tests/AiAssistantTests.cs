using System.IO;
using MicaRomManager.Models;
using MicaRomManager.Services.Ai;
using AiWireMessage = MicaRomManager.Services.Ai.AiChatMessage;
using Xunit;

namespace MicaRomManager.Tests;

public class AiAssistantTests
{
    [Fact]
    public void Registry_ExposesExpectedToolsAndSafeSettings()
    {
        var names = AiToolRegistry.All.Select(tool => tool.Name).ToList();

        Assert.Contains("query_games", names);
        Assert.Contains("web_search", names);
        Assert.Contains("update_games", names);
        Assert.Contains("delete_games", names);
        Assert.Contains("save_device_profile", names);
        Assert.DoesNotContain("ai.api_key", AiToolRegistry.SettingsWhitelist);
        Assert.Contains("network.proxy.mode", AiToolRegistry.SettingsWhitelist);
    }

    [Fact]
    public async Task UpdateGames_ReturnsPendingAndDoesNotWrite()
    {
        var host = new FakeHost(new[]
        {
            new Game { Id = 1, Title = "银河战士", Platform = "SNES" },
        });
        var call = Tool("update_games",
            "{\"game_ids\":[1],\"fields\":{\"genre\":\"动作\",\"rating\":5}}");

        var result = await new AiToolExecutor().ExecuteAsync(call, host, CancellationToken.None);

        Assert.NotNull(result.Pending);
        Assert.Equal(0, host.UpdateCalls);
        Assert.Contains("修改游戏信息", result.Pending!.Title);

        var execution = await result.Pending.ExecuteAsync();

        Assert.Equal(1, host.UpdateCalls);
        Assert.Contains("\"updated\":1", execution);
        Assert.Equal("动作", host.Games[0].Genre);
        Assert.Equal(5, host.Games[0].Rating);
    }

    [Fact]
    public async Task DeleteGames_RequiresDestructiveConfirmation()
    {
        var host = new FakeHost(new[]
        {
            new Game { Id = 3, Title = "旧游戏", RomPath = @"C:\roms\old.sfc" },
        });
        var call = Tool("delete_games", "{\"game_ids\":[3],\"recycle_files\":true}");

        var result = await new AiToolExecutor().ExecuteAsync(call, host, CancellationToken.None);

        Assert.NotNull(result.Pending);
        Assert.True(result.Pending!.IsDestructive);
        Assert.Equal(0, host.DeleteCalls);

        await result.Pending.ExecuteAsync();
        Assert.Equal(1, host.DeleteCalls);
        Assert.True(host.LastRecycleFlag);
    }

    [Fact]
    public async Task SetSettings_RejectsApiKeyAndInvalidValues()
    {
        var host = new FakeHost(Array.Empty<Game>());
        var executor = new AiToolExecutor();

        var blocked = await executor.ExecuteAsync(
            Tool("set_settings", "{\"settings\":{\"ai.api_key\":\"sk-test\"}}"),
            host, CancellationToken.None);
        Assert.Contains("不允许", blocked.ResultForModel);
        Assert.Null(blocked.Pending);

        var invalid = await executor.ExecuteAsync(
            Tool("set_settings", "{\"settings\":{\"ai.timeout_seconds\":\"5\"}}"),
            host, CancellationToken.None);
        Assert.Contains("10–600", invalid.ResultForModel);
        Assert.Null(invalid.Pending);

        var allowed = await executor.ExecuteAsync(
            Tool("set_settings", "{\"settings\":{\"network.proxy.mode\":\"manual\"}}"),
            host, CancellationToken.None);
        Assert.NotNull(allowed.Pending);
        await allowed.Pending!.ExecuteAsync();
        Assert.Equal("manual", host.GetSetting("network.proxy.mode"));
    }

    [Fact]
    public async Task QueryGames_CanApplyFilterToMainList()
    {
        var host = new FakeHost(new[]
        {
            new Game { Id = 1, Title = "A", Platform = "PlayStation", Status = GameStatus.Finished },
            new Game { Id = 2, Title = "B", Platform = "PlayStation", Status = GameStatus.NotPlayed },
        });
        var call = Tool("query_games",
            "{\"filter\":{\"platform\":\"PlayStation\",\"status\":\"已通关\"},\"apply_to_list\":true}");

        var result = await new AiToolExecutor().ExecuteAsync(call, host, CancellationToken.None);

        Assert.Contains("\"total\":1", result.ResultForModel);
        Assert.NotNull(host.LastFilter);
        Assert.Equal(GameStatus.Finished, host.LastFilter!.Status);
    }

    [Fact]
    public async Task LaunchGame_RandomUsesAllGamesNotFilteredList()
    {
        var numeric = new Game { Id = 1, Title = "002", Platform = "GBA", Status = GameStatus.NotPlayed };
        var named = new Game { Id = 2, Title = "洛克人X4", Platform = "PlayStation", Status = GameStatus.NotPlayed };
        var host = new FakeHost(new[] { numeric, named }) { FilteredOverride = new[] { numeric } };

        var result = await new AiToolExecutor().ExecuteAsync(
            Tool("launch_game", "{}"), host, CancellationToken.None);

        Assert.NotNull(result.Pending);
        Assert.Contains("洛克人X4", result.Pending!.Summary);
        Assert.DoesNotContain("002", result.Pending.Summary);
    }

    [Fact]
    public async Task LaunchGame_WithoutIdReturnsThreeButtonSuggestion()
    {
        var host = new FakeHost(new[]
        {
            new Game { Id = 1, Title = "002", Platform = "GBA", Status = GameStatus.NotPlayed },
            new Game { Id = 2, Title = "洛克人X4", Platform = "PlayStation", Status = GameStatus.NotPlayed },
            new Game { Id = 3, Title = "超级银河战士", Platform = "SNES", Status = GameStatus.Playing },
        });

        var result = await new AiToolExecutor().ExecuteAsync(
            Tool("launch_game", "{}"), host, CancellationToken.None);

        Assert.NotNull(result.Pending);
        Assert.True(result.Pending!.IsLaunchSuggestion);
        Assert.NotNull(result.Pending.AlternativeAsync);
        Assert.DoesNotContain("002", result.Pending.Summary);

        await result.Pending.ExecuteAsync();
        Assert.Equal(1, host.LaunchCalls);

        var alternative = await result.Pending.AlternativeAsync!();
        Assert.NotNull(alternative);
        Assert.NotEqual(result.Pending.Summary, alternative!.Summary);
    }

    [Fact]
    public async Task AssistantService_RunsToolThenReturnsFinalText()
    {
        var host = new FakeHost(new[]
        {
            new Game { Id = 1, Title = "银河战士", Platform = "SNES", Status = GameStatus.NotPlayed },
        });
        var client = new FakeChatClient();
        client.Results.Enqueue(ToolCallResult(Tool("query_games", "{\"filter\":{\"status\":\"未玩\"}}")));
        client.Results.Enqueue(new AiToolChatResult(true, "库里有一款未玩的银河战士。", "",
            new List<AiToolCall>(), 200, TimeSpan.Zero));

        var service = new AiAssistantService(client);
        var result = await service.SendAsync("有哪些没玩的？", host, CancellationToken.None);

        Assert.Equal("", result.Error);
        Assert.Equal("库里有一款未玩的银河战士。", result.Text);
        Assert.Equal(2, client.Requests.Count);
        Assert.Contains(client.Requests[1], message => message.Role == "tool");
    }

    [Fact]
    public async Task AssistantService_PendingWaitsForConfirmation()
    {
        var host = new FakeHost(new[]
        {
            new Game { Id = 1, Title = "银河战士", Platform = "SNES" },
        });
        var client = new FakeChatClient();
        client.Results.Enqueue(ToolCallResult(Tool("update_games",
            "{\"game_ids\":[1],\"fields\":{\"genre\":\"动作\"}}")));
        client.Results.Enqueue(new AiToolChatResult(true, "已更新。", "",
            new List<AiToolCall>(), 200, TimeSpan.Zero));

        var service = new AiAssistantService(client);
        var first = await service.SendAsync("把它改成动作", host, CancellationToken.None);

        Assert.NotNull(first.Pending);
        Assert.True(service.HasPending);

        var second = await service.CompletePendingAsync("{\"ok\":true,\"updated\":1}",
            host, CancellationToken.None);

        Assert.Equal("已更新。", second.Text);
        Assert.False(service.HasPending);
    }

    [Fact]
    public void ChatStore_SaveLoadAndClear()
    {
        var path = Path.Combine(Path.GetTempPath(), "mica-chat-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new AiChatStore(path);
            store.Save(new[]
            {
                new AiChatRecord("user", "你好", DateTime.Now),
                new AiChatRecord("assistant", "你好，有什么可以帮你？", DateTime.Now),
            });

            var loaded = store.Load();
            Assert.Equal(2, loaded.Count);
            Assert.Equal("user", loaded[0].Role);

            store.Clear();
            Assert.Empty(store.Load());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static AiToolCall Tool(string name, string arguments) => new()
    {
        Id = "call-" + name,
        Function = new AiFunctionCall { Name = name, Arguments = arguments },
    };

    private static AiToolChatResult ToolCallResult(AiToolCall call) => new(
        true, "", "", new List<AiToolCall> { call }, 200, TimeSpan.Zero);

    private sealed class FakeChatClient : IAiChatClient
    {
        public Queue<AiToolChatResult> Results { get; } = new();
        public List<List<AiWireMessage>> Requests { get; } = new();

        public AiOptions Options { get; } = new()
        {
            BaseUrl = "http://localhost:1234/v1",
            ApiKey = "test",
            Model = "test-model",
        };

        public Task<AiToolChatResult> CompleteWithToolsAsync(
            IReadOnlyList<AiWireMessage> messages,
            IReadOnlyList<AiToolDefinition> tools,
            CancellationToken token)
        {
            Requests.Add(messages.ToList());
            return Task.FromResult(Results.Count > 0
                ? Results.Dequeue()
                : new AiToolChatResult(false, "", "没有更多假响应", new List<AiToolCall>(), 0, TimeSpan.Zero));
        }
    }

    private sealed class FakeHost : IAiAppHost
    {
        public List<Game> Games { get; }
        public int UpdateCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int LaunchCalls { get; private set; }
        public bool LastRecycleFlag { get; private set; }
        public AiQueryFilter? LastFilter { get; private set; }
        public string LastWindow { get; private set; } = "";
        private readonly Dictionary<string, string> _settings = new();

        public FakeHost(IReadOnlyList<Game> games) => Games = games.ToList();

        public IReadOnlyList<Game> AllGames => Games;
        public IReadOnlyList<Game>? FilteredOverride { get; init; }
        public IReadOnlyList<Game> FilteredGames => FilteredOverride ?? Games;
        public Game? SelectedGame => Games.FirstOrDefault();
        public IReadOnlyList<Game> SelectedGames => Games.Take(1).ToList();
        public string ConnectedDevice => "测试设备";

        public void ApplyLibraryFilter(AiQueryFilter filter) => LastFilter = filter;

        public void RefreshLibrary() { }

        public int UpdateGames(IReadOnlyList<long> ids, GamePatch patch)
        {
            UpdateCalls++;
            var targets = Games.Where(game => ids.Contains(game.Id)).ToList();
            foreach (var game in targets) patch.ApplyTo(game);
            return targets.Count;
        }

        public int DeleteGames(IReadOnlyList<long> ids, bool recycleFiles)
        {
            DeleteCalls++;
            LastRecycleFlag = recycleFiles;
            var targets = Games.Where(game => ids.Contains(game.Id)).ToList();
            foreach (var game in targets) Games.Remove(game);
            return targets.Count;
        }

        public bool LaunchGame(long id, int discIndex, out string message)
        {
            LaunchCalls++;
            message = "ok";
            return true;
        }

        public string SendGames(IReadOnlyList<long> ids) => "已入队";

        public string GetSetting(string key) => _settings.TryGetValue(key, out var value) ? value : "";

        public void SetSetting(string key, string value) => _settings[key] = value;

        public void OpenWindow(string window) => LastWindow = window;
    }
}
