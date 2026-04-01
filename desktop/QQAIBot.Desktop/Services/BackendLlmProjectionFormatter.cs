using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class BackendLlmProjectionFormatter
{
    public static string FormatRequestDetail(BackendLlmRequestStatus? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route))
        {
            return "No completed requests captured yet.";
        }

        var capturedAt = FormatCapturedAt(request.CapturedAt);
        var reasoning = string.IsNullOrWhiteSpace(request.EffectiveReasoningEffort) ? "none" : request.EffectiveReasoningEffort;
        var verbosity = string.IsNullOrWhiteSpace(request.EffectiveTextVerbosity) ? "none" : request.EffectiveTextVerbosity;
        var tools = BackendToolCatalog.FormatToolList(request.EffectiveTools);
        var suppressedTools = BackendToolCatalog.FormatSuppressedTools(request.SuppressedTools);
        var decisionSummary = FormatDecisionSummary(request);
        var executionSummary = BackendExecutionProjectionFormatter.Format(
            request.ExecutionProjection,
            request.ExecutionKind,
            request.ExecutionSummary);
        var modelSummary = FormatModelSummary(request);

        return $"At {capturedAt} | {decisionSummary} | {executionSummary} | {modelSummary} | reasoning={reasoning} | verbosity={verbosity} | tools={tools} | suppressed={suppressedTools} | images={request.ImageCount}";
    }

    public static string FormatRequestDecisionTrigger(BackendLlmRequestStatus? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route))
        {
            return "n/a";
        }

        var summary = request.DecisionSummary;

        if (summary is not null)
        {
            return FormatDecisionTrigger(summary);
        }

        if (!string.IsNullOrWhiteSpace(request.MatchedPrefix))
        {
            return $"directive:{request.MatchedPrefix}";
        }

        return "default";
    }

    public static string FormatRequestDecisionCapability(BackendLlmRequestStatus? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route))
        {
            return "n/a";
        }

        return FormatDecisionReasonGroup(request.DecisionSummary?.ReasonGroups?.CapabilityReasons, "default");
    }

    public static string FormatRequestDecisionUpgrade(BackendLlmRequestStatus? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route))
        {
            return "n/a";
        }

        return FormatDecisionReasonGroup(request.DecisionSummary?.ReasonGroups?.UpgradeReasons, "none");
    }

    public static string FormatRequestedCapabilities(BackendLlmRequestStatus? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route))
        {
            return "n/a";
        }

        var requested = request.DecisionSummary?.RequestedCapabilities;
        var requestedTools = BackendToolCatalog.DeriveRequestedToolKinds(
            request.RequestedTools ?? request.DecisionSummary?.RequestedTools,
            requested,
            request.EffectiveTools);
        var suppressedTools = request.SuppressedTools?.Length > 0
            ? request.SuppressedTools
            : request.DecisionSummary?.SuppressedTools
                ?? [];

        if (requested is not null)
        {
            return $"reasoning={DefaultIfBlank(requested.ReasoningEffort, "none")} | verbosity={DefaultIfBlank(requested.TextVerbosity, "none")} | tools={BackendToolCatalog.FormatToolList(requestedTools)} | suppressed={BackendToolCatalog.FormatSuppressedTools(suppressedTools)}";
        }

        return $"reasoning={DefaultIfBlank(request.EffectiveReasoningEffort, "none")} | verbosity={DefaultIfBlank(request.EffectiveTextVerbosity, "none")} | tools={BackendToolCatalog.FormatToolList(request.EffectiveTools)} | suppressed={BackendToolCatalog.FormatSuppressedTools(suppressedTools)}";
    }

    public static string FormatFailureDetail(BackendLlmFailureStatus? failure)
    {
        if (failure is null || string.IsNullOrWhiteSpace(failure.Route))
        {
            return "No failures captured yet.";
        }

        return $"At {FormatCapturedAt(failure.CapturedAt)} | trigger={FormatFailureTrigger(failure)} | capability={FormatFailureCapability(failure)} | upgrade={FormatFailureUpgrade(failure)} | {BackendExecutionProjectionFormatter.Format(failure.ExecutionProjection, failure.ExecutionKind, failure.ExecutionSummary)} | error={FormatFailureError(failure)}";
    }

    public static string FormatFailureTrigger(BackendLlmFailureStatus? failure)
    {
        if (failure is null || string.IsNullOrWhiteSpace(failure.Route))
        {
            return "n/a";
        }

        var summary = failure.DecisionSummary;

        if (summary is not null)
        {
            return FormatDecisionTrigger(summary);
        }

        if (!string.IsNullOrWhiteSpace(failure.MatchedPrefix))
        {
            return $"directive:{failure.MatchedPrefix}";
        }

        return "default";
    }

    public static string FormatFailureCapability(BackendLlmFailureStatus? failure)
    {
        if (failure is null || string.IsNullOrWhiteSpace(failure.Route))
        {
            return "n/a";
        }

        return FormatDecisionReasonGroup(failure.DecisionSummary?.ReasonGroups?.CapabilityReasons, "default");
    }

    public static string FormatFailureUpgrade(BackendLlmFailureStatus? failure)
    {
        if (failure is null || string.IsNullOrWhiteSpace(failure.Route))
        {
            return "n/a";
        }

        return FormatDecisionReasonGroup(failure.DecisionSummary?.ReasonGroups?.UpgradeReasons, "none");
    }

    public static string FormatFailureError(BackendLlmFailureStatus? failure)
    {
        if (failure is null || string.IsNullOrWhiteSpace(failure.Route))
        {
            return "n/a";
        }

        return string.IsNullOrWhiteSpace(failure.Error) ? "unknown" : failure.Error;
    }

    private static string FormatDecisionSummary(BackendLlmRequestStatus request)
    {
        var summary = request.DecisionSummary;

        if (summary is null)
        {
            var routeReason = string.IsNullOrWhiteSpace(request.RouteReason) ? "default" : request.RouteReason;
            return $"reason={routeReason}";
        }

        var trigger = FormatDecisionTrigger(summary);
        var capabilityReasons = FormatDecisionReasonGroup(summary.ReasonGroups?.CapabilityReasons, "default");
        var upgradeReasons = FormatDecisionReasonGroup(summary.ReasonGroups?.UpgradeReasons, "none");

        return $"trigger={trigger} | capability={capabilityReasons} | upgrade={upgradeReasons}";
    }

    private static string FormatDecisionTrigger(BackendDecisionSummary summary)
    {
        var triggerKind = string.IsNullOrWhiteSpace(summary.Trigger?.Kind)
            ? "default"
            : summary.Trigger.Kind;
        var matchedPrefix = string.IsNullOrWhiteSpace(summary.Trigger?.MatchedPrefix)
            ? summary.MatchedPrefix
            : summary.Trigger!.MatchedPrefix;

        return triggerKind == "directive" && !string.IsNullOrWhiteSpace(matchedPrefix)
            ? $"directive:{matchedPrefix}"
            : triggerKind;
    }

    private static string FormatDecisionReasonGroup(string[]? reasons, string emptyValue)
    {
        return reasons is { Length: > 0 }
            ? string.Join("+", reasons)
            : emptyValue;
    }

    private static string DefaultIfBlank(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string FormatModelSummary(BackendLlmRequestStatus request)
    {
        var actualModel = DefaultIfBlank(request.Model, "unknown-model");
        var configuredModel = DefaultIfBlank(request.ConfiguredModel, actualModel);

        return string.Equals(actualModel, configuredModel, StringComparison.Ordinal)
            ? $"model={actualModel}"
            : $"model={actualModel} | configured_model={configuredModel}";
    }

    private static string FormatCapturedAt(string capturedAt)
    {
        if (DateTimeOffset.TryParse(capturedAt, out var parsed))
        {
            return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        return string.IsNullOrWhiteSpace(capturedAt) ? "unknown" : capturedAt;
    }
}
