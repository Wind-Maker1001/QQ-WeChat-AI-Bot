using System.Collections.ObjectModel;

using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class BackendRuntimeSnapshotViewHelper
{
    public static void ReplaceRecentActivities(
        ObservableCollection<BackendRecentActivityItem> target,
        IEnumerable<BackendRecentActivityItem>? source)
    {
        target.Clear();

        if (source is null)
        {
            return;
        }

        foreach (var item in source)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.EventKey))
            {
                continue;
            }

            target.Add(item);
        }
    }

    public static void NotifyRuntimeSnapshotChanged(Action<string> notifyPropertyChanged)
    {
        foreach (var propertyName in DesktopShellPropertyCatalog.RuntimeSnapshotPropertyNames)
        {
            notifyPropertyChanged(propertyName);
        }
    }
}
