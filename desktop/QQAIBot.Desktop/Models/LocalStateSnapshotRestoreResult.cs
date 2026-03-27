namespace QQAIBot.Desktop.Models;

public sealed record LocalStateSnapshotRestoreResult
{
    public string ArchivePath { get; init; } = string.Empty;

    public IReadOnlyList<string> RestoredEntries { get; init; } = [];
}
