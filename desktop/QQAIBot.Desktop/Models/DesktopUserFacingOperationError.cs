namespace QQAIBot.Desktop.Models;

public sealed record DesktopUserFacingOperationError
{
    public string StatusText { get; init; } = string.Empty;

    public string DialogTitle { get; init; } = string.Empty;

    public string DialogMessage { get; init; } = string.Empty;

    public string SuggestedActionLabel { get; init; } = string.Empty;

    public string SuggestedActionKey { get; init; } = string.Empty;
}
