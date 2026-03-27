namespace QQAIBot.Desktop.Models;

public sealed record LocalStateSnapshotPreviewResult
{
    public string ArchivePath { get; init; } = string.Empty;

    public IReadOnlyList<string> Lines { get; init; } = [];

    public IReadOnlyList<string> Recommendations { get; init; } = [];
}
