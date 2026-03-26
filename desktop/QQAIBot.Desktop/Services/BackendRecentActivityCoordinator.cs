using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class BackendRecentActivityCoordinator
{
    public static BackendRecentActivityProjectionResult ProjectRuntimeUpdate(
        BackendLlmRequestStatus? request,
        BackendLlmFailureStatus? failure,
        IEnumerable<BackendRecentActivityItem> existingItems,
        string? lastRequestEventKey,
        string? lastFailureEventKey,
        bool isPinned,
        BackendRecentActivityItem? selectedItem,
        int maxItems = 6)
    {
        return BackendRecentActivityProjector.Project(
            request,
            failure,
            existingItems,
            lastRequestEventKey,
            lastFailureEventKey,
            isPinned,
            selectedItem,
            maxItems);
    }

    public static BackendRecentActivityItem? ResolveSelectionAfterFilterChange(
        IEnumerable<BackendRecentActivityItem> items,
        BackendRecentActivityItem? selectedItem,
        bool failuresOnly)
    {
        return BackendRecentActivityViewStateHelper.ResolveVisibleSelection(
            items,
            selectedItem,
            failuresOnly);
    }

    public static BackendRecentActivityItem? ResolveSelectionAfterRestore(
        DesktopActivityStatePolicy activityStatePolicy,
        IEnumerable<BackendRecentActivityItem> items,
        string? selectedEventKey,
        bool failuresOnly)
    {
        var restoredSelection = activityStatePolicy.ResolveSelectedItem(items, selectedEventKey);

        return BackendRecentActivityViewStateHelper.ResolveVisibleSelection(
            items,
            restoredSelection,
            failuresOnly);
    }
}
