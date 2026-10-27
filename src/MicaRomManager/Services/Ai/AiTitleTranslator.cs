using System.Text.Json.Serialization;
using MicaRomManager.Models;

namespace MicaRomManager.Services.Ai;

public sealed record TranslateProgress(int Completed, int Total, string Title);

public sealed record TitleTranslation(
    long GameId,
    string OldTitle,
    string NewTitle,
    string Note,
    string Error)
{
    public bool Success => Error.Length == 0 && NewTitle.Length > 0
                           && !string.Equals(OldTitle, NewTitle, StringComparison.Ordinal);
}

/// <summary>把游戏标题转换为中文通用译名；只生成建议，不直接写库或改名。</summary>
public sealed class AiTitleTranslator
{
    public async Task<List<TitleTranslation>> TranslateAsync(
        IReadOnlyList<Game> games,
        IProgress<TranslateProgress>? progress,
        CancellationToken token)
    {
        var results = new TitleTranslation[games.Count];
        var completed = 0;
        using var gate = new SemaphoreSlim(2);

        var tasks = games.Select(async (game, index) =>
        {
            await gate.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                results[index] = await TranslateOneAsync(game, token);
                var done = Interlocked.Increment(ref completed);
                progress?.Report(new TranslateProgress(done, games.Count, game.Title));
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results.Where(r => r != null).ToList();
    }

    private static async Task<TitleTranslation> TranslateOneAsync(Game game, CancellationToken token)
    {
        var response = await AppServices.Ai.CompleteAsync(AiPrompts.TranslateSystem,
            AiPrompts.TranslateUser(game), token);
        if (!response.Success) return new TitleTranslation(game.Id, game.Title, "", "", response.Error);

        var payload = AiJson.Parse<TranslatePayload>(response.Content);
        if (payload == null)
            return new TitleTranslation(game.Id, game.Title, "", "",
                $"返回内容不是有效 JSON：{AiJson.Snippet(response.Content, 120)}");

        var title = (payload.Title ?? "").Trim().Trim('「', '」', '"', '“', '”').Replace('\n', ' ');
        var note = (payload.Note ?? "").Trim();
        if (title.Length == 0)
            return new TitleTranslation(game.Id, game.Title, "", note, "模型没有返回标题。");

        return new TitleTranslation(game.Id, game.Title, title, note, "");
    }

    private sealed class TranslatePayload
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
    }
}
