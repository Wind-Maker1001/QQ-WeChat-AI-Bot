using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class BackendRecentActivityProjector
{
    public static BackendRecentActivityProjectionResult Project(
        BackendLlmRequestStatus? request,
        BackendLlmFailureStatus? failure,
        IEnumerable<BackendRecentActivityItem> existingItems,
        string? lastRequestEventKey,
        string? lastFailureEventKey,
        bool isPinned,
        BackendRecentActivityItem? selectedItem,
        int maxItems = 6)
    {
        var items = existingItems?.ToList() ?? [];
        var nextLastRequestEventKey = lastRequestEventKey ?? string.Empty;
        var nextLastFailureEventKey = lastFailureEventKey ?? string.Empty;
        var hadSelection = selectedItem is not null;
        BackendRecentActivityItem? newestInserted = null;
        var entries = new List<(DateTimeOffset? CapturedAt, string Key, BackendRecentActivityItem Item, bool IsRequest)>();

        if (request is not null && !string.IsNullOrWhiteSpace(request.Route))
        {
            var key = BuildRequestEventKey(request);

            if (!string.Equals(key, nextLastRequestEventKey, StringComparison.Ordinal))
            {
                entries.Add((ParseCapturedAt(request.CapturedAt), key, BuildRequestActivityItem(request), true));
            }
        }

        if (failure is not null && !string.IsNullOrWhiteSpace(failure.Route))
        {
            var key = BuildFailureEventKey(failure);

            if (!string.Equals(key, nextLastFailureEventKey, StringComparison.Ordinal))
            {
                entries.Add((ParseCapturedAt(failure.CapturedAt), key, BuildFailureActivityItem(failure), false));
            }
        }

        foreach (var entry in entries.OrderBy(static entry => entry.CapturedAt ?? DateTimeOffset.MinValue))
        {
            InsertRecentActivity(items, entry.Item, maxItems);
            newestInserted = entry.Item;

            if (entry.IsRequest)
            {
                nextLastRequestEventKey = entry.Key;
            }
            else
            {
                nextLastFailureEventKey = entry.Key;
            }
        }

        var nextSelectedItem = selectedItem;

        if (!hadSelection && newestInserted is not null)
        {
            nextSelectedItem = newestInserted;
        }
        else if (hadSelection && newestInserted is not null && !isPinned)
        {
            nextSelectedItem = newestInserted;
        }

        if (nextSelectedItem is not null && !items.Contains(nextSelectedItem))
        {
            nextSelectedItem = items.FirstOrDefault();
        }

        return new BackendRecentActivityProjectionResult(
            items,
            nextLastRequestEventKey,
            nextLastFailureEventKey,
            nextSelectedItem);
    }

    private static void InsertRecentActivity(
        ICollection<BackendRecentActivityItem> recentActivityItems,
        BackendRecentActivityItem item,
        int maxItems)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Summary))
        {
            return;
        }

        if (recentActivityItems is List<BackendRecentActivityItem> list)
        {
            list.Insert(0, item);

            while (list.Count > maxItems)
            {
                list.RemoveAt(list.Count - 1);
            }
        }
    }

    private static string BuildRequestEventKey(BackendLlmRequestStatus request)
    {
        return $"request|{request.CapturedAt}|{request.Route}|{request.ResponseId}|{request.Model}";
    }

    private static string BuildFailureEventKey(BackendLlmFailureStatus failure)
    {
        return $"failure|{failure.CapturedAt}|{failure.Route}|{failure.Error}";
    }

    private static BackendRecentActivityItem BuildRequestActivityItem(BackendLlmRequestStatus request)
    {
        return new BackendRecentActivityItem
        {
            EventKey = BuildRequestEventKey(request),
            CapturedAt = request.CapturedAt,
            EventType = "Request",
            Summary = $"{request.Route} / {DefaultIfBlank(request.Model, "unknown-model")} / {DefaultIfBlank(request.EffectiveApiStyle, "unknown-api")}",
            Meta = BackendActivityProjectionFormatter.FormatCapturedAt(request.CapturedAt),
            Detail = BackendLlmProjectionFormatter.FormatRequestDetail(request),
            IsFailure = false
        };
    }

    private static BackendRecentActivityItem BuildFailureActivityItem(BackendLlmFailureStatus failure)
    {
        return new BackendRecentActivityItem
        {
            EventKey = BuildFailureEventKey(failure),
            CapturedAt = failure.CapturedAt,
            EventType = "Failure",
            Summary = $"{failure.Route} / {DefaultIfBlank(failure.Error, "unknown")}",
            Meta = BackendActivityProjectionFormatter.FormatCapturedAt(failure.CapturedAt),
            Detail = BackendLlmProjectionFormatter.FormatFailureDetail(failure),
            IsFailure = true
        };
    }

    private static DateTimeOffset? ParseCapturedAt(string? capturedAt)
    {
        return DateTimeOffset.TryParse(capturedAt, out var parsed) ? parsed : null;
    }

    private static string DefaultIfBlank(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}

public sealed record BackendRecentActivityProjectionResult(
    IReadOnlyList<BackendRecentActivityItem> Items,
    string LastRequestEventKey,
    string LastFailureEventKey,
    BackendRecentActivityItem? SelectedItem);
