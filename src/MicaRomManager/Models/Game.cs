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
        ReleaseYear = other.ReleaseYear;
        Publisher = other.Publisher;
        Developer = other.Developer;
        Favorite = other.Favorite;
        Status = other.Status;
        RomPath = other.RomPath;
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
