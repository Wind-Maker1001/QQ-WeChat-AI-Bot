namespace QQAIBot.Desktop.Models;

public sealed class BackendLlmRequestStatus
{
    public string CapturedAt { get; set; } = string.Empty;

    public string ChannelId { get; set; } = string.Empty;

    public string Route { get; set; } = string.Empty;

    public string RouteReason { get; set; } = string.Empty;

    public string MatchedPrefix { get; set; } = string.Empty;

    public string ConfiguredModel { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public string ConfiguredApiStyle { get; set; } = string.Empty;

    public string EffectiveApiStyle { get; set; } = string.Empty;

    public string ConfiguredReasoningEffort { get; set; } = string.Empty;

    public string EffectiveReasoningEffort { get; set; } = string.Empty;

    public string ConfiguredTextVerbosity { get; set; } = string.Empty;

    public string EffectiveTextVerbosity { get; set; } = string.Empty;

    public string[] ConfiguredTools { get; set; } = [];

    public BackendRequestedTools? RequestedTools { get; set; }

    public string[] EffectiveTools { get; set; } = [];

    public BackendSuppressedTool[] SuppressedTools { get; set; } = [];

    public string ExecutionKind { get; set; } = string.Empty;

    public string ExecutionSummary { get; set; } = string.Empty;

    public BackendExecutionProjection? ExecutionProjection { get; set; }

    public int ImageCount { get; set; }

    public BackendDecisionSummary? DecisionSummary { get; set; }

    public string ChatId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string ResponseId { get; set; } = string.Empty;
}
