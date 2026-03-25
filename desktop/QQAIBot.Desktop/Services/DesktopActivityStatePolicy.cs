using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public sealed class DesktopActivityStatePolicy
{
    public static DesktopActivityStatePolicy Default { get; } = new();

    public int Version { get; init; } = 1;

    public int MaxRecentActivitiesPerChannel { get; init; } = 6;

    public TimeSpan RetentionWindow { get; init; } = TimeSpan.FromDays(14);

    public DesktopActivityState CreateDefaultState()
    {
        return new DesktopActivityState
        {
            Version = Version
        };
    }

    public DesktopActivityState? Normalize(DesktopActivityState? state)
    {
        if (state is null)
        {
            return CreateDefaultState();
        }

        if (state.Version != 0 && state.Version != Version)
        {
            return null;
        }

        var qqActivities = NormalizeItems(state.QqRecentActivities);
        var wechatActivities = NormalizeItems(state.WechatRecentActivities);

        return new DesktopActivityState
        {
            Version = Version,
            QqRecentActivities = qqActivities,
            WechatRecentActivities = wechatActivities,
            SelectedQqEventKey = ResolveSelectedEventKey(qqActivities, state.SelectedQqEventKey),
            SelectedWechatEventKey = ResolveSelectedEventKey(wechatActivities, state.SelectedWechatEventKey),
            PinSelectedQqActivity = state.PinSelectedQqActivity,
            PinSelectedWechatActivity = state.PinSelectedWechatActivity,
            ShowOnlyQqFailures = state.ShowOnlyQqFailures,
            ShowOnlyWechatFailures = state.ShowOnlyWechatFailures
        };
    }

    public string ResolveSelectedEventKey(
        IEnumerable<BackendRecentActivityItem> items,
        string? selectedEventKey)
    {
        return !string.IsNullOrWhiteSpace(selectedEventKey) &&
            items.Any((item) => string.Equals(item.EventKey, selectedEventKey, StringComparison.Ordinal))
            ? selectedEventKey
            : string.Empty;
    }

    public BackendRecentActivityItem? ResolveSelectedItem(
        IEnumerable<BackendRecentActivityItem> items,
        string? selectedEventKey)
    {
        var resolvedEventKey = ResolveSelectedEventKey(items, selectedEventKey);

        if (string.IsNullOrWhiteSpace(resolvedEventKey))
        {
            return null;
        }

        return items.FirstOrDefault((item) => string.Equals(item.EventKey, resolvedEventKey, StringComparison.Ordinal));
    }

    public DesktopActivityState CreateSnapshot(
        IEnumerable<BackendRecentActivityItem> qqActivities,
        IEnumerable<BackendRecentActivityItem> wechatActivities,
        BackendRecentActivityItem? selectedQqActivity,
        BackendRecentActivityItem? selectedWechatActivity,
        bool pinSelectedQqActivity,
        bool pinSelectedWechatActivity,
        bool showOnlyQqFailures,
        bool showOnlyWechatFailures)
    {
        var normalizedQqActivities = NormalizeItems(qqActivities);
        var normalizedWechatActivities = NormalizeItems(wechatActivities);

        return new DesktopActivityState
        {
            Version = Version,
            QqRecentActivities = normalizedQqActivities,
            WechatRecentActivities = normalizedWechatActivities,
            SelectedQqEventKey = ResolveSelectedEventKey(normalizedQqActivities, selectedQqActivity?.EventKey),
            SelectedWechatEventKey = ResolveSelectedEventKey(normalizedWechatActivities, selectedWechatActivity?.EventKey),
            PinSelectedQqActivity = pinSelectedQqActivity,
            PinSelectedWechatActivity = pinSelectedWechatActivity,
            ShowOnlyQqFailures = showOnlyQqFailures,
            ShowOnlyWechatFailures = showOnlyWechatFailures
        };
    }

    public bool IsDefaultState(DesktopActivityState state)
    {
        return state.QqRecentActivities.Count == 0 &&
            state.WechatRecentActivities.Count == 0 &&
            string.IsNullOrWhiteSpace(state.SelectedQqEventKey) &&
            string.IsNullOrWhiteSpace(state.SelectedWechatEventKey) &&
            !state.PinSelectedQqActivity &&
            !state.PinSelectedWechatActivity &&
            !state.ShowOnlyQqFailures &&
            !state.ShowOnlyWechatFailures;
    }

    public List<BackendRecentActivityItem> NormalizeItems(IEnumerable<BackendRecentActivityItem>? items)
    {
        if (items is null)
        {
            return [];
        }

        var cutoff = DateTimeOffset.UtcNow - RetentionWindow;

        return items
            .Where(static item => item is not null && !string.IsNullOrWhiteSpace(item.EventKey))
            .Where((item) => ShouldRetainItem(item, cutoff))
            .GroupBy(static item => item.EventKey, StringComparer.Ordinal)
            .Select(static group => group.First())
            .Take(MaxRecentActivitiesPerChannel)
            .Select(static item => new BackendRecentActivityItem
            {
                EventKey = item.EventKey,
                CapturedAt = item.CapturedAt ?? string.Empty,
                EventType = item.EventType ?? string.Empty,
                Summary = item.Summary ?? string.Empty,
                Meta = item.Meta ?? string.Empty,
                Detail = item.Detail ?? string.Empty,
                IsFailure = item.IsFailure
            })
            .ToList();
    }

    private static bool ShouldRetainItem(BackendRecentActivityItem item, DateTimeOffset cutoff)
    {
        if (string.IsNullOrWhiteSpace(item.CapturedAt))
        {
            return true;
        }

        return !DateTimeOffset.TryParse(item.CapturedAt, out var parsed) || parsed >= cutoff;
    }
}
