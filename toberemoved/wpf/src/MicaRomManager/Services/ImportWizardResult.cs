using MicaRomManager.Models;

namespace MicaRomManager.Services;

/// <summary>导入向导确认后返回的结果。</summary>
public sealed class ImportWizardResult
{
    public IReadOnlyList<ImportCandidate> Candidates { get; init; } = Array.Empty<ImportCandidate>();
    public ImportMode Mode { get; init; }
    public bool Recursive { get; init; } = true;
}
