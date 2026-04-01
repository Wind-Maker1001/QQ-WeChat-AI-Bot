namespace QQAIBot.Desktop.Models;

public sealed class BackendDecisionSummary
{
    public BackendDecisionTrigger? Trigger { get; set; }

    public string[] ReasonTags { get; set; } = [];

    public BackendDecisionReasonGroups? ReasonGroups { get; set; }

    public BackendRequestedCapabilities? RequestedCapabilities { get; set; }

    public BackendRequestedTools? RequestedTools { get; set; }

    public BackendSuppressedTool[] SuppressedTools { get; set; } = [];

    public string RouteReason { get; set; } = string.Empty;

    public string MatchedPrefix { get; set; } = string.Empty;
}
