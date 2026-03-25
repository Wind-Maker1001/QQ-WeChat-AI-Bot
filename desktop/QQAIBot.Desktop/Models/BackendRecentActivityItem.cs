namespace QQAIBot.Desktop.Models;

public sealed class BackendRecentActivityItem
{
    public string EventKey { get; set; } = string.Empty;

    public string CapturedAt { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Meta { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public bool IsFailure { get; set; }
}
