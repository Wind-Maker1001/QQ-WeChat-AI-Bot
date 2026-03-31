using QQAIBot.Desktop.Models;
using QQAIBot.Desktop.Services;

internal static class DesktopRuntimeWorkflowTests
{
    public static Task TestRuntimeProjectionAsync()
    {
        var requestItem = new BackendRecentActivityItem
        {
            EventKey = "req-1",
            Summary = "request"
        };
        var failureItem = new BackendRecentActivityItem
        {
            EventKey = "fail-1",
            Summary = "failure",
            IsFailure = true
        };
        var projection = new BackendRuntimeSnapshotProjection(
            new BackendRecentActivityProjectionResult([requestItem], "req-1", string.Empty, requestItem),
            new BackendRecentActivityProjectionResult([failureItem], string.Empty, "fail-1", failureItem),
            new BackendRuntimeSnapshotViewState
            {
                RuntimeActive = true,
                RuntimeReady = true
            },
            ControlApiReachable: true);

        var nextState = DesktopRuntimeWorkflow.ApplyRuntimeProjection(new DesktopShellSourceState(), projection);
        var resetState = DesktopRuntimeWorkflow.ResetControlApiFailureState(
            nextState with
            {
                RuntimeSourceState = nextState.RuntimeSourceState with
                {
                    ControlApiRecoveryInProgress = true,
                    ControlApiPollState = new BackendControlApiPollState
                    {
                        ConsecutiveFailures = 3
                    }
                }
            });

        AssertTrue(nextState.RuntimeSourceState.IsProcessRunning, "Runtime workflow should mirror runtime-active state.");
        AssertEqual("req-1", nextState.RecentActivityState.LastQqRequestEventKey, "Runtime workflow should update QQ request event keys.");
        AssertEqual("fail-1", nextState.RecentActivityState.LastWechatFailureEventKey, "Runtime workflow should update WeChat failure event keys.");
        AssertFalse(resetState.RuntimeSourceState.ControlApiRecoveryInProgress, "Runtime workflow should clear recovery state when resetting control API failure state.");
        AssertEqual(0, resetState.RuntimeSourceState.ControlApiPollState.ConsecutiveFailures, "Runtime workflow should reset poll-state counters.");
        return Task.CompletedTask;
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected={expected} Actual={actual}");
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertFalse(bool condition, string message)
    {
        if (condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
