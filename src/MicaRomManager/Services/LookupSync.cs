using MicaRomManager.Data;
using MicaRomManager.Models;

namespace MicaRomManager.Services;

/// <summary>把游戏里出现的元数据补进字典库（刮削/编辑后调用），已存在则跳过。</summary>
public static class LookupSync
{
    public static int SyncGame(Game game)
    {
        var added = 0;
        added += Add(LookupRepository.Platform, game.Platform);
        added += Add(LookupRepository.Region, game.Region);
        added += Add(LookupRepository.Language, game.Language);
        added += Add(LookupRepository.Genre, game.Genre);
        added += Add(LookupRepository.Publisher, game.Publisher);
        added += Add(LookupRepository.Developer, game.Developer);
        foreach (var tag in game.Tags.Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
            added += Add(LookupRepository.Tag, tag);
        return added;
    }

    private static int Add(string kind, string rawValue)
    {
        var value = rawValue?.Trim() ?? "";
        if (value.Length == 0) return 0;
        if (AppServices.Lookups.List(kind).Contains(value, StringComparer.OrdinalIgnoreCase)) return 0;

        try
        {
            var result = AppServices.Lookups.Add(kind, value);
            return result.Success ? 1 : 0;
        }
        catch
        {
            return 0;
        }
    }
}
