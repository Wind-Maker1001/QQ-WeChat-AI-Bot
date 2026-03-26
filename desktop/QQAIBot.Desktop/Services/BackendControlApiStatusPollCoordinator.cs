using Forms = System.Windows.Forms;

using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class BackendControlApiStatusPollCoordinator
{
    public static BackendControlApiStatusPollOutcome Evaluate(
        BackendRuntimeSnapshotViewState previousSnapshot,
        BackendControlApiPollState previousPollState,
        BackendRuntimeStatus? status,
        BackendControlApiFailure statusFailure,
        int recoveryAttemptThreshold,
        int outageNotificationThreshold)
    {
        if (status is null)
        {
            return statusFailure.Kind == BackendControlApiFailureKind.Unreachable
                ? EvaluateUnreachableFailure(
                    previousSnapshot,
                    previousPollState,
                    recoveryAttemptThreshold,
                    outageNotificationThreshold)
                : EvaluateImmediateFailure(previousSnapshot, previousPollState, statusFailure);
        }

        return EvaluateSuccess(previousSnapshot, previousPollState, status);
    }

    private static BackendControlApiStatusPollOutcome EvaluateImmediateFailure(
        BackendRuntimeSnapshotViewState previousSnapshot,
        BackendControlApiPollState previousPollState,
        BackendControlApiFailure statusFailure)
    {
        var nextSnapshot = previousSnapshot with
        {
            ControlApiReachable = true
        };
        var nextPollState = statusFailure.Kind == BackendControlApiFailureKind.Unauthorized
            ? previousPollState with
            {
                UnauthorizedNotified = true
            }
            : previousPollState;
        var logMessages = statusFailure.Kind == BackendControlApiFailureKind.Unauthorized &&
            !previousPollState.UnauthorizedNotified
            ? new[]
            {
                "Control API authentication failed. Update QQ_AI_BOT_CONTROL_API_TOKEN in the local .env."
            }
            : [];

        return new BackendControlApiStatusPollOutcome(
            ShouldApplyRuntimeStatus: false,
            ShouldAttemptRecovery: false,
            NextRuntimeSnapshot: nextSnapshot,
            NextPollState: nextPollState,
            LogMessages: logMessages,
            Notifications: []);
    }

    private static BackendControlApiStatusPollOutcome EvaluateUnreachableFailure(
        BackendRuntimeSnapshotViewState previousSnapshot,
        BackendControlApiPollState previousPollState,
        int recoveryAttemptThreshold,
        int outageNotificationThreshold)
    {
        var nextPollState = previousPollState with
        {
            UnauthorizedNotified = false,
            ConsecutiveFailures = previousPollState.ConsecutiveFailures + 1
        };
        var nextSnapshot = previousSnapshot with
        {
            ControlApiReachable = false,
            WechatBridgeConnected = false
        };
        var logMessages = new List<string>();
        var notifications = new List<TrayNotification>();

        if (!previousPollState.OutageNotified &&
            nextPollState.ConsecutiveFailures >= outageNotificationThreshold)
        {
            logMessages.Add("Control API became unreachable.");
            notifications.Add(new TrayNotification
            {
                Title = "QQ AI Bot",
                Message = "Control API is unreachable. The supervisor may be stopped or restarting.",
                Icon = Forms.ToolTipIcon.Warning
            });
            nextPollState = nextPollState with
            {
                OutageNotified = true
            };
        }

        return new BackendControlApiStatusPollOutcome(
            ShouldApplyRuntimeStatus: false,
            ShouldAttemptRecovery: nextPollState.ConsecutiveFailures == recoveryAttemptThreshold,
            NextRuntimeSnapshot: nextSnapshot,
            NextPollState: nextPollState,
            LogMessages: logMessages,
            Notifications: notifications);
    }

    private static BackendControlApiStatusPollOutcome EvaluateSuccess(
        BackendRuntimeSnapshotViewState previousSnapshot,
        BackendControlApiPollState previousPollState,
        BackendRuntimeStatus status)
    {
        var logMessages = new List<string>();
        var notifications = new List<TrayNotification>();

        if (previousSnapshot.ControlApiReachable == false && previousPollState.OutageNotified)
        {
            logMessages.Add("Control API became reachable again.");
            notifications.Add(new TrayNotification
            {
                Title = "QQ AI Bot",
                Message = "Control API is reachable again.",
                Icon = Forms.ToolTipIcon.Info
            });
        }

        if (previousSnapshot.WorkerProcessId is int previousWorkerPid &&
            status.WorkerProcessId is int currentWorkerPid &&
            previousWorkerPid != currentWorkerPid)
        {
            logMessages.Add($"Worker restarted: {previousWorkerPid} -> {currentWorkerPid}");
            notifications.Add(new TrayNotification
            {
                Title = "QQ AI Bot",
                Message = $"Worker restarted automatically ({previousWorkerPid} -> {currentWorkerPid}).",
                Icon = Forms.ToolTipIcon.Info
            });
        }

        if (previousSnapshot.WechatWorkerProcessId is int previousWechatWorkerPid &&
            status.WechatWorkerProcessId is int currentWechatWorkerPid &&
            previousWechatWorkerPid != currentWechatWorkerPid)
        {
            logMessages.Add($"Wechat worker restarted: {previousWechatWorkerPid} -> {currentWechatWorkerPid}");
            notifications.Add(new TrayNotification
            {
                Title = "QQ AI Bot",
                Message = $"Wechat worker restarted automatically ({previousWechatWorkerPid} -> {currentWechatWorkerPid}).",
                Icon = Forms.ToolTipIcon.Info
            });
        }

        if (previousSnapshot.RuntimeActive == true && !status.RuntimeActive)
        {
            logMessages.Add("Runtime became inactive.");
            notifications.Add(new TrayNotification
            {
                Title = "QQ AI Bot",
                Message = "Runtime is stopped.",
                Icon = Forms.ToolTipIcon.Warning
            });
        }

        return new BackendControlApiStatusPollOutcome(
            ShouldApplyRuntimeStatus: true,
            ShouldAttemptRecovery: false,
            NextRuntimeSnapshot: previousSnapshot with
            {
                ControlApiReachable = true
            },
            NextPollState: new BackendControlApiPollState(),
            LogMessages: logMessages,
            Notifications: notifications);
    }
}

public sealed record BackendControlApiStatusPollOutcome(
    bool ShouldApplyRuntimeStatus,
    bool ShouldAttemptRecovery,
    BackendRuntimeSnapshotViewState NextRuntimeSnapshot,
    BackendControlApiPollState NextPollState,
    IReadOnlyList<string> LogMessages,
    IReadOnlyList<TrayNotification> Notifications);
