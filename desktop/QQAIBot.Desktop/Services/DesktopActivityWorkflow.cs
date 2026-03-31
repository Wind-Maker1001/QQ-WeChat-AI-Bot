using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class DesktopActivityWorkflow
{
    public static DesktopShellSourceState UpdateBackendRoot(
        DesktopShellSourceState sourceState,
        string backendRootPath,
        bool backendRootDetected)
    {
        return sourceState with
        {
            LocalDocumentSourceState = sourceState.LocalDocumentSourceState with
            {
                BackendRootPath = backendRootPath ?? string.Empty,
                BackendRootDetected = backendRootDetected
            },
            SnapshotSourceState = new DesktopSnapshotSourceState()
        };
    }

    public static DesktopShellSourceState ClearQqActivityHistory(DesktopShellSourceState sourceState)
    {
        return sourceState with
        {
            RecentActivityState = sourceState.RecentActivityState with
            {
                QqRecentActivities = [],
                LastQqRequestEventKey = string.Empty,
                LastQqFailureEventKey = string.Empty,
                PinSelectedQqActivity = false,
                SelectedQqRecentActivity = null
            }
        };
    }

    public static DesktopShellSourceState ClearWechatActivityHistory(DesktopShellSourceState sourceState)
    {
        return sourceState with
        {
            RecentActivityState = sourceState.RecentActivityState with
            {
                WechatRecentActivities = [],
                LastWechatRequestEventKey = string.Empty,
                LastWechatFailureEventKey = string.Empty,
                PinSelectedWechatActivity = false,
                SelectedWechatRecentActivity = null
            }
        };
    }

    public static DesktopShellSourceState ApplyRestoredActivityState(
        DesktopShellSourceState sourceState,
        BackendActivityRestoreProjection projection)
    {
        return sourceState with
        {
            RecentActivityState = sourceState.RecentActivityState with
            {
                QqRecentActivities = projection.QqRecentActivities,
                WechatRecentActivities = projection.WechatRecentActivities,
                PinSelectedQqActivity = projection.PinSelectedQqActivity,
                PinSelectedWechatActivity = projection.PinSelectedWechatActivity,
                ShowOnlyQqFailures = projection.ShowOnlyQqFailures,
                ShowOnlyWechatFailures = projection.ShowOnlyWechatFailures,
                SelectedQqRecentActivity = projection.SelectedQqRecentActivity,
                SelectedWechatRecentActivity = projection.SelectedWechatRecentActivity
            }
        };
    }

    public static DesktopShellSourceState ApplyActivitySelection(
        DesktopShellSourceState sourceState,
        bool? pinSelectedQqActivity = null,
        bool? pinSelectedWechatActivity = null,
        bool? showOnlyQqFailures = null,
        bool? showOnlyWechatFailures = null,
        BackendRecentActivityItem? selectedQqRecentActivity = null,
        BackendRecentActivityItem? selectedWechatRecentActivity = null,
        bool updateQqSelection = false,
        bool updateWechatSelection = false)
    {
        var nextState = sourceState.RecentActivityState with
        {
            PinSelectedQqActivity = pinSelectedQqActivity ?? sourceState.RecentActivityState.PinSelectedQqActivity,
            PinSelectedWechatActivity = pinSelectedWechatActivity ?? sourceState.RecentActivityState.PinSelectedWechatActivity,
            ShowOnlyQqFailures = showOnlyQqFailures ?? sourceState.RecentActivityState.ShowOnlyQqFailures,
            ShowOnlyWechatFailures = showOnlyWechatFailures ?? sourceState.RecentActivityState.ShowOnlyWechatFailures,
            SelectedQqRecentActivity = updateQqSelection ? selectedQqRecentActivity : sourceState.RecentActivityState.SelectedQqRecentActivity,
            SelectedWechatRecentActivity = updateWechatSelection ? selectedWechatRecentActivity : sourceState.RecentActivityState.SelectedWechatRecentActivity
        };

        if (showOnlyQqFailures.HasValue)
        {
            nextState = nextState with
            {
                SelectedQqRecentActivity = BackendRecentActivityCoordinator.ResolveSelectionAfterFilterChange(
                    nextState.QqRecentActivities,
                    nextState.SelectedQqRecentActivity,
                    nextState.ShowOnlyQqFailures)
            };
        }

        if (showOnlyWechatFailures.HasValue)
        {
            nextState = nextState with
            {
                SelectedWechatRecentActivity = BackendRecentActivityCoordinator.ResolveSelectionAfterFilterChange(
                    nextState.WechatRecentActivities,
                    nextState.SelectedWechatRecentActivity,
                    nextState.ShowOnlyWechatFailures)
            };
        }

        return sourceState with
        {
            RecentActivityState = nextState
        };
    }
}
