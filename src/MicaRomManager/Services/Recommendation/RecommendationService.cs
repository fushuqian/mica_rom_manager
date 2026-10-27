using MicaRomManager.Models;
using MicaRomManager.Services.Ai;

namespace MicaRomManager.Services.Recommendation;

public sealed record RecommendationItem(Game Game, double Score, string Reason);

/// <summary>纯本地相似度推荐，不调用 AI、不耗流量。</summary>
public static class RecommendationService
{
    public static List<RecommendationItem> Recommend(Game? seed, IEnumerable<Game> all, int limit = 10)
    {
        var games = all.ToList();
        if (seed == null)
        {
            seed = games.FirstOrDefault(g => g.Status == GameStatus.Playing)
                   ?? games.Where(g => g.Status == GameStatus.NotPlayed)
                       .OrderByDescending(g => g.Rating)
                       .FirstOrDefault();
        }

        if (seed == null) return new List<RecommendationItem>();

        var seedTags = AiQueryMatcher.SplitTags(seed.Tags).ToList();
        var results = new List<RecommendationItem>();

        foreach (var game in games)
        {
            if (game.Id == seed.Id) continue;

            double score = 0;
            var reasons = new List<string>();

            if (Same(game.Platform, seed.Platform))
            {
                score += 2;
                reasons.Add("同平台");
            }

            if (!string.IsNullOrWhiteSpace(seed.Developer) && Same(game.Developer, seed.Developer))
            {
                score += 1;
                reasons.Add("同开发商");
            }

            if (!string.IsNullOrWhiteSpace(seed.Genre) && Same(game.Genre, seed.Genre))
            {
                score += 1;
                reasons.Add("同类型");
            }

            var sharedTags = AiQueryMatcher.SplitTags(game.Tags)
                .Count(tag => seedTags.Contains(tag, StringComparer.OrdinalIgnoreCase));
            if (sharedTags > 0)
            {
                score += Math.Min(sharedTags, 3);
                reasons.Add("同标签");
            }

            if (!string.IsNullOrWhiteSpace(seed.Region) && Same(game.Region, seed.Region))
                score += 0.5;

            if (game.Status == GameStatus.NotPlayed)
            {
                score += 1;
                reasons.Add("未玩过");
            }
            else if (game.Status == GameStatus.Finished)
            {
                score -= 1;
            }

            if (score <= 0) continue;
            results.Add(new RecommendationItem(game, score, reasons.Count == 0 ? "相似" : string.Join("、", reasons)));
        }

        return results
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Game.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(limit)
            .ToList();
    }

    private static bool Same(string left, string right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
