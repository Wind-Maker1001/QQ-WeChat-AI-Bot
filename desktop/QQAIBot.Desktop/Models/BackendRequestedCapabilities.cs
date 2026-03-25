namespace QQAIBot.Desktop.Models;

public sealed class BackendRequestedCapabilities
{
    public string ReasoningEffort { get; set; } = string.Empty;

    public string TextVerbosity { get; set; } = string.Empty;

    public bool EnableWebSearch { get; set; }

    public bool EnableCodeInterpreter { get; set; }

    public bool NeedsResponsesCapabilities { get; set; }
}
