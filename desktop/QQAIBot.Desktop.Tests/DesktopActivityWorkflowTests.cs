using QQAIBot.Desktop.Models;
using QQAIBot.Desktop.Services;

internal static class DesktopActivityWorkflowTests
{
    public static Task TestSelectionAndClearAsync()
    {
        var qqFailure = new BackendRecentActivityItem
        {
            EventKey = "qq-fail",
            Summary = "qq failure",
            IsFailure = true
        };
        var qqSuccess = new BackendRecentActivityItem
        {
            EventKey = "qq-ok",
            Summary = "qq success",
            IsFailure = false
        };
        var initialState = new DesktopShellSourceState
        {
            RecentActivityState = new DesktopRecentActivityState
            {
                QqRecentActivities = [qqFailure, qqSuccess],
                SelectedQqRecentActivity = qqSuccess
            }
        };

        var filteredState = DesktopActivityWorkflow.ApplyActivitySelection(initialState, showOnlyQqFailures: true);
        var clearedState = DesktopActivityWorkflow.ClearQqActivityHistory(filteredState);

        AssertTrue(filteredState.RecentActivityState.ShowOnlyQqFailures, "Activity workflow should persist filter toggles.");
        AssertEqual("qq-fail", filteredState.RecentActivityState.SelectedQqRecentActivity?.EventKey, "Activity workflow should move selection to a visible failure when filters change.");
        AssertEqual(0, clearedState.RecentActivityState.QqRecentActivities.Count, "Activity workflow should clear QQ history state.");
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
}
