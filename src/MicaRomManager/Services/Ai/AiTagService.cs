using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using MicaRomManager.Models;

namespace MicaRomManager.Services.Ai;

public sealed record TagProgress(int Completed, int Total, string Title);

/// <summary>一条 AI 标签建议，预览确认后才写库。</summary>
public partial class TagSuggestion : ObservableObject
{
    public long GameId { get; init; }
    public string Title { get; init; } = "";
    public string CurrentTags { get; init; } = "";
    public string CurrentGenre { get; init; } = "";
    public List<string> Tags { get; init; } = new();
    public string Genre { get; init; } = "";

    [ObservableProperty] private bool apply = true;
    [ObservableProperty] private string error = "";

    public string TagsText => string.Join("、", Tags);
    public bool HasError => Error.Length > 0;

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
}

/// <summary>为选中游戏生成标签与类型建议。</summary>
public sealed class AiTagService
{
    public async Task<List<TagSuggestion>> SuggestAsync(
        IReadOnlyList<Game> games,
        IProgress<TagProgress>? progress,
        CancellationToken token)
    {
        var results = new TagSuggestion[games.Count];
        var completed = 0;
        using var gate = new SemaphoreSlim(2);

        var tasks = games.Select(async (game, index) =>
        {
            await gate.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                results[index] = await SuggestOneAsync(game, token);
                var done = Interlocked.Increment(ref completed);
                progress?.Report(new TagProgress(done, games.Count, game.Title));
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results.Where(r => r != null).ToList();
    }

    private static async Task<TagSuggestion> SuggestOneAsync(Game game, CancellationToken token)
    {
        var suggestion = new TagSuggestion
        {
            GameId = game.Id,
            Title = game.Title,
            CurrentTags = game.Tags,
            CurrentGenre = game.Genre,
        };

        var existing = AiQueryMatcher.SplitTags(game.Tags).ToList();
        var response = await AppServices.Ai.CompleteAsync(AiPrompts.TagSystem,
            AiPrompts.TagUser(game, existing), token);
        if (!response.Success)
        {
            suggestion.Error = response.Error;
            suggestion.Apply = false;
            return suggestion;
        }

        var payload = AiJson.Parse<TagPayload>(response.Content);
        if (payload == null)
        {
            suggestion.Error = $"返回内容不是有效 JSON：{AiJson.Snippet(response.Content, 120)}";
            suggestion.Apply = false;
            return suggestion;
        }

        var tags = (payload.Tags ?? new List<string>())
            .Select(tag => (tag ?? "").Trim())
            .Where(tag => tag.Length is > 0 and <= 12)
            .Where(tag => !existing.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();

        return new TagSuggestion
        {
            GameId = game.Id,
            Title = game.Title,
            CurrentTags = game.Tags,
            CurrentGenre = game.Genre,
            Tags = tags,
            Genre = (payload.Genre ?? "").Trim().Length <= 40 ? (payload.Genre ?? "").Trim() : "",
            Apply = tags.Count > 0 || !string.IsNullOrWhiteSpace(payload.Genre),
            Error = tags.Count == 0 && string.IsNullOrWhiteSpace(payload.Genre) ? "模型没有给出可用标签。" : "",
        };
    }

    /// <summary>应用建议：append 模式下保留人工标签。</summary>
    public static int Apply(IEnumerable<TagSuggestion> suggestions, IReadOnlyList<Game> games, bool append)
    {
        var applied = 0;
        foreach (var suggestion in suggestions.Where(s => s.Apply && !s.HasError))
        {
            var game = games.FirstOrDefault(g => g.Id == suggestion.GameId);
            if (game == null) continue;

            var current = append ? AiQueryMatcher.SplitTags(game.Tags).ToList() : new List<string>();
            foreach (var tag in suggestion.Tags)
                if (!current.Contains(tag, StringComparer.OrdinalIgnoreCase)) current.Add(tag);

            game.Tags = string.Join(",", current);
            if (suggestion.Genre.Length > 0) game.Genre = suggestion.Genre;

            try
            {
                AppServices.GameRepo.Update(game);
                applied++;
            }
            catch
            {
                suggestion.Error = "写库失败";
            }
        }

        return applied;
    }

    private sealed class TagPayload
    {
        [JsonPropertyName("tags")] public List<string>? Tags { get; set; }
        [JsonPropertyName("genre")] public string? Genre { get; set; }
    }
}
