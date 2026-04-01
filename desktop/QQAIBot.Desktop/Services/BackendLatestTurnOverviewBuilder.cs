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
        var configuredModel = DefaultIfBlank(request.ConfiguredModel, model);
        var apiStyle = DefaultIfBlank(request.EffectiveApiStyle, DefaultIfBlank(request.ConfiguredApiStyle, "unknown"));
        var capturedAt = BackendActivityProjectionFormatter.FormatCapturedAt(request.CapturedAt);
        var isLocalReply = string.Equals(executionKind, BackendExecutionProjectionTags.LocalCapabilityReplyKind, StringComparison.Ordinal);

        return new DesktopLatestTurnOverview
        {
            State = DesktopHealthState.Good,
            Headline = isLocalReply
                ? $"最新一轮：{channelLabel} 在 {capturedAt} 已本地回复。"
                : $"最新一轮：{channelLabel} 在 {capturedAt} 已完成。",
            Summary = isLocalReply
                ? $"摘要：这一轮停留在 {route} 路由上，并且没有发起 LLM 请求就直接本地回复了。"
                : $"摘要：这一轮使用了 {route} / {model} / {apiStyle}。",
            Capabilities = BuildCapabilitySummary(
                request.RequestedTools ?? request.DecisionSummary?.RequestedTools,
                request.DecisionSummary?.RequestedCapabilities,
                request.EffectiveTools,
                request.ImageCount),
            Reason = BuildReasonSummary(
                request.DecisionSummary,
                request.MatchedPrefix,
                request.RouteReason),
            Outcome = BuildRequestOutcomeSummary(executionKind, request.ExecutionProjection),
            ActionLabel = "查看最近活动",
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
            Headline = $"最新一轮：{channelLabel} 在 {capturedAt} 失败。",
            Summary = $"摘要：{route} 路由这次没有成功完成。",
            Capabilities = BuildCapabilitySummary(
                failure.DecisionSummary?.RequestedTools,
                failure.DecisionSummary?.RequestedCapabilities,
                effectiveTools: null,
                imageCount: null),
            Reason = BuildReasonSummary(
                failure.DecisionSummary,
                failure.MatchedPrefix,
                failure.RouteReason),
            Outcome = BuildFailureOutcomeSummary(executionKind, failure.ExecutionProjection, failure.Error),
            ActionLabel = channelLabel == "QQ" ? "查看 QQ 失败" : "查看微信失败",
            ActionKey = channelLabel == "QQ"
                ? DesktopHealthActionKeys.FocusQqFailure
                : DesktopHealthActionKeys.FocusWechatFailure
        };
    }

    private static string BuildCapabilitySummary(
        BackendRequestedTools? requestedTools,
        BackendRequestedCapabilities? requestedCapabilities,
        string[]? effectiveTools,
        int? imageCount)
    {
        var resolvedTools = BackendToolCatalog.DeriveRequestedToolKinds(
            requestedTools,
            requestedCapabilities,
            effectiveTools);
        var webEnabled = BackendToolCatalog.ContainsTool(resolvedTools, "web_search");
        var codeEnabled = BackendToolCatalog.ContainsTool(resolvedTools, "code_interpreter");
        var imageText = imageCount is int count ? count.ToString() : "unknown";
        var toolsText = BackendToolCatalog.FormatToolList(resolvedTools);

        return $"能力：工具 {toolsText}，联网 {(webEnabled ? "开" : "关")}，代码 {(codeEnabled ? "开" : "关")}，图片 {imageText}。";
    }

    private static string BuildReasonSummary(
        BackendDecisionSummary? summary,
        string? matchedPrefix,
        string? routeReason)
    {
        if (summary is null && string.IsNullOrWhiteSpace(matchedPrefix) && string.IsNullOrWhiteSpace(routeReason))
        {
            return "原因：没有捕获到结构化决策原因。";
        }

        var trigger = FormatTrigger(summary, matchedPrefix);
        var capability = FormatReasonGroup(summary?.ReasonGroups?.CapabilityReasons, "default");
        var upgrade = FormatReasonGroup(summary?.ReasonGroups?.UpgradeReasons, "none");
        var routeReasonText = DefaultIfBlank(summary?.RouteReason, DefaultIfBlank(routeReason, "default"));

        return $"原因：触发 {trigger}；能力 {capability}；升级 {upgrade}；路由原因 {routeReasonText}。";
    }

    private static string BuildRequestOutcomeSummary(
        string executionKind,
        BackendExecutionProjection? executionProjection)
    {
        if (string.Equals(executionKind, BackendExecutionProjectionTags.LocalCapabilityReplyKind, StringComparison.Ordinal))
        {
            return "结果：未调用 LLM，直接在本地完成回复。";
        }

        if (executionProjection?.Recoveries?.Contains("provider-fallback-to-deepseek") == true)
        {
            return "Outcome: GPT request failed and DeepSeek fallback completed.";
        }

        if ((executionProjection?.Degraded == true || executionProjection?.Recoveries?.Length > 0) &&
            string.Equals(executionKind, BackendExecutionProjectionTags.DirectKind, StringComparison.Ordinal))
        {
            var recoveries = FormatRecoveries(executionProjection?.Recoveries);
            return $"Outcome: completed via degraded direct path. recoveries={recoveries}";
        }

        if (string.Equals(executionKind, BackendExecutionProjectionTags.DirectKind, StringComparison.Ordinal))
        {
            return "结果：通过一次直接 LLM 调用完成。";
        }

        if (!string.Equals(executionKind, BackendExecutionProjectionTags.DeliberationKind, StringComparison.Ordinal))
        {
            return $"结果：通过 {DefaultIfBlank(executionKind, "未知")} 执行路径完成。";
        }

        if (executionProjection?.Degraded == true || executionProjection?.Recoveries?.Length > 0)
        {
            var completedStages = FormatStages(executionProjection?.CompletedStages);
            var recoveries = FormatRecoveries(executionProjection?.Recoveries);
            if (executionProjection?.Recoveries?.Contains("provider-fallback-to-deepseek") == true)
            {
                return $"缁撴灉锛氳繖涓€杞湪 GPT 璋冪敤澶辫触鍚庡凡鑷姩鍒囨崲鍒?DeepSeek 瀹屾垚銆傛仮澶嶏細{recoveries}銆?";
            }
            return $"结果：审议流程在 {completedStages} 后以降级模式完成。恢复：{recoveries}。";
        }

        return "结果：审议流程依次完成了 planner、draft 和 rewrite。";
    }

    private static string BuildFailureOutcomeSummary(
        string executionKind,
        BackendExecutionProjection? executionProjection,
        string? error)
    {
        var normalizedError = DefaultIfBlank(error, "未知错误");

        if (string.Equals(executionKind, BackendExecutionProjectionTags.DeliberationKind, StringComparison.Ordinal))
        {
            var failedStage = DefaultIfBlank(executionProjection?.FailedStage, "unknown stage");
            var completedStages = FormatStages(executionProjection?.CompletedStages);
            return string.IsNullOrWhiteSpace(completedStages)
                ? $"结果：审议流程在 {failedStage} 阶段失败。错误：{normalizedError}。"
                : $"结果：审议流程在完成 {completedStages} 后，于 {failedStage} 阶段失败。错误：{normalizedError}。";
        }

        if (string.Equals(executionKind, BackendExecutionProjectionTags.LocalCapabilityReplyKind, StringComparison.Ordinal))
        {
            return $"结果：本地能力回复在返回答案前失败。错误：{normalizedError}。";
        }

        return $"结果：{DefaultIfBlank(executionKind, "未知")} 执行路径失败。错误：{normalizedError}。";
    }

    private static LatestTurnCandidate? ResolveLatestTurn(BackendRuntimeSnapshotViewState snapshot)
    {
        var candidates = new List<LatestTurnCandidate>();

        addRequest("QQ", snapshot.LastQqLlmRequest);
        addFailure("QQ", snapshot.LastQqLlmFailure);
        addRequest("微信", snapshot.LastWechatLlmRequest);
        addFailure("微信", snapshot.LastWechatLlmFailure);

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
