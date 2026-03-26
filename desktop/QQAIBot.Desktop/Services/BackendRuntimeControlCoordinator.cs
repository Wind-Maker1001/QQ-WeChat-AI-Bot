using Forms = System.Windows.Forms;

using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class BackendRuntimeControlCoordinator
{
    public static async Task<BackendRuntimeControlOutcome> StartAsync(
        Func<CancellationToken, Task<BackendRuntimeStatus?>> tryGetStatusAsync,
        Func<BackendControlApiFailure> getLastFailure,
        Func<BackendControlApiFailure, bool> isImmediateFailure,
        Action startProcess,
        Func<CancellationToken, Task<BackendRuntimeStatus?>> waitForStatusAsync,
        Func<CancellationToken, Task<BackendRuntimeStatus?>> tryStartAsync,
        CancellationToken cancellationToken = default)
    {
        var existingStatus = await tryGetStatusAsync(cancellationToken);
        if (existingStatus is not null)
        {
            var attachStatus = await tryStartAsync(cancellationToken) ?? existingStatus;
            return new BackendRuntimeControlOutcome(
                AppliedStatus: attachStatus,
                ControlApiReachable: true,
                StatusText: attachStatus.RuntimeActive ? "Backend is running" : "Backend start command was ignored",
                LogMessages:
                [
                    $"Attached to existing backend host: {attachStatus.ControlApiUrl}"
                ],
                Notifications: [],
                ShouldDetachProcess: true,
                ShouldReloadConfig: true);
        }

        var existingStatusFailure = getLastFailure();
        if (isImmediateFailure(existingStatusFailure))
        {
            throw new InvalidOperationException(existingStatusFailure.Message);
        }

        startProcess();

        var status = await waitForStatusAsync(cancellationToken);
        if (status is null)
        {
            var waitFailure = getLastFailure();
            if (isImmediateFailure(waitFailure))
            {
                throw new InvalidOperationException(waitFailure.Message);
            }

            return new BackendRuntimeControlOutcome(
                AppliedStatus: null,
                ControlApiReachable: false,
                StatusText: "Backend started, waiting for control API",
                LogMessages: [],
                Notifications: [],
                ShouldDetachProcess: false,
                ShouldReloadConfig: false);
        }

        var startedStatus = await tryStartAsync(cancellationToken);
        return new BackendRuntimeControlOutcome(
            AppliedStatus: startedStatus,
            ControlApiReachable: startedStatus is not null,
            StatusText: startedStatus?.RuntimeActive == true ? "Backend is running" : "Backend start command was ignored",
            LogMessages: startedStatus is not null
                ? [$"Control API ready: {startedStatus.ControlApiUrl}"]
                : [],
            Notifications: [],
            ShouldDetachProcess: startedStatus is not null,
            ShouldReloadConfig: startedStatus is not null);
    }

    public static async Task<BackendRuntimeControlOutcome> StopAsync(
        Func<CancellationToken, Task<BackendRuntimeStatus?>> tryStopAsync,
        Func<BackendControlApiFailure> getLastFailure,
        Func<BackendControlApiFailure, bool> isImmediateFailure,
        Func<bool> isProcessRunning,
        Func<Task> stopProcessAsync,
        CancellationToken cancellationToken = default)
    {
        var stoppedStatus = await tryStopAsync(cancellationToken);

        if (stoppedStatus is not null)
        {
            return new BackendRuntimeControlOutcome(
                AppliedStatus: stoppedStatus,
                ControlApiReachable: true,
                StatusText: stoppedStatus.RuntimeActive ? "Backend is still running" : "Backend stopped",
                LogMessages:
                [
                    $"Sent stop command via control API: {stoppedStatus.ControlApiUrl}"
                ],
                Notifications:
                [
                    new TrayNotification
                    {
                        Title = "QQ AI Bot",
                        Message = stoppedStatus.RuntimeActive
                            ? "Stop command was ignored because runtime is still active."
                            : "Runtime stopped by user.",
                        Icon = stoppedStatus.RuntimeActive
                            ? Forms.ToolTipIcon.Warning
                            : Forms.ToolTipIcon.Info
                    }
                ],
                ShouldDetachProcess: false,
                ShouldReloadConfig: false);
        }

        var stopFailure = getLastFailure();
        if (isImmediateFailure(stopFailure))
        {
            throw new InvalidOperationException(stopFailure.Message);
        }

        if (isProcessRunning())
        {
            await stopProcessAsync();
            return new BackendRuntimeControlOutcome(
                AppliedStatus: null,
                ControlApiReachable: false,
                StatusText: "Backend host stopped",
                LogMessages: [],
                Notifications:
                [
                    new TrayNotification
                    {
                        Title = "QQ AI Bot",
                        Message = "Backend host stopped by user.",
                        Icon = Forms.ToolTipIcon.Info
                    }
                ],
                ShouldDetachProcess: false,
                ShouldReloadConfig: false);
        }

        return new BackendRuntimeControlOutcome(
            AppliedStatus: null,
            ControlApiReachable: false,
            StatusText: "Backend is not reachable",
            LogMessages:
            [
                "Control API is unavailable and no local backend host process is attached."
            ],
            Notifications: [],
            ShouldDetachProcess: false,
            ShouldReloadConfig: false);
    }
}

public sealed record BackendRuntimeControlOutcome(
    BackendRuntimeStatus? AppliedStatus,
    bool ControlApiReachable,
    string StatusText,
    IReadOnlyList<string> LogMessages,
    IReadOnlyList<TrayNotification> Notifications,
    bool ShouldDetachProcess,
    bool ShouldReloadConfig);
