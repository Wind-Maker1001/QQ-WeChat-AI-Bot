namespace QQAIBot.Desktop.Models;

public sealed class BackendLlmFailureStatus
{
    public string CapturedAt { get; set; } = string.Empty;

    public string ChannelId { get; set; } = string.Empty;

    public string Route { get; set; } = string.Empty;

    public string RouteReason { get; set; } = string.Empty;

    public string MatchedPrefix { get; set; } = string.Empty;

    public BackendDecisionSummary? DecisionSummary { get; set; }

    public BackendRequestedTools? RequestedTools { get; set; }

    public BackendSuppressedTool[] SuppressedTools { get; set; } = [];

    public string ExecutionKind { get; set; } = string.Empty;

    public string ExecutionSummary { get; set; } = string.Empty;

    public BackendExecutionProjection? ExecutionProjection { get; set; }

    public string ChatId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string Error { get; set; } = string.Empty;
}
