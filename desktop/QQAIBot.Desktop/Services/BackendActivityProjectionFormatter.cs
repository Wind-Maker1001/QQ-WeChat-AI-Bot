using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class BackendActivityProjectionFormatter
{
    public static string FormatRequestSummary(BackendLlmRequestStatus? request, string emptyText)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route) || string.IsNullOrWhiteSpace(request.Model))
        {
            return emptyText;
        }

        var apiStyle = string.IsNullOrWhiteSpace(request.EffectiveApiStyle) ? "unknown" : request.EffectiveApiStyle;
        return $"{request.Route} / {request.Model} / {apiStyle}";
    }

    public static string FormatActivitySummary(
        BackendLlmRequestStatus? request,
        BackendLlmFailureStatus? failure,
        string emptyText)
    {
        if (request is null && failure is null)
        {
            return emptyText;
        }

        var requestCapturedAt = ParseCapturedAt(request?.CapturedAt);
        var failureCapturedAt = ParseCapturedAt(failure?.CapturedAt);

        if (failureCapturedAt is not null &&
            (requestCapturedAt is null || failureCapturedAt >= requestCapturedAt))
        {
            return $"Latest event: failure at {FormatCapturedAt(failure!.CapturedAt)}";
        }

        if (requestCapturedAt is not null)
        {
            return $"Latest event: request at {FormatCapturedAt(request!.CapturedAt)}";
        }

        return emptyText;
    }

    public static string FormatRecentActivity(IEnumerable<BackendRecentActivityItem> items, string emptyText)
    {
        var visibleLines = items
            .Where(static item => item is not null && !string.IsNullOrWhiteSpace(item.Summary))
            .Select(static item => $"{item.EventType} | {item.Summary}")
            .ToArray();
        return visibleLines.Length > 0 ? string.Join(Environment.NewLine, visibleLines) : emptyText;
    }

    public static string FormatActivityState(
        BackendLlmRequestStatus? request,
        BackendLlmFailureStatus? failure)
    {
        if (request is null && failure is null)
        {
            return "No activity";
        }

        var requestCapturedAt = ParseCapturedAt(request?.CapturedAt);
        var failureCapturedAt = ParseCapturedAt(failure?.CapturedAt);

        if (requestCapturedAt is not null && failureCapturedAt is not null)
        {
            if (requestCapturedAt > failureCapturedAt)
            {
                return "Recovered after failure";
            }

            if (failureCapturedAt > requestCapturedAt)
            {
                return "Failure is latest event";
            }

            return "Request and failure captured";
        }

        if (requestCapturedAt is not null)
        {
            return "Latest event is successful request";
        }

        return "Latest event is failure";
    }

    public static string FormatLatestSuccess(BackendLlmRequestStatus? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route))
        {
            return "Success | not captured";
        }

        return $"Success | {FormatCapturedAt(request.CapturedAt)}";
    }

    public static string FormatLatestFailure(BackendLlmFailureStatus? failure)
    {
        if (failure is null || string.IsNullOrWhiteSpace(failure.Route))
        {
            return "Failure | not captured";
        }

        return $"Failure | {FormatCapturedAt(failure.CapturedAt)}";
    }

    public static string FormatRecoveryState(
        BackendLlmRequestStatus? request,
        BackendLlmFailureStatus? failure)
    {
        var requestCapturedAt = ParseCapturedAt(request?.CapturedAt);
        var failureCapturedAt = ParseCapturedAt(failure?.CapturedAt);

        if (requestCapturedAt is null || failureCapturedAt is null)
        {
            return "Recovery | not observed";
        }

        if (requestCapturedAt > failureCapturedAt)
        {
            return $"Recovery | {FormatCapturedAt(request!.CapturedAt)}";
        }

        return "Recovery | pending";
    }

    public static string FormatRequestTimeline(BackendLlmRequestStatus? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route))
        {
            return "Request | n/a | not captured";
        }

        return $"Request | {FormatCapturedAt(request.CapturedAt)} | completed";
    }

    public static string FormatFailureSummary(BackendLlmFailureStatus? failure, string emptyText)
    {
        if (failure is null || string.IsNullOrWhiteSpace(failure.Route))
        {
            return emptyText;
        }

        return $"{failure.Route} / {FormatCapturedAt(failure.CapturedAt)}";
    }

    public static string FormatFailureTimeline(BackendLlmFailureStatus? failure)
    {
        if (failure is null || string.IsNullOrWhiteSpace(failure.Route))
        {
            return "Failure | n/a | not captured";
        }

        return $"Failure | {FormatCapturedAt(failure.CapturedAt)} | failed";
    }

    private static DateTimeOffset? ParseCapturedAt(string? capturedAt)
    {
        if (DateTimeOffset.TryParse(capturedAt, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public static string FormatCapturedAt(string capturedAt)
    {
        if (DateTimeOffset.TryParse(capturedAt, out var parsed))
        {
            return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        return string.IsNullOrWhiteSpace(capturedAt) ? "unknown" : capturedAt;
    }
}
