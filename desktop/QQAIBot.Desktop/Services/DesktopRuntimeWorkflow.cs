using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class DesktopRuntimeWorkflow
{
    public static DesktopShellSourceState ApplyRuntimeProjection(
        DesktopShellSourceState sourceState,
        BackendRuntimeSnapshotProjection projection)
    {
        return sourceState with
        {
            RecentActivityState = sourceState.RecentActivityState with
            {
                QqRecentActivities = projection.QqActivity.Items.ToArray(),
                WechatRecentActivities = projection.WechatActivity.Items.ToArray(),
                LastQqRequestEventKey = projection.QqActivity.LastRequestEventKey,
                LastQqFailureEventKey = projection.QqActivity.LastFailureEventKey,
                SelectedQqRecentActivity = projection.QqActivity.SelectedItem,
                LastWechatRequestEventKey = projection.WechatActivity.LastRequestEventKey,
                LastWechatFailureEventKey = projection.WechatActivity.LastFailureEventKey,
                SelectedWechatRecentActivity = projection.WechatActivity.SelectedItem
            },
            RuntimeSourceState = sourceState.RuntimeSourceState with
            {
                IsProcessRunning = projection.SnapshotState.RuntimeActive == true,
                RuntimeSnapshot = projection.SnapshotState
            }
        };
    }

    public static DesktopShellSourceState ApplyPollOutcome(
        DesktopShellSourceState sourceState,
        BackendControlApiPollState nextPollState,
        BackendRuntimeSnapshotViewState nextRuntimeSnapshot)
    {
        return sourceState with
        {
            RuntimeSourceState = sourceState.RuntimeSourceState with
            {
                ControlApiPollState = nextPollState,
                RuntimeSnapshot = nextRuntimeSnapshot
            }
        };
    }

    public static DesktopShellSourceState SetRecoveryInProgress(
        DesktopShellSourceState sourceState,
        bool recoveryInProgress)
    {
        return sourceState with
        {
            RuntimeSourceState = sourceState.RuntimeSourceState with
            {
                ControlApiRecoveryInProgress = recoveryInProgress
            }
        };
    }

    public static DesktopShellSourceState ResetControlApiFailureState(DesktopShellSourceState sourceState)
    {
        return sourceState with
        {
            RuntimeSourceState = sourceState.RuntimeSourceState with
            {
                ControlApiPollState = new BackendControlApiPollState(),
                ControlApiRecoveryInProgress = false
            }
        };
    }
}
