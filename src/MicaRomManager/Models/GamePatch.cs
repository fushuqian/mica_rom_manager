using MicaRomManager.Models;

namespace MicaRomManager.Models;

/// <summary>AI 批量更新游戏的字段集合；null 表示不修改。</summary>
public sealed class GamePatch
{
    public string? Title { get; set; }
    public string? Genre { get; set; }
    public string? Region { get; set; }
    public string? Language { get; set; }
    public string? SpecialNote { get; set; }
    public string? Publisher { get; set; }
    public string? Developer { get; set; }
    public string? Notes { get; set; }
    public string? Tags { get; set; }
    public string? Status { get; set; }
    public int? ReleaseYear { get; set; }
    public int? Rating { get; set; }
    public bool? Favorite { get; set; }

    public bool HasAny =>
        Title != null || Genre != null || Region != null || Language != null
        || SpecialNote != null || Publisher != null || Developer != null || Notes != null
        || Tags != null || Status != null || ReleaseYear != null || Rating != null || Favorite != null;

    public void ApplyTo(Game game)
    {
        if (Title != null) game.Title = Title.Trim();
        if (Genre != null) game.Genre = Genre.Trim();
        if (Region != null) game.Region = Region.Trim();
        if (Language != null) game.Language = Language.Trim();
        if (SpecialNote != null) game.SpecialNote = SpecialNote.Trim();
        if (Publisher != null) game.Publisher = Publisher.Trim();
        if (Developer != null) game.Developer = Developer.Trim();
        if (Notes != null) game.Notes = Notes.Trim();
        if (Tags != null) game.Tags = Tags.Trim();
        if (Status != null) game.Status = Status.Trim();
        if (ReleaseYear is int year) game.ReleaseYear = year;
        if (Rating is int rating) game.Rating = Math.Clamp(rating, 0, 5);
        if (Favorite is bool favorite) game.Favorite = favorite;
    }

    public string Describe()
    {
        var parts = new List<string>();
        Add(parts, "标题", Title);
        Add(parts, "类型", Genre);
        Add(parts, "区域", Region);
        Add(parts, "语言", Language);
        Add(parts, "特注", SpecialNote);
        Add(parts, "发行商", Publisher);
        Add(parts, "开发商", Developer);
        Add(parts, "备注", Notes);
        Add(parts, "标签", Tags);
        Add(parts, "状态", Status);
        if (ReleaseYear is int year) parts.Add($"年份={year}");
        if (Rating is int rating) parts.Add($"评分={rating}");
        if (Favorite is bool favorite) parts.Add(favorite ? "收藏=是" : "收藏=否");
        return parts.Count == 0 ? "无字段变化" : string.Join("，", parts);
    }

    private static void Add(List<string> parts, string label, string? value)
    {
        if (value != null) parts.Add($"{label}={value}");
    }
}
