using CommunityToolkit.Mvvm.ComponentModel;
using MicaRomManager.Models;

namespace MicaRomManager.Models;

/// <summary>一次刮削的候选结果；只用于预览，确认后才写库。</summary>
public partial class ScrapeResult : ObservableObject
{
    public long GameId { get; set; }

    [ObservableProperty] private bool apply = true;
    [ObservableProperty] private string title = "";
    [ObservableProperty] private string genre = "";
    [ObservableProperty] private string region = "";
    [ObservableProperty] private string language = "";
    [ObservableProperty] private int? releaseYear;
    [ObservableProperty] private string publisher = "";
    [ObservableProperty] private string developer = "";
    /// <summary>简介候选，只在原备注为空时写入 Notes。</summary>
    [ObservableProperty] private string summary = "";
    [ObservableProperty] private string source = "";
    [ObservableProperty] private string error = "";

    public string YearText => ReleaseYear?.ToString() ?? "";

    public bool HasError => Error.Length > 0;

    partial void OnReleaseYearChanged(int? value) => OnPropertyChanged(nameof(YearText));
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));

    /// <summary>把建议值写回游戏；summary 只在原备注为空时写入。</summary>
    public void ApplyTo(Game game)
    {
        if (Title.Length > 0) game.Title = Title;
        if (Genre.Length > 0) game.Genre = Genre;
        if (Region.Length > 0) game.Region = Region;
        if (Language.Length > 0) game.Language = Language;
        if (ReleaseYear is int year) game.ReleaseYear = year;
        if (Publisher.Length > 0) game.Publisher = Publisher;
        if (Developer.Length > 0) game.Developer = Developer;
        if (Summary.Length > 0 && string.IsNullOrWhiteSpace(game.Notes)) game.Notes = Summary;
    }
}
