namespace MicaRomManager.Models;

/// <summary>批量编辑的字段变更集：只有 ApplyXxx 为 true 的字段才会写入。</summary>
public sealed class BatchEditChanges
{
    public bool ApplyPlatform;
    public string Platform = "";
    public bool ApplyGenre;
    public string Genre = "";
    public bool ApplyRegion;
    public string Region = "";
    public bool ApplyLanguage;
    public string Language = "";
    public bool ApplySpecialNote;
    public string SpecialNote = "";
    public bool ApplyReleaseYear;
    public int? ReleaseYear;
    public bool ApplyPublisher;
    public string Publisher = "";
    public bool ApplyDeveloper;
    public string Developer = "";
    public bool ApplyStatus;
    public string Status = "";
    public bool ApplyEmulator;
    public long? EmulatorId;
    public bool ApplyTagsAdd;
    public string TagsAdd = "";
    public bool ApplyTagsRemove;
    public string TagsRemove = "";
    public bool ApplyNotes;
    public bool NotesAppend;
    public string Notes = "";

    public bool HasAny =>
        ApplyPlatform || ApplyGenre || ApplyRegion || ApplyLanguage || ApplySpecialNote ||
        ApplyReleaseYear ||
        ApplyPublisher || ApplyDeveloper || ApplyStatus || ApplyEmulator ||
        ApplyTagsAdd || ApplyTagsRemove || ApplyNotes;

    public void ApplyTo(Game game)
    {
        if (ApplyPlatform) game.Platform = Platform;
        if (ApplyGenre) game.Genre = Genre;
        if (ApplyRegion) game.Region = Region;
        if (ApplyLanguage) game.Language = Language;
        if (ApplySpecialNote) game.SpecialNote = SpecialNote;
        if (ApplyReleaseYear) game.ReleaseYear = ReleaseYear;
        if (ApplyPublisher) game.Publisher = Publisher;
        if (ApplyDeveloper) game.Developer = Developer;
        if (ApplyStatus) game.Status = Status;
        if (ApplyEmulator) game.EmulatorId = EmulatorId;

        if (ApplyTagsAdd || ApplyTagsRemove)
        {
            var tags = SplitTags(game.Tags).ToList();
            if (ApplyTagsAdd)
            {
                foreach (var tag in SplitTags(TagsAdd))
                    if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) tags.Add(tag);
            }

            if (ApplyTagsRemove)
            {
                var removing = new HashSet<string>(SplitTags(TagsRemove), StringComparer.OrdinalIgnoreCase);
                tags.RemoveAll(removing.Contains);
            }

            game.Tags = string.Join(",", tags);
        }

        if (ApplyNotes)
        {
            if (!NotesAppend)
            {
                game.Notes = Notes;
            }
            else if (Notes.Length > 0)
            {
                game.Notes = string.IsNullOrWhiteSpace(game.Notes)
                    ? Notes
                    : game.Notes.TrimEnd() + Environment.NewLine + Notes;
            }
        }
    }

    private static IEnumerable<string> SplitTags(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? Enumerable.Empty<string>()
            : raw.Split(new[] { ',', '，', ';', '；' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
