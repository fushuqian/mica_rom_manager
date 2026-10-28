using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MicaRomManager.Data;
using MicaRomManager.Helpers;
using MicaRomManager.Models;

namespace MicaRomManager.ViewModels;

public sealed record EmulatorChoice(long? Id, string Name);

public partial class BatchEditViewModel : ObservableObject
{
    public string Title { get; }
    public ObservableCollection<string> Platforms { get; } = new();
    public ObservableCollection<string> Regions { get; } = new();
    public ObservableCollection<string> Languages { get; } = new();
    public ObservableCollection<EmulatorChoice> EmulatorChoices { get; } = new();
    public IReadOnlyList<string> Statuses { get; } = GameStatus.All;

    [ObservableProperty] private bool applyPlatform;
    [ObservableProperty] private string platformValue = "";
    [ObservableProperty] private bool applyGenre;
    [ObservableProperty] private string genreValue = "";
    [ObservableProperty] private bool applyRegion;
    [ObservableProperty] private string regionValue = "";
    [ObservableProperty] private bool applyLanguage;
    [ObservableProperty] private string languageValue = "";
    [ObservableProperty] private bool applyReleaseYear;
    [ObservableProperty] private string releaseYearText = "";
    [ObservableProperty] private bool applyPublisher;
    [ObservableProperty] private string publisherValue = "";
    [ObservableProperty] private bool applyDeveloper;
    [ObservableProperty] private string developerValue = "";
    [ObservableProperty] private bool applyStatus;
    [ObservableProperty] private string statusValue = GameStatus.NotPlayed;
    [ObservableProperty] private bool applyEmulator;
    [ObservableProperty] private EmulatorChoice? selectedEmulator;
    [ObservableProperty] private bool applyTagsAdd;
    [ObservableProperty] private string tagsAddValue = "";
    [ObservableProperty] private bool applyTagsRemove;
    [ObservableProperty] private string tagsRemoveValue = "";
    [ObservableProperty] private bool applyNotes;
    /// <summary>0 = 追加，1 = 覆盖。</summary>
    [ObservableProperty] private int notesModeIndex;
    [ObservableProperty] private string notesValue = "";
    [ObservableProperty] private string error = "";

    public BatchEditViewModel(int selectedCount)
    {
        Title = $"批量编辑（已选 {selectedCount} 条）";

        foreach (var platform in PlatformCatalog.Common) Platforms.Add(platform);
        foreach (var platform in AppServices.GameRepo.ListAll()
                     .Select(g => g.Platform)
                     .Where(p => !string.IsNullOrWhiteSpace(p))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(p => p))
        {
            if (!Platforms.Contains(platform, StringComparer.OrdinalIgnoreCase)) Platforms.Add(platform);
        }

        foreach (var region in AppServices.Lookups.List(LookupRepository.Region)) Regions.Add(region);
        foreach (var language in AppServices.Lookups.List(LookupRepository.Language)) Languages.Add(language);

        EmulatorChoices.Add(new EmulatorChoice(null, "（不指定）"));
        foreach (var emulator in AppServices.EmulatorRepo.ListAll().Where(e => e.Enabled))
            EmulatorChoices.Add(new EmulatorChoice(emulator.Id, emulator.Name));
        SelectedEmulator = EmulatorChoices[0];
    }

    public BatchEditChanges? BuildChanges()
    {
        Error = "";

        var changes = new BatchEditChanges
        {
            ApplyPlatform = ApplyPlatform,
            Platform = Clean(PlatformValue),
            ApplyGenre = ApplyGenre,
            Genre = Clean(GenreValue),
            ApplyRegion = ApplyRegion,
            Region = Clean(RegionValue),
            ApplyLanguage = ApplyLanguage,
            Language = Clean(LanguageValue),
            ApplyReleaseYear = ApplyReleaseYear,
            ApplyPublisher = ApplyPublisher,
            Publisher = Clean(PublisherValue),
            ApplyDeveloper = ApplyDeveloper,
            Developer = Clean(DeveloperValue),
            ApplyStatus = ApplyStatus,
            Status = Clean(StatusValue),
            ApplyEmulator = ApplyEmulator,
            EmulatorId = SelectedEmulator?.Id,
            ApplyTagsAdd = ApplyTagsAdd,
            TagsAdd = Clean(TagsAddValue),
            ApplyTagsRemove = ApplyTagsRemove,
            TagsRemove = Clean(TagsRemoveValue),
            ApplyNotes = ApplyNotes,
            NotesAppend = NotesModeIndex == 0,
            Notes = Clean(NotesValue),
        };

        if (changes.ApplyPlatform && changes.Platform.Length == 0)
        {
            Error = "平台不能为空";
            return null;
        }

        if (changes.ApplyReleaseYear)
        {
            var text = ReleaseYearText.Trim();
            if (text.Length == 0)
            {
                changes.ReleaseYear = null;
            }
            else if (!int.TryParse(text, out var year) || year < 1950 || year > 2100)
            {
                Error = "年份格式不正确（1950–2100 的整数，留空表示清空）";
                return null;
            }
            else
            {
                changes.ReleaseYear = year;
            }
        }

        if (!changes.HasAny)
        {
            Error = "请至少勾选一个要修改的字段";
            return null;
        }

        return changes;
    }

    private static string Clean(string? value) => value?.Trim() ?? "";
}
