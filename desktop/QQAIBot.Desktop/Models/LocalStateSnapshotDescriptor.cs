namespace QQAIBot.Desktop.Models;

public sealed record LocalStateSnapshotDescriptor
{
    public string ArchivePath { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public string SizeText { get; init; } = string.Empty;

    public string CreatedAtText { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public bool IncludesSecrets { get; init; }

    public IReadOnlyList<string> IncludedEntries { get; init; } = [];
}
