using QQAIBot.Desktop.Models;
using QQAIBot.Desktop.Services;

internal static class DesktopSnapshotWorkflowTests
{
    public static Task TestStateTransitionsAsync()
    {
        var snapshot = new LocalStateSnapshotDescriptor
        {
            ArchivePath = @"D:\snapshots\runtime-state.zip",
            FileName = "runtime-state.zip"
        };
        var preview = new LocalStateSnapshotPreviewResult
        {
            ArchivePath = snapshot.ArchivePath
        };
        var restoreResult = new LocalStateSnapshotRestoreResult
        {
            ArchivePath = snapshot.ArchivePath
        };

        var nextState = DesktopSnapshotWorkflow.ApplySnapshotList(new DesktopShellSourceState(), [snapshot], snapshot);
        nextState = DesktopSnapshotWorkflow.UpdateSelectedSnapshot(nextState, snapshot);
        nextState = DesktopSnapshotWorkflow.ApplyRestoreResult(nextState, snapshot.ArchivePath, restoreResult, preview);
        nextState = DesktopSnapshotWorkflow.ApplySnapshotExportResult(nextState, snapshot.ArchivePath);
        var clearedState = DesktopSnapshotWorkflow.ClearRestoreResultIfMatchesArchive(nextState, snapshot.ArchivePath);

        AssertEqual(snapshot.ArchivePath, nextState.SnapshotSourceState.LastStateRestoreText, "Snapshot workflow should record the restore target.");
        AssertEqual(snapshot.ArchivePath, nextState.SnapshotSourceState.LastStateSnapshotText, "Snapshot workflow should record the latest exported snapshot.");
        AssertEqual("尚未恢复状态快照", clearedState.SnapshotSourceState.LastStateRestoreText, "Snapshot workflow should clear restore state when the restored archive is deleted.");
        AssertTrue(clearedState.SnapshotSourceState.LastStateRestoreResult is null, "Snapshot workflow should clear restore result payloads together.");
        return Task.CompletedTask;
    }

    public static Task TestConfirmationMessagesAsync()
    {
        var snapshot = new LocalStateSnapshotDescriptor
        {
            ArchivePath = @"D:\snapshots\runtime-state.zip",
            FileName = "runtime-state.zip",
            Summary = "summary"
        };
        var preview = new LocalStateSnapshotPreviewResult
        {
            ArchivePath = snapshot.ArchivePath
        };

        var restoreConfirmation = DesktopSnapshotWorkflow.BuildRestoreConfirmation(snapshot, preview, "恢复选中快照");
        var deleteConfirmation = DesktopSnapshotWorkflow.BuildDeleteConfirmation(snapshot);

        AssertEqual("恢复选中快照", restoreConfirmation.Title, "Snapshot workflow should preserve restore confirmation titles.");
        AssertTrue(restoreConfirmation.Message.Contains("恢复快照：runtime-state.zip", StringComparison.Ordinal), "Snapshot workflow should describe the restore target.");
        AssertEqual("删除选中快照", deleteConfirmation.Title, "Snapshot workflow should preserve delete confirmation titles.");
        AssertTrue(deleteConfirmation.Message.Contains("删除快照：runtime-state.zip", StringComparison.Ordinal), "Snapshot workflow should describe the delete target.");
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
