using System.Collections.ObjectModel;

using QQAIBot.Desktop.Models;
using QQAIBot.Desktop.ViewModels;

namespace QQAIBot.Desktop.Services;

public static class BackendRuntimeSnapshotViewHelper
{
    private static readonly string[] RuntimeSnapshotPropertyNames =
    [
        nameof(MainViewModel.WechatRuntimeStateText),
        nameof(MainViewModel.RuntimeReadyText),
        nameof(MainViewModel.WechatRuntimeReadyText),
        nameof(MainViewModel.WechatBridgeStateText),
        nameof(MainViewModel.WechatWorkerProcessText),
        nameof(MainViewModel.ShowOnlyQqFailures),
        nameof(MainViewModel.ShowOnlyWechatFailures),
        nameof(MainViewModel.PinSelectedQqActivity),
        nameof(MainViewModel.PinSelectedWechatActivity),
        nameof(MainViewModel.LatestQqActivitySummaryText),
        nameof(MainViewModel.LatestQqRecentActivityText),
        nameof(MainViewModel.SelectedQqRecentActivity),
        nameof(MainViewModel.SelectedQqRecentActivitySummaryText),
        nameof(MainViewModel.SelectedQqRecentActivityMetaText),
        nameof(MainViewModel.SelectedQqRecentActivityDetailText),
        nameof(MainViewModel.LatestQqActivityStateText),
        nameof(MainViewModel.LatestQqLatestSuccessText),
        nameof(MainViewModel.LatestQqLatestFailureText),
        nameof(MainViewModel.LatestQqRecoveryText),
        nameof(MainViewModel.LatestQqLlmSummaryText),
        nameof(MainViewModel.LatestQqLlmDetailText),
        nameof(MainViewModel.LatestQqRequestTimelineText),
        nameof(MainViewModel.LatestQqDecisionTriggerText),
        nameof(MainViewModel.LatestQqDecisionCapabilityText),
        nameof(MainViewModel.LatestQqDecisionUpgradeText),
        nameof(MainViewModel.LatestQqRequestedCapabilitiesText),
        nameof(MainViewModel.LatestQqFailureSummaryText),
        nameof(MainViewModel.LatestQqFailureTimelineText),
        nameof(MainViewModel.LatestQqFailureTriggerText),
        nameof(MainViewModel.LatestQqFailureCapabilityText),
        nameof(MainViewModel.LatestQqFailureUpgradeText),
        nameof(MainViewModel.LatestQqFailureErrorText),
        nameof(MainViewModel.LatestWechatActivitySummaryText),
        nameof(MainViewModel.LatestWechatRecentActivityText),
        nameof(MainViewModel.SelectedWechatRecentActivity),
        nameof(MainViewModel.SelectedWechatRecentActivitySummaryText),
        nameof(MainViewModel.SelectedWechatRecentActivityMetaText),
        nameof(MainViewModel.SelectedWechatRecentActivityDetailText),
        nameof(MainViewModel.LatestWechatActivityStateText),
        nameof(MainViewModel.LatestWechatLatestSuccessText),
        nameof(MainViewModel.LatestWechatLatestFailureText),
        nameof(MainViewModel.LatestWechatRecoveryText),
        nameof(MainViewModel.LatestWechatLlmSummaryText),
        nameof(MainViewModel.LatestWechatLlmDetailText),
        nameof(MainViewModel.LatestWechatRequestTimelineText),
        nameof(MainViewModel.LatestWechatDecisionTriggerText),
        nameof(MainViewModel.LatestWechatDecisionCapabilityText),
        nameof(MainViewModel.LatestWechatDecisionUpgradeText),
        nameof(MainViewModel.LatestWechatRequestedCapabilitiesText),
        nameof(MainViewModel.LatestWechatFailureSummaryText),
        nameof(MainViewModel.LatestWechatFailureTimelineText),
        nameof(MainViewModel.LatestWechatFailureTriggerText),
        nameof(MainViewModel.LatestWechatFailureCapabilityText),
        nameof(MainViewModel.LatestWechatFailureUpgradeText),
        nameof(MainViewModel.LatestWechatFailureErrorText)
    ];

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
        foreach (var propertyName in RuntimeSnapshotPropertyNames)
        {
            notifyPropertyChanged(propertyName);
        }
    }
}
