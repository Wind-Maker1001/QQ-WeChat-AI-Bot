namespace QQAIBot.Desktop.Models;

public sealed record LocalStateSnapshotResult
{
    public string ArchivePath { get; init; } = string.Empty;

    public IReadOnlyList<string> IncludedEntries { get; init; } = [];

    public bool IncludesSecrets { get; init; }
}
