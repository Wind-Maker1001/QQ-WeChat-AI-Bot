using System.Linq;

using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class BackendLatestTurnOverviewBuilder
{
    public static DesktopLatestTurnOverview Build(BackendRuntimeSnapshotViewState? runtimeSnapshot)
    {
        var snapshot = runtimeSnapshot ?? new BackendRuntimeSnapshotViewState();
        var latestTurn = ResolveLatestTurn(snapshot);

        if (latestTurn is null)
        {
            return new DesktopLatestTurnOverview();
        }

        return latestTurn.Kind switch
        {
            LatestTurnKind.Request => BuildRequestOverview(latestTurn.ChannelLabel, latestTurn.Request!),
            LatestTurnKind.Failure => BuildFailureOverview(latestTurn.ChannelLabel, latestTurn.Failure!),
            _ => new DesktopLatestTurnOverview()
        };
    }

    private static DesktopLatestTurnOverview BuildRequestOverview(
        string channelLabel,
        BackendLlmRequestStatus request)
    {
        var executionKind = NormalizeExecutionKind(request.ExecutionProjection?.Kind, request.ExecutionKind);
        var route = DefaultIfBlank(request.Route, "unknown");
        var model = DefaultIfBlank(request.Model, "unknown-model");
        var apiStyle = DefaultIfBlank(request.EffectiveApiStyle, DefaultIfBlank(request.ConfiguredApiStyle, "unknown"));
        var capturedAt = BackendActivityProjectionFormatter.FormatCapturedAt(request.CapturedAt);
        var isLocalReply = string.Equals(executionKind, BackendExecutionProjectionTags.LocalCapabilityReplyKind, StringComparison.Ordinal);

        return new DesktopLatestTurnOverview
        {
            State = DesktopHealthState.Good,
            Headline = isLocalReply
                ? $"Latest turn: {channelLabel} answered locally at {capturedAt}."
                : $"Latest turn: {channelLabel} completed at {capturedAt}.",
            Summary = isLocalReply
                ? $"Summary: this turn stayed on the {route} route and replied locally without sending an LLM request."
                : $"Summary: this turn used {route} / {model} / {apiStyle}.",
            Capabilities = BuildCapabilitySummary(
                request.DecisionSummary?.RequestedCapabilities,
                request.EffectiveTools,
                request.ImageCount),
            Reason = BuildReasonSummary(
                request.DecisionSummary,
                request.MatchedPrefix,
                request.RouteReason),
            Outcome = BuildRequestOutcomeSummary(executionKind, request.ExecutionProjection),
            ActionLabel = "Review recent activity",
            ActionKey = DesktopHealthActionKeys.FocusLatestActivity
        };
    }

    private static DesktopLatestTurnOverview BuildFailureOverview(
        string channelLabel,
        BackendLlmFailureStatus failure)
    {
        var executionKind = NormalizeExecutionKind(failure.ExecutionProjection?.Kind, failure.ExecutionKind);
        var route = DefaultIfBlank(failure.Route, "unknown");
        var capturedAt = BackendActivityProjectionFormatter.FormatCapturedAt(failure.CapturedAt);

        return new DesktopLatestTurnOverview
        {
            State = DesktopHealthState.Warning,
            Headline = $"Latest turn: {channelLabel} failed at {capturedAt}.",
            Summary = $"Summary: the {route} route did not finish successfully.",
            Capabilities = BuildCapabilitySummary(
                failure.DecisionSummary?.RequestedCapabilities,
                effectiveTools: null,
                imageCount: null),
            Reason = BuildReasonSummary(
                failure.DecisionSummary,
                failure.MatchedPrefix,
                failure.RouteReason),
            Outcome = BuildFailureOutcomeSummary(executionKind, failure.ExecutionProjection, failure.Error),
            ActionLabel = channelLabel == "QQ" ? "Review QQ failure" : "Review WeChat failure",
            ActionKey = channelLabel == "QQ"
                ? DesktopHealthActionKeys.FocusQqFailure
                : DesktopHealthActionKeys.FocusWechatFailure
        };
    }

    private static string BuildCapabilitySummary(
        BackendRequestedCapabilities? requestedCapabilities,
        string[]? effectiveTools,
        int? imageCount)
    {
        var webEnabled = requestedCapabilities?.EnableWebSearch == true ||
                         effectiveTools?.Contains("web_search", StringComparer.Ordinal) == true;
        var codeEnabled = requestedCapabilities?.EnableCodeInterpreter == true ||
                          effectiveTools?.Contains("code_interpreter", StringComparer.Ordinal) == true;
        var imageText = imageCount is int count ? count.ToString() : "unknown";

        return $"Capabilities: web {(webEnabled ? "on" : "off")}, code {(codeEnabled ? "on" : "off")}, images {imageText}.";
    }

    private static string BuildReasonSummary(
        BackendDecisionSummary? summary,
        string? matchedPrefix,
        string? routeReason)
    {
        if (summary is null && string.IsNullOrWhiteSpace(matchedPrefix) && string.IsNullOrWhiteSpace(routeReason))
        {
            return "Why: no structured decision reason was captured.";
        }

        var trigger = FormatTrigger(summary, matchedPrefix);
        var capability = FormatReasonGroup(summary?.ReasonGroups?.CapabilityReasons, "default");
        var upgrade = FormatReasonGroup(summary?.ReasonGroups?.UpgradeReasons, "none");
        var routeReasonText = DefaultIfBlank(summary?.RouteReason, DefaultIfBlank(routeReason, "default"));

        return $"Why: trigger {trigger}; capability {capability}; upgrade {upgrade}; route reason {routeReasonText}.";
    }

    private static string BuildRequestOutcomeSummary(
        string executionKind,
        BackendExecutionProjection? executionProjection)
    {
        if (string.Equals(executionKind, BackendExecutionProjectionTags.LocalCapabilityReplyKind, StringComparison.Ordinal))
        {
            return "Outcome: answered locally without calling the LLM.";
        }

        if (string.Equals(executionKind, BackendExecutionProjectionTags.DirectKind, StringComparison.Ordinal))
        {
            return "Outcome: completed in one direct LLM call.";
        }

        if (!string.Equals(executionKind, BackendExecutionProjectionTags.DeliberationKind, StringComparison.Ordinal))
        {
            return $"Outcome: completed via {DefaultIfBlank(executionKind, "unknown")} execution.";
        }

        if (executionProjection?.Degraded == true || executionProjection?.Recoveries?.Length > 0)
        {
            var completedStages = FormatStages(executionProjection?.CompletedStages);
            var recoveries = FormatRecoveries(executionProjection?.Recoveries);
            return $"Outcome: deliberation completed in degraded mode after {completedStages}. Recovery: {recoveries}.";
        }

        return "Outcome: deliberation completed through planner, draft, and rewrite.";
    }

    private static string BuildFailureOutcomeSummary(
        string executionKind,
        BackendExecutionProjection? executionProjection,
        string? error)
    {
        var normalizedError = DefaultIfBlank(error, "unknown error");

        if (string.Equals(executionKind, BackendExecutionProjectionTags.DeliberationKind, StringComparison.Ordinal))
        {
            var failedStage = DefaultIfBlank(executionProjection?.FailedStage, "unknown stage");
            var completedStages = FormatStages(executionProjection?.CompletedStages);
            return string.IsNullOrWhiteSpace(completedStages)
                ? $"Outcome: deliberation failed during {failedStage}. Error: {normalizedError}."
                : $"Outcome: deliberation failed during {failedStage} after {completedStages}. Error: {normalizedError}.";
        }

        if (string.Equals(executionKind, BackendExecutionProjectionTags.LocalCapabilityReplyKind, StringComparison.Ordinal))
        {
            return $"Outcome: local capability reply failed before the answer could be returned. Error: {normalizedError}.";
        }

        return $"Outcome: {DefaultIfBlank(executionKind, "unknown")} execution failed. Error: {normalizedError}.";
    }

    private static LatestTurnCandidate? ResolveLatestTurn(BackendRuntimeSnapshotViewState snapshot)
    {
        var candidates = new List<LatestTurnCandidate>();

        addRequest("QQ", snapshot.LastQqLlmRequest);
        addFailure("QQ", snapshot.LastQqLlmFailure);
        addRequest("WeChat", snapshot.LastWechatLlmRequest);
        addFailure("WeChat", snapshot.LastWechatLlmFailure);

        return candidates
            .OrderByDescending(static candidate => candidate.CapturedAt)
            .FirstOrDefault();

        void addRequest(string channelLabel, BackendLlmRequestStatus? request)
        {
            if (request is null || !DateTimeOffset.TryParse(request.CapturedAt, out var parsedCapturedAt))
            {
                return;
            }

            candidates.Add(new LatestTurnCandidate(channelLabel, LatestTurnKind.Request, parsedCapturedAt, request, null));
        }

        void addFailure(string channelLabel, BackendLlmFailureStatus? failure)
        {
            if (failure is null || !DateTimeOffset.TryParse(failure.CapturedAt, out var parsedCapturedAt))
            {
                return;
            }

            candidates.Add(new LatestTurnCandidate(channelLabel, LatestTurnKind.Failure, parsedCapturedAt, null, failure));
        }
    }

    private static string FormatTrigger(BackendDecisionSummary? summary, string? matchedPrefix)
    {
        var triggerKind = DefaultIfBlank(summary?.Trigger?.Kind, "default");
        var effectiveMatchedPrefix = DefaultIfBlank(summary?.Trigger?.MatchedPrefix, DefaultIfBlank(summary?.MatchedPrefix, DefaultIfBlank(matchedPrefix, string.Empty)));

        return triggerKind == "directive" && !string.IsNullOrWhiteSpace(effectiveMatchedPrefix)
            ? $"directive {effectiveMatchedPrefix}"
            : triggerKind;
    }

    private static string FormatReasonGroup(string[]? reasons, string emptyText)
    {
        return reasons is { Length: > 0 }
            ? string.Join(", ", reasons)
            : emptyText;
    }

    private static string FormatStages(string[]? stages)
    {
        var normalizedStages = stages?
            .Where(static stage => !string.IsNullOrWhiteSpace(stage))
            .ToArray() ?? [];

        return normalizedStages.Length > 0
            ? string.Join(" -> ", normalizedStages)
            : string.Empty;
    }

    private static string FormatRecoveries(string[]? recoveries)
    {
        var normalizedRecoveries = recoveries?
            .Where(static recovery => !string.IsNullOrWhiteSpace(recovery))
            .ToArray() ?? [];

        return normalizedRecoveries.Length > 0
            ? string.Join(", ", normalizedRecoveries)
            : "none";
    }

    private static string NormalizeExecutionKind(string? projectionKind, string? fallbackKind)
    {
        return DefaultIfBlank(projectionKind, DefaultIfBlank(fallbackKind, "unknown"));
    }

    private static string DefaultIfBlank(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private enum LatestTurnKind
    {
        Request,
        Failure
    }

    private sealed record LatestTurnCandidate(
        string ChannelLabel,
        LatestTurnKind Kind,
        DateTimeOffset CapturedAt,
        BackendLlmRequestStatus? Request,
        BackendLlmFailureStatus? Failure);
}
