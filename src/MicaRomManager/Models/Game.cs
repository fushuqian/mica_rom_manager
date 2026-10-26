using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MicaRomManager.Models;

public static class GameStatus
{
    public const string NotPlayed = "未玩";
    public const string Playing = "在玩";
    public const string Finished = "已通关";
    public const string Dropped = "已放弃";

    public static readonly string[] All = { NotPlayed, Playing, Finished, Dropped };
}

public partial class Game : ObservableObject
{
    public long Id { get; set; }

    [ObservableProperty] private string title = "";
    [ObservableProperty] private string platform = "";
    [ObservableProperty] private string genre = "";
    [ObservableProperty] private string region = "";
    [ObservableProperty] private string language = "";
    /// <summary>ROM 内容指纹（SHA256；大文件为 fast: 采样指纹）。</summary>
    [ObservableProperty] private string romHash = "";
    /// <summary>多碟游戏的附加碟片路径，换行分隔（第一碟仍是 RomPath）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMultiDisc))]
    [NotifyPropertyChangedFor(nameof(DiscCount))]
    [NotifyPropertyChangedFor(nameof(DiscCountText))]
    private string discPaths = "";
    [ObservableProperty] private int? releaseYear;
    [ObservableProperty] private string publisher = "";
    [ObservableProperty] private string developer = "";
    [ObservableProperty] private bool favorite;
    [ObservableProperty] private string status = GameStatus.NotPlayed;
    [ObservableProperty] private string romPath = "";
    [ObservableProperty] private long fileSize;
    [ObservableProperty] private string coverPath = "";
    [ObservableProperty] private string notes = "";
    [ObservableProperty] private string tags = "";
    [ObservableProperty] private long? emulatorId;
    [ObservableProperty] private int playCount;
    [ObservableProperty] private long playSeconds;
    [ObservableProperty] private string? lastPlayedAt;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public bool IsMissing => !string.IsNullOrWhiteSpace(RomPath) && !File.Exists(RomPath);

    public string FavoriteDisplay => Favorite ? "★" : "";

    /// <summary>全部碟片路径（第一碟 + 附加碟）。</summary>
    public IReadOnlyList<string> Discs
    {
        get
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(RomPath)) list.Add(RomPath);
            foreach (var path in DiscPaths.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (!list.Contains(path, StringComparer.OrdinalIgnoreCase)) list.Add(path);
            return list;
        }
    }

    public bool IsMultiDisc => Discs.Count > 1;

    public int DiscCount => Discs.Count;

    public string DiscCountText => DiscCount.ToString();

    public string PlaySecondsDisplay => PlaySeconds <= 0
        ? ""
        : $"{PlaySeconds / 3600:00}:{PlaySeconds % 3600 / 60:00}";

    public string LastPlayedDisplay => string.IsNullOrWhiteSpace(LastPlayedAt)
        ? ""
        : DateTime.TryParse(LastPlayedAt, out var dt) ? dt.ToString("yyyy-MM-dd HH:mm") : LastPlayedAt;

    public string FileSizeDisplay => FormatSize(FileSize);

    public string FileName => string.IsNullOrWhiteSpace(RomPath) ? "" : Path.GetFileName(RomPath);

    partial void OnPlaySecondsChanged(long value) => OnPropertyChanged(nameof(PlaySecondsDisplay));

    partial void OnLastPlayedAtChanged(string? value) => OnPropertyChanged(nameof(LastPlayedDisplay));

    partial void OnFileSizeChanged(long value) => OnPropertyChanged(nameof(FileSizeDisplay));

    partial void OnFavoriteChanged(bool value) => OnPropertyChanged(nameof(FavoriteDisplay));

    partial void OnRomPathChanged(string value)
    {
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(FileName));
    }

    public void RefreshFileState() => OnPropertyChanged(nameof(IsMissing));

    public Game Clone() => (Game)MemberwiseClone();

    /// <summary>把编辑副本的字段写回原对象（取消编辑时不污染原数据）。</summary>
    public void ApplyFrom(Game other)
    {
        Title = other.Title;
        Platform = other.Platform;
        Genre = other.Genre;
        Region = other.Region;
        Language = other.Language;
        ReleaseYear = other.ReleaseYear;
        Publisher = other.Publisher;
        Developer = other.Developer;
        Favorite = other.Favorite;
        Status = other.Status;
        RomPath = other.RomPath;
        RomHash = other.RomHash;
        DiscPaths = other.DiscPaths;
        FileSize = other.FileSize;
        CoverPath = other.CoverPath;
        Notes = other.Notes;
        Tags = other.Tags;
        EmulatorId = other.EmulatorId;
    }

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }
}
