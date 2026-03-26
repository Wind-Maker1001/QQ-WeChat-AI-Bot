namespace QQAIBot.Desktop.Models;

public static class BackendExecutionProjectionTags
{
    public const string DirectKind = "direct";
    public const string DeliberationKind = "deliberation";
    public const string LocalCapabilityReplyKind = "local-capability-reply";

    public const string DirectStage = "direct";
    public const string PlannerStage = "planner";
    public const string DraftStage = "draft";
    public const string RewriteStage = "rewrite";
    public const string LocalCapabilityReplyStage = "local-capability-reply";

    public const string PlannerFailedRecovery = "planner-failed";
    public const string RewriteFallbackToDraftRecovery = "rewrite-fallback-to-draft";

    public static string DeliberationSummary => string.Join("->", DeliberationStages);

    public static string[] DeliberationStages =>
    [
        PlannerStage,
        DraftStage,
        RewriteStage
    ];
}
