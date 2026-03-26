using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class BackendRecentActivityViewStateHelper
{
    public static bool ShouldInclude(object? item, bool failuresOnly)
    {
        if (item is not BackendRecentActivityItem activityItem)
        {
            return false;
        }

        return !failuresOnly || activityItem.IsFailure;
    }

    public static BackendRecentActivityItem? ResolveVisibleSelection(
        IEnumerable<BackendRecentActivityItem> items,
        BackendRecentActivityItem? selectedItem,
        bool failuresOnly)
    {
        var visibleItems = (items ?? [])
            .Where((item) => ShouldInclude(item, failuresOnly))
            .ToList();

        if (selectedItem is not null && !string.IsNullOrWhiteSpace(selectedItem.EventKey))
        {
            var matchedItem = visibleItems.FirstOrDefault(
                (item) => string.Equals(item.EventKey, selectedItem.EventKey, StringComparison.Ordinal));

            if (matchedItem is not null)
            {
                return matchedItem;
            }
        }

        return visibleItems.FirstOrDefault();
    }
}
