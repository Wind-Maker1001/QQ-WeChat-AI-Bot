using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class BackendRuntimeSnapshotCoordinator
{
    public static BackendRuntimeSnapshotProjection ProjectRuntimeStatus(
        BackendRuntimeStatus? status,
        bool controlApiReachable,
        BackendChannelActivityContext qqContext,
        BackendChannelActivityContext wechatContext)
    {
        var qqActivityProjection = BackendRecentActivityCoordinator.ProjectRuntimeUpdate(
            status?.LastQqLlmRequest,
            status?.LastQqLlmFailure,
            qqContext.ExistingItems,
            qqContext.LastRequestEventKey,
            qqContext.LastFailureEventKey,
            qqContext.IsPinned,
            qqContext.SelectedItem);
        var wechatActivityProjection = BackendRecentActivityCoordinator.ProjectRuntimeUpdate(
            status?.LastWechatLlmRequest,
            status?.LastWechatLlmFailure,
            wechatContext.ExistingItems,
            wechatContext.LastRequestEventKey,
            wechatContext.LastFailureEventKey,
            wechatContext.IsPinned,
            wechatContext.SelectedItem);

        return new BackendRuntimeSnapshotProjection(
            qqActivityProjection,
            wechatActivityProjection,
            new BackendRuntimeSnapshotViewState
            {
                RuntimeActive = status?.RuntimeActive,
                RuntimeReady = status?.RuntimeReady,
                WorkerProcessId = status?.WorkerProcessId,
                WechatWorkerProcessId = status?.WechatWorkerProcessId,
                WechatConfigured = status?.WechatConfigured,
                WechatRuntimeActive = status?.WechatRuntimeActive,
                WechatRuntimeReady = status?.WechatRuntimeReady,
                WechatBridgeConnected = status?.WechatBridgeConnected,
                ControlApiReachable = controlApiReachable,
                LastQqLlmRequest = status?.LastQqLlmRequest,
                LastQqLlmFailure = status?.LastQqLlmFailure,
                LastWechatLlmRequest = status?.LastWechatLlmRequest,
                LastWechatLlmFailure = status?.LastWechatLlmFailure
            },
            controlApiReachable);
    }

    public static BackendActivityRestoreProjection ProjectActivityRestore(
        DesktopActivityStatePolicy activityStatePolicy,
        DesktopActivityState? state)
    {
        var normalizedState = activityStatePolicy.Normalize(state) ?? activityStatePolicy.CreateDefaultState();
        var qqSelection = BackendRecentActivityCoordinator.ResolveSelectionAfterRestore(
            activityStatePolicy,
            normalizedState.QqRecentActivities,
            normalizedState.SelectedQqEventKey,
            normalizedState.ShowOnlyQqFailures);
        var wechatSelection = BackendRecentActivityCoordinator.ResolveSelectionAfterRestore(
            activityStatePolicy,
            normalizedState.WechatRecentActivities,
            normalizedState.SelectedWechatEventKey,
            normalizedState.ShowOnlyWechatFailures);

        return new BackendActivityRestoreProjection(
            normalizedState.QqRecentActivities,
            normalizedState.WechatRecentActivities,
            normalizedState.PinSelectedQqActivity,
            normalizedState.PinSelectedWechatActivity,
            normalizedState.ShowOnlyQqFailures,
            normalizedState.ShowOnlyWechatFailures,
            qqSelection,
            wechatSelection);
    }
}

public sealed record BackendChannelActivityContext(
    IEnumerable<BackendRecentActivityItem> ExistingItems,
    string LastRequestEventKey,
    string LastFailureEventKey,
    bool IsPinned,
    BackendRecentActivityItem? SelectedItem);

public sealed record BackendRuntimeSnapshotProjection(
    BackendRecentActivityProjectionResult QqActivity,
    BackendRecentActivityProjectionResult WechatActivity,
    BackendRuntimeSnapshotViewState SnapshotState,
    bool ControlApiReachable);

public sealed record BackendActivityRestoreProjection(
    IReadOnlyList<BackendRecentActivityItem> QqRecentActivities,
    IReadOnlyList<BackendRecentActivityItem> WechatRecentActivities,
    bool PinSelectedQqActivity,
    bool PinSelectedWechatActivity,
    bool ShowOnlyQqFailures,
    bool ShowOnlyWechatFailures,
    BackendRecentActivityItem? SelectedQqRecentActivity,
    BackendRecentActivityItem? SelectedWechatRecentActivity);
