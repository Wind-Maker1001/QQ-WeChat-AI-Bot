using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class BackendControlApiRecoveryCoordinator
{
    public static async Task<BackendControlApiRecoveryOutcome> TryRecoverAsync(
        string reason,
        Func<Task> prepareAsync,
        Func<CancellationToken, Task<BackendRuntimeStatus?>> tryGetStatusAsync,
        Func<BackendControlApiFailure> getLastFailure,
        Func<BackendControlApiFailure, bool> isImmediateFailure,
        Func<bool> isProcessRunning,
        Action startProcess,
        Func<CancellationToken, Task<BackendRuntimeStatus?>> tryStartAsync,
        Func<CancellationToken, Task<BackendRuntimeStatus?>> waitForStatusAsync,
        Action detachProcess,
        CancellationToken cancellationToken = default)
    {
        var logMessages = new List<string>();

        try
        {
            await prepareAsync();
            logMessages.Add($"Control API unreachable; attempting backend recovery ({reason}).");

            var existingStatus = await tryGetStatusAsync(cancellationToken);
            if (existingStatus is not null)
            {
                logMessages.Add("Control API recovered before local restart was needed.");
                return new BackendControlApiRecoveryOutcome(
                    RecoveredStatus: existingStatus,
                    ShouldDetachProcess: false,
                    LogMessages: logMessages);
            }

            var existingStatusFailure = getLastFailure();
            if (isImmediateFailure(existingStatusFailure))
            {
                logMessages.Add($"Control API recovery aborted: {existingStatusFailure.Message}");
                return new BackendControlApiRecoveryOutcome(
                    RecoveredStatus: null,
                    ShouldDetachProcess: false,
                    LogMessages: logMessages);
            }

            if (!isProcessRunning())
            {
                startProcess();
                logMessages.Add("Started local backend host for control API recovery.");
            }

            var startStatus = await tryStartAsync(cancellationToken);
            if (startStatus is not null)
            {
                detachProcess();
                logMessages.Add($"Control API recovery succeeded: {startStatus.ControlApiUrl}");
                return new BackendControlApiRecoveryOutcome(
                    RecoveredStatus: startStatus,
                    ShouldDetachProcess: false,
                    LogMessages: logMessages);
            }

            var startFailure = getLastFailure();
            if (isImmediateFailure(startFailure))
            {
                logMessages.Add($"Control API recovery aborted: {startFailure.Message}");
                return new BackendControlApiRecoveryOutcome(
                    RecoveredStatus: null,
                    ShouldDetachProcess: false,
                    LogMessages: logMessages);
            }

            var recoveredStatus = await waitForStatusAsync(cancellationToken);
            if (recoveredStatus is not null)
            {
                detachProcess();
                logMessages.Add($"Control API recovery succeeded: {recoveredStatus.ControlApiUrl}");
                return new BackendControlApiRecoveryOutcome(
                    RecoveredStatus: recoveredStatus,
                    ShouldDetachProcess: false,
                    LogMessages: logMessages);
            }

            logMessages.Add("Control API recovery attempt did not restore connectivity.");
        }
        catch (Exception ex)
        {
            logMessages.Add($"Control API recovery failed: {ex.Message}");
        }

        return new BackendControlApiRecoveryOutcome(
            RecoveredStatus: null,
            ShouldDetachProcess: false,
            LogMessages: logMessages);
    }
}

public sealed record BackendControlApiRecoveryOutcome(
    BackendRuntimeStatus? RecoveredStatus,
    bool ShouldDetachProcess,
    IReadOnlyList<string> LogMessages);
