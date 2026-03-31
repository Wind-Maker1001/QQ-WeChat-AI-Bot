using System.IO;

using QQAIBot.Desktop.Models;
using QQAIBot.Desktop.Services;

internal static class DesktopControlPlaneSessionFacadeTests
{
    public static async Task TestSnapshotTargetingAsync()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"desktop-session-facade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(rootPath, "src"));
        Directory.CreateDirectory(Path.Combine(rootPath, "data"));
        await File.WriteAllTextAsync(Path.Combine(rootPath, "package.json"), "{}", System.Text.Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(rootPath, "src", "index.mjs"), "console.log('ok');", System.Text.Encoding.UTF8);

        var context = new DesktopUiTestContext(
            rootPath,
            new FakeBackendControlApiService
            {
                Status = new BackendRuntimeStatus(),
                Config = new BackendControlConfigResponse
                {
                    ConfigPath = Path.Combine(rootPath, "data", "runtime-settings.json"),
                    BootstrapEnvPath = Path.Combine(rootPath, ".env")
                }
            },
            new FakeLocalConfigFallbackReader(
                new EnvDocument
                {
                    ExtraValues =
                    {
                        ["QQ_AI_BOT_CONTROL_API_TOKEN"] = "desktop-token"
                    }
                }),
            new FakeAutoStartService(),
            new FakeBotProcessService(),
            new FakeActivityStateStore(),
            new FakeLocalBootstrapConfigStore(),
            new FakeLocalPathOperationsService(),
            new FakeLocalStateSnapshotService(),
            new FakeConfirmationDialogService());
        var session = new DesktopControlPlaneSession(
            new DesktopSessionDependencies
            {
                LocalConfigFallbackReader = context.FakeLocalFallbackReader,
                LocalBootstrapConfigStore = context.FakeLocalBootstrapStore,
                LocalPathOperationsService = context.FakeLocalPathOperationsService,
                LocalStateSnapshotService = context.FakeLocalStateSnapshotService,
                BackendControlApiService = context.FakeBackend,
                BotProcessService = context.FakeBotProcess,
                ActivityStateStore = context.FakeActivityStateStore,
                ActivityStatePolicy = DesktopActivityStatePolicy.Default
            },
            new DesktopShellState
            {
                SnapshotState = new DesktopSnapshotState
                {
                    StateSnapshots = context.FakeLocalStateSnapshotService.Snapshots
                },
                LocalDocumentState = new DesktopLocalDocumentState
                {
                    BackendRootPath = context.RootPath,
                    BackendRootDetected = true,
                    ConfigDocument = new DesktopConfigDocumentState
                    {
                        Document = context.FakeLocalFallbackReader.Document
                    }
                }
            });

        await session.RefreshStateSnapshotsAsync();
        var selectedSnapshot = context.FakeLocalStateSnapshotService.Snapshots[1];

        session.UpdateSelectedStateSnapshot(selectedSnapshot);
        var previewResult = await session.RefreshSelectedStateSnapshotPreviewAsync();
        var restoreConfirmation = await session.BuildRestoreSelectedStateSnapshotConfirmationAsync(selectedSnapshot.ArchivePath);
        var deleteConfirmation = session.BuildDeleteSelectedStateSnapshotConfirmation(selectedSnapshot.ArchivePath);

        AssertEqual(selectedSnapshot.ArchivePath, previewResult.NextState?.SnapshotState.SelectedStateSnapshot?.ArchivePath, "Session facade should keep the selected snapshot stable across preview refresh.");
        AssertEqual("恢复选中快照", restoreConfirmation.Title, "Session facade should delegate restore confirmation titles.");
        AssertEqual("删除选中快照", deleteConfirmation.Title, "Session facade should delegate delete confirmation titles.");
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected={expected} Actual={actual}");
        }
    }
}
