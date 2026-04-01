using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.IO;
using System.IO.Compression;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

using QQAIBot.Desktop.Infrastructure;
using QQAIBot.Desktop.Models;
using QQAIBot.Desktop.Services;
using QQAIBot.Desktop.ViewModels;
using QQAIBot.Desktop;

var testFailures = new List<string>();
Thread? uiThread = null;
Dispatcher? uiDispatcher = null;
QQAIBot.Desktop.App? uiApp = null;
TaskCompletionSource uiDispatcherReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
TaskCompletionSource uiThreadStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

KillStaleDesktopTestProcesses();

await RunTestAsync("LocalEnvConfigFallbackReader ignores runtime config keys from env-only installs and keeps local extras", TestEnvConfigSnapshotStoreReadsLegacyAllowedGroupIdsWithoutMutationAsync);
await RunTestAsync("LocalEnvConfigFallbackReader load does not create env files when missing", TestEnvConfigSnapshotStoreLoadDoesNotCreateMissingEnvAsync);
await RunTestAsync("LocalEnvBootstrapConfigStore updates local-only control plane values", TestLocalEnvBootstrapConfigStoreAsync);
await RunTestAsync("LocalPathOperationsService clears cache directories without removing the root", TestLocalPathOperationsServiceAsync);
await RunTestAsync("LocalStateSnapshotService exports env, data, and desktop activity state", TestLocalStateSnapshotServiceAsync);
await RunTestAsync("LocalStateSnapshotService restores the latest exported state snapshot", TestLocalStateSnapshotRestoreAsync);
await RunTestAsync("LocalStateSnapshotService exports a safe snapshot without .env", TestLocalStateSnapshotSafeExportAsync);
await RunTestAsync("LocalStateSnapshotService lists snapshots and restores a selected archive", TestLocalStateSnapshotListAndRestoreAsync);
await RunTestAsync("LocalStateSnapshotService deletes a selected archive and updates the list", TestLocalStateSnapshotDeleteAsync);
await RunTestAsync("LocalStateSnapshotService previews current-vs-snapshot differences", TestLocalStateSnapshotPreviewAsync);
await RunTestAsync("LocalStateSnapshotPresentationBuilder explains snapshot safety and restore follow-up actions", TestLocalStateSnapshotPresentationBuilderAsync);
await RunTestAsync("TestEnvConfigSnapshotWriter saves ALLOWED_CHAT_IDS only", TestEnvConfigSnapshotStoreSavesAllowedChatIdsOnlyAsync);
await RunTestAsync("LocalEnvConfigFallbackReader prefers runtime settings and keeps local control-plane extras", TestEnvConfigSnapshotStoreRoundTripsOpenAiRouteControlsAsync);
await RunTestAsync("PathDiscoveryService identifies backend root", TestPathDiscoveryServiceBackendRootAsync);
await RunTestAsync("DesktopActivityStatePolicy normalizes selection and retention semantics", TestDesktopActivityStatePolicySemanticsAsync);
await RunTestAsync("LocalActivityStateStore round-trips and normalizes persisted activity state", TestLocalActivityStateStoreRoundTripAsync);
await RunTestAsync("LocalActivityStateStore prunes activity older than the retention window", TestLocalActivityStateStoreRetentionAsync);
await RunTestAsync("LocalActivityStateStore drops incompatible versions and deletes default state files", TestLocalActivityStateStoreVersionCleanupAsync);
await RunTestAsync("BackendControlApiService uses camelCase control API contract", TestBackendControlApiServiceCamelCaseContractAsync);
await RunTestAsync("BackendExecutionProjectionFormatter formats direct and degraded deliberation projections", TestBackendExecutionProjectionFormatterAsync);
await RunTestAsync("BackendLlmProjectionFormatter formats request and failure details", TestBackendLlmProjectionFormatterAsync);
await RunTestAsync("BackendActivityProjectionFormatter formats summaries and timelines", TestBackendActivityProjectionFormatterAsync);
await RunTestAsync("BackendLatestTurnOverviewBuilder compresses latest turn state into user-facing guidance", TestBackendLatestTurnOverviewBuilderAsync);
await RunTestAsync("DesktopHealthGuidanceBuilder prioritizes blocking setup, runtime follow-up, and latest issues", TestDesktopHealthGuidanceBuilderAsync);
await RunTestAsync("DesktopGuideFlowBuilder derives first-run, daily-use, and readiness state from runtime context", TestDesktopGuideFlowBuilderAsync);
await RunTestAsync("DesktopHealthChecklistBuilder derives stable checklist items and setup summary", TestDesktopHealthChecklistBuilderAsync);
await RunTestAsync("DesktopHealthStatusBuilder derives readiness, primary action, and runtime explanation", TestDesktopHealthStatusBuilderAsync);
await RunTestAsync("DesktopHealthReportBuilder surfaces setup blockers and ready-to-start guidance", TestDesktopHealthReportBuilderAsync);
await RunTestAsync("BackendRecentActivityProjector updates order, selection, and de-duplicates replayed events", TestBackendRecentActivityProjectorAsync);
await RunTestAsync("BackendRecentActivityViewStateHelper filters items and resolves visible selection", TestBackendRecentActivityViewStateHelperAsync);
await RunTestAsync("BackendRecentActivityCoordinator composes runtime update, restore, and filter selection", TestBackendRecentActivityCoordinatorAsync);
await RunTestAsync("BackendRuntimeSnapshotCoordinator projects runtime status and activity restore state", TestBackendRuntimeSnapshotCoordinatorAsync);
await RunTestAsync("BackendRuntimeSnapshotViewHelper replaces recent activities and enumerates snapshot property names", TestBackendRuntimeSnapshotViewHelperAsync);
await RunTestAsync("BackendControlApiStatusPollCoordinator evaluates unreachable, unauthorized, and success transitions", TestBackendControlApiStatusPollCoordinatorAsync);
await RunTestAsync("BackendControlApiStatusWaiter waits through unreachable retries and stops on immediate failures", TestBackendControlApiStatusWaiterAsync);
await RunTestAsync("BackendControlApiRecoveryCoordinator handles recovered, started, and aborted recovery paths", TestBackendControlApiRecoveryCoordinatorAsync);
await RunTestAsync("BackendControlPlaneCoordinator handles load and save recovery paths", TestBackendControlPlaneCoordinatorAsync);
await RunTestAsync("BackendRuntimeControlCoordinator handles start and stop branches", TestBackendRuntimeControlCoordinatorAsync);
await RunTestAsync("BackendControlPlaneFacade composes load, save, start, and stop entry points", TestBackendControlPlaneFacadeAsync);
await RunTestAsync("DesktopConfigWorkflow updates editor state without re-growing the session center", DesktopConfigWorkflowTests.TestEditorProjectionAsync);
await RunTestAsync("DesktopRuntimeWorkflow applies runtime projection and resets control API failure state", DesktopRuntimeWorkflowTests.TestRuntimeProjectionAsync);
await RunTestAsync("DesktopSnapshotWorkflow tracks restore state transitions directly", DesktopSnapshotWorkflowTests.TestStateTransitionsAsync);
await RunTestAsync("DesktopSnapshotWorkflow builds restore and delete confirmations directly", DesktopSnapshotWorkflowTests.TestConfirmationMessagesAsync);
await RunTestAsync("DesktopActivityWorkflow applies filter-driven selection and clear-history transitions", DesktopActivityWorkflowTests.TestSelectionAndClearAsync);
await RunTestAsync("DesktopShellProjector derives local document state from source state", DesktopShellProjectorTests.TestProjectsLocalDocumentStateAsync);
await RunTestAsync("DesktopControlPlaneSession facade delegates snapshot targeting and confirmation building", DesktopControlPlaneSessionFacadeTests.TestSnapshotTargetingAsync);
await RunTestAsync("DesktopControlPlaneSession selects local-token recovery guidance after restore token mismatch", TestDesktopControlPlaneSessionRestoreTokenGuidanceAsync);
await RunTestAsync("DesktopControlPlaneSession selects NapCat review guidance when QQ readiness is blocked after restore", TestDesktopControlPlaneSessionRestoreNapCatGuidanceAsync);
await RunTestAsync("DesktopControlPlaneSession targets selected snapshot for preview restore and delete", TestDesktopControlPlaneSessionSnapshotTargetingAsync);
await RunTestAsync("DesktopControlPlaneSession projects snapshot preview and restore presentation from selected snapshot context", TestDesktopControlPlaneSessionSnapshotPresentationAsync);
await RunTestAsync("DesktopControlPlaneSession preserves pinned activity selection and failure filtering across runtime updates", TestDesktopControlPlaneSessionActivitySelectionStabilityAsync);
await RunTestAsync("DesktopControlPlaneSession exports snapshots and preserves rollback selection", TestDesktopControlPlaneSessionSnapshotExportAsync);
await RunTestAsync("DesktopControlPlaneSession builds snapshot confirmation payloads", TestDesktopControlPlaneSessionSnapshotConfirmationAsync);
await RunTestAsync("DesktopControlPlaneSession routes local path operations through the path service", TestDesktopControlPlaneSessionLocalPathOperationsAsync);
await RunTestAsync("DesktopControlPlaneSession projects backend-root local document paths and cache state", TestDesktopControlPlaneSessionLocalDocumentProjectionAsync);
await RunTestAsync("DesktopControlPlaneFeedback applies outcomes and errors to shell callbacks", TestDesktopControlPlaneFeedbackAsync);
await RunTestAsync("DesktopOperationErrorFormatter translates common control-plane failures into user guidance", TestDesktopOperationErrorFormatterAsync);
await RunTestAsync("DesktopShellPropertyCatalog exposes a unique shell notification directory", TestDesktopShellPropertyCatalogAsync);
await RunTestAsync("BackendControlApiService classifies 401 responses as unauthorized", TestBackendControlApiServiceUnauthorizedAsync);
await RunTestAsync("BackendControlApiService exposes rejected config errors separately from transport failures", TestBackendControlApiServiceRejectedSaveAsync);
await RunTestAsync("BackendControlApiService rejects legacy config contracts without configPath", TestBackendControlApiServiceLegacyContractAsync);
await RunTestAsync("BackendControlApiService treats empty successful config responses as unknown failures", TestBackendControlApiServiceEmptyConfigResponseAsync);
await RunTestAsync("BackendControlApiService treats empty successful save responses as unknown failures", TestBackendControlApiServiceEmptySaveResponseAsync);
await RunTestAsync("BackendControlApiService treats empty successful status/start/stop responses as unknown failures", TestBackendControlApiServiceEmptyRuntimeResponsesAsync);
await RunTestAsync("BotProcessService starts backend process, emits logs, and stops cleanly", TestBotProcessServiceStartStopAsync);
await RunTestAsync("BotProcessService detaches without stopping backend process", TestBotProcessServiceDetachKeepsBackendAliveAsync);
await RunTestAsync("BotProcessService throws for missing backend directory", TestBotProcessServiceMissingDirectoryAsync);
await RunTestAsync("AsyncRelayCommand blocks re-entry while running", TestAsyncRelayCommandReentryAsync);
await RunTestAsync("RelayCommand respects can-execute predicate", TestRelayCommandCanExecuteAsync);
await RunTestAsync("AutoStartService builds launch command and resolves executable path", TestAutoStartServiceCommandResolutionAsync);
await RunTestAsync("AutoStartService enables and disables startup through registry store abstraction", TestAutoStartServiceRegistryStoreAsync);
await RunTestAsync("AutoStartService writes and removes startup value through Windows registry store on custom key path", TestAutoStartServiceWindowsRegistryStoreAsync);
await RunTestAsync("SingleInstanceCoordinator signals activate event to primary instance", TestSingleInstanceCoordinatorActivateAsync);
await RunTestAsync("SingleInstanceCoordinator signals ensure-runtime event to primary instance", TestSingleInstanceCoordinatorEnsureRuntimeAsync);
await RunTestAsync("Desktop app secondary process restores primary instance and ensure-runtime process signals runtime", TestDesktopCrossProcessSingleInstanceActivationAsync);
await RunTestAsync("MainWindow smoke automation binds controls and routes save/start/stop commands", TestMainWindowSmokeAutomationAsync);
await RunTestAsync("MainViewModel dispose does not stop backend launcher ownership after attach", TestMainViewModelDisposeDoesNotStopBackendProcessAsync);
await RunTestAsync("MainViewModel preserves default editor state when startup auto-start is already disabled", TestMainViewModelPreservesDefaultEditorStateOnStartupAsync);
await RunTestAsync("MainViewModel auto-recovers control API before showing outage warning", TestMainViewModelAutoRecoversControlApiBeforeWarningAsync);
await RunTestAsync("DesktopControlPlaneSession rejects unknown control API config failures before file fallback", TestDesktopControlPlaneSessionRejectsUnknownConfigFailureBeforeFallbackAsync);
await RunTestAsync("DesktopControlPlaneSession rejects unauthorized control API config failures before file fallback", TestDesktopControlPlaneSessionRejectsUnauthorizedConfigFailureBeforeFallbackAsync);
await RunTestAsync("DesktopControlPlaneSession rejects incompatible control API config contracts before file fallback", TestDesktopControlPlaneSessionRejectsIncompatibleConfigContractBeforeFallbackAsync);
await RunTestAsync("MainViewModel loads through recovered control API before file fallback", TestMainViewModelLoadsThroughRecoveredControlApiAsync);
await RunTestAsync("MainViewModel saves through recovered control API instead of env fallback", TestMainViewModelSavesThroughRecoveredControlApiAsync);
await RunTestAsync("MainViewModel keeps edited BOT_SYSTEM_PROMPT when save response omits it", TestMainViewModelPreservesEditedBotSystemPromptWhenSaveResponseOmitsItAsync);
await RunTestAsync("MainViewModel surfaces rejected control API saves without env fallback or recovery", TestMainViewModelSurfacesRejectedControlApiSaveAsync);
await RunTestAsync("MainViewModel preserves DeepSeek edits when saving through the control API", TestMainViewModelPreservesDeepSeekEditsOnSaveAsync);
await RunTestAsync("MainViewModel surfaces incompatible control API saves without reverting DeepSeek edits", TestMainViewModelSurfacesIncompatibleControlApiSaveWithoutRevertingDeepSeekEditsAsync);
await RunTestAsync("MainViewModel publishes resident-mode notifications when startup is toggled", TestMainViewModelPublishesResidentModeNotificationsAsync);
await RunTestAsync("MainViewModel exposes completed homepage guide states when runtime and startup are ready", TestMainViewModelGuideCompletionStatesAsync);
await RunTestAsync("MainViewModel restores local activity state for recent events and pin/filter preferences", TestMainViewModelRestoresLocalActivityStateAsync);
await RunTestAsync("MainViewModel explains restore token mismatch with a local-token-first action", TestMainViewModelRestoreGuidancePrioritizesLocalTokenFixAsync);
await RunTestAsync("MainViewModel explains restore QQ readiness blockers with NapCat-first guidance", TestMainViewModelRestoreGuidancePrioritizesNapCatReviewAsync);
await RunTestAsync("MainWindow auto-starts backend when control API is unreachable on load", TestMainWindowAutoStartsBackendWhenControlApiIsUnavailableAsync);
await RunTestAsync("MainWindow external activation restores minimized window and triggers ensure-runtime", TestMainWindowExternalActivationAsync);
await RunTestAsync("MainWindow health actions focus relevant controls and route primary action", TestMainWindowHealthActionsAsync);
await RunTestAsync("MainWindow hides to tray when minimized and shows tray balloon", TestMainWindowTrayMinimizeBehaviorAsync);
await RunTestAsync("MainWindow tray balloon explains when backend keeps running", TestMainWindowTrayBalloonExplainsRunningBackendAsync);
await RunTestAsync("MainWindow close hides to tray and shows balloon tip when tray is enabled", TestMainWindowTrayCloseBehaviorAsync);
await RunTestAsync("MainWindow tray exit confirms when backend is still running", TestMainWindowTrayExitConfirmsRunningBackendAsync);
await RunTestAsync("MainWindow toolbar exit confirms when backend is still running", TestMainWindowToolbarExitConfirmsRunningBackendAsync);
await RunTestAsync("MainWindow tray menu exposes expected actions and routes open/start/stop", TestMainWindowTrayMenuActionsAsync);

if (testFailures.Count > 0)
{
    await ShutdownUiThreadAsync();
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Desktop regression failed: {testFailures.Count} test(s).");

    foreach (var failure in testFailures)
    {
        Console.Error.WriteLine(failure);
    }

    Environment.Exit(1);
}

await ShutdownUiThreadAsync();
Console.WriteLine();
Console.WriteLine("Desktop regression passed.");

return;

async Task RunTestAsync(string name, Func<Task> test)
{
    try
    {
        await test();
        Console.WriteLine($"[PASS] {name}");
    }
    catch (Exception ex)
    {
        testFailures.Add($"[FAIL] {name}: {ex}");
    }
}

async Task TestEnvConfigSnapshotStoreReadsLegacyAllowedGroupIdsWithoutMutationAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-env-legacy-");
    var envPath = Path.Combine(rootPath, ".env");
    var originalText =
        string.Join(
            Environment.NewLine,
            [
                "OPENAI_API_KEY=test-key",
                "ALLOWED_GROUP_IDS=chat-a,chat-b",
                "ALLOWED_USER_IDS=user-a",
                "QQ_AI_BOT_CONTROL_API_TOKEN=local-token"
            ]) + Environment.NewLine;
    await File.WriteAllTextAsync(
        envPath,
        originalText,
        Encoding.UTF8);

    var service = new LocalEnvConfigFallbackReader();
    var document = await service.LoadAsync(rootPath);
    var loadedText = await File.ReadAllTextAsync(envPath, Encoding.UTF8);

    AssertEqual(string.Empty, document.Config.AllowedChatIds, "Env-only runtime config keys should no longer hydrate config fields.");
    AssertEqual(string.Empty, document.Config.OpenAiApiKey, "Env-only runtime config keys should be ignored.");
    AssertEqual("local-token", document.ExtraValues["QQ_AI_BOT_CONTROL_API_TOKEN"], "Local control-plane extras should still load from .env.");
    AssertEqual(originalText, loadedText, "LoadAsync should not mutate the env file when reading legacy keys.");
}

async Task TestEnvConfigSnapshotStoreLoadDoesNotCreateMissingEnvAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-env-missing-");
    var envPath = Path.Combine(rootPath, ".env");
    var service = new LocalEnvConfigFallbackReader();

    var document = await service.LoadAsync(rootPath);

    AssertEqual(string.Empty, document.Config.OpenAiApiKey, "Missing env load should return an empty document.");
    AssertFalse(File.Exists(envPath), "LoadAsync should not create .env when the file is missing.");
}

async Task TestLocalEnvBootstrapConfigStoreAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-env-bootstrap-");
    var envPath = Path.Combine(rootPath, ".env");
    await File.WriteAllTextAsync(
        envPath,
        string.Join(
            Environment.NewLine,
            [
                "OPENAI_API_KEY=test-key",
                "QQ_AI_BOT_CONTROL_API_TOKEN=old-token",
                "NAPCAT_TOKEN=napcat-token"
            ]) + Environment.NewLine,
        Encoding.UTF8);

    var store = new LocalEnvBootstrapConfigStore();
    await store.SaveExtraValueAsync(rootPath, "QQ_AI_BOT_CONTROL_API_TOKEN", "new-token");

    var savedText = await File.ReadAllTextAsync(envPath, Encoding.UTF8);
    AssertContains(savedText, "QQ_AI_BOT_CONTROL_API_TOKEN=new-token", "Bootstrap store should replace the local token value.");
    AssertContains(savedText, "OPENAI_API_KEY=test-key", "Bootstrap store should preserve unrelated env lines.");
    AssertContains(savedText, "NAPCAT_TOKEN=napcat-token", "Bootstrap store should preserve unrelated runtime values.");

    await store.SaveExtraValueAsync(rootPath, "QQ_AI_BOT_CONTROL_API_TOKEN", "");
    var clearedText = await File.ReadAllTextAsync(envPath, Encoding.UTF8);
    AssertDoesNotContain(clearedText, "QQ_AI_BOT_CONTROL_API_TOKEN=", "Bootstrap store should remove the local token when the value is blank.");
}

async Task TestLocalPathOperationsServiceAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-local-paths-");
    var cachePath = Path.Combine(rootPath, "image-cache");
    Directory.CreateDirectory(Path.Combine(cachePath, "nested"));
    await File.WriteAllTextAsync(Path.Combine(cachePath, "one.txt"), "one", Encoding.UTF8);
    await File.WriteAllTextAsync(Path.Combine(cachePath, "nested", "two.txt"), "two", Encoding.UTF8);

    var service = new LocalPathOperationsService();
    var removedEntries = service.ClearDirectoryContents(cachePath);

    AssertTrue(removedEntries >= 3, "Path operations should report removed cache entries.");
    AssertTrue(Directory.Exists(cachePath), "Path operations should preserve the cache root directory.");
    AssertEqual(0, Directory.GetFileSystemEntries(cachePath).Length, "Path operations should clear the cache contents.");
}

async Task TestLocalStateSnapshotServiceAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-state-snapshot-");
    var envPath = Path.Combine(rootPath, ".env");
    var dataPath = Path.Combine(rootPath, "data");
    Directory.CreateDirectory(dataPath);
    await File.WriteAllTextAsync(envPath, "OPENAI_API_KEY=test-key", Encoding.UTF8);
    await File.WriteAllTextAsync(Path.Combine(dataPath, "sessions.json"), "{}", Encoding.UTF8);

    var activityStateRoot = await CreateTempDirectoryAsync("desktop-activity-export-");
    var storagePolicy = new DesktopActivityStateStoragePolicy(activityStateRoot);
    var activityStatePath = storagePolicy.ResolveStateFilePath(rootPath);
    Directory.CreateDirectory(Path.GetDirectoryName(activityStatePath)!);
    await File.WriteAllTextAsync(activityStatePath, "{\"version\":1}", Encoding.UTF8);

    var service = new LocalStateSnapshotService(storagePolicy);
    var result = await service.ExportAsync(rootPath);

    AssertTrue(File.Exists(result.ArchivePath), "State snapshot service should create an archive.");
    AssertTrue(result.IncludedEntries.Contains("app/.env"), "State snapshot service should include .env.");
    AssertTrue(result.IncludedEntries.Contains("app/data/sessions.json"), "State snapshot service should include data files.");
    AssertTrue(result.IncludedEntries.Contains("desktop/activity-state.json"), "State snapshot service should include desktop activity state.");

    using var archive = ZipFile.OpenRead(result.ArchivePath);
    AssertTrue(archive.Entries.Any(static entry => entry.FullName == "manifest.json"), "State snapshot archive should include a manifest.");
    AssertTrue(archive.Entries.Any(static entry => entry.FullName == "app/.env"), "State snapshot archive should contain the env entry.");
    AssertTrue(archive.Entries.Any(static entry => entry.FullName == "app/data/sessions.json"), "State snapshot archive should contain the data entry.");
}

async Task TestLocalStateSnapshotRestoreAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-state-restore-");
    var envPath = Path.Combine(rootPath, ".env");
    var dataPath = Path.Combine(rootPath, "data");
    Directory.CreateDirectory(dataPath);
    await File.WriteAllTextAsync(envPath, "OPENAI_API_KEY=before-restore", Encoding.UTF8);
    await File.WriteAllTextAsync(Path.Combine(dataPath, "sessions.json"), "{\"before\":true}", Encoding.UTF8);

    var activityStateRoot = await CreateTempDirectoryAsync("desktop-activity-restore-");
    var storagePolicy = new DesktopActivityStateStoragePolicy(activityStateRoot);
    var activityStatePath = storagePolicy.ResolveStateFilePath(rootPath);
    Directory.CreateDirectory(Path.GetDirectoryName(activityStatePath)!);
    await File.WriteAllTextAsync(activityStatePath, "{\"version\":1,\"qqRecentActivities\":[]}", Encoding.UTF8);

    var service = new LocalStateSnapshotService(storagePolicy);
    var exportResult = await service.ExportAsync(rootPath);

    await File.WriteAllTextAsync(envPath, "OPENAI_API_KEY=after-export", Encoding.UTF8);
    await File.WriteAllTextAsync(Path.Combine(dataPath, "sessions.json"), "{\"after\":true}", Encoding.UTF8);
    await File.WriteAllTextAsync(activityStatePath, "{\"version\":2}", Encoding.UTF8);
    await Task.Delay(25);
    File.SetLastWriteTimeUtc(exportResult.ArchivePath, DateTime.UtcNow);

    var restoreResult = await service.RestoreLatestAsync(rootPath);

    AssertEqual(exportResult.ArchivePath, restoreResult.ArchivePath, "Restore should use the latest archive.");
    AssertContains(await File.ReadAllTextAsync(envPath, Encoding.UTF8), "before-restore", "Restore should overwrite .env from the snapshot.");
    AssertContains(await File.ReadAllTextAsync(Path.Combine(dataPath, "sessions.json"), Encoding.UTF8), "\"before\":true", "Restore should overwrite session data from the snapshot.");
    AssertContains(await File.ReadAllTextAsync(activityStatePath, Encoding.UTF8), "\"version\":1", "Restore should overwrite desktop activity state from the snapshot.");
    AssertTrue(restoreResult.RestoredEntries.Contains("app/.env"), "Restore result should report the env entry.");
    AssertTrue(restoreResult.RestoredEntries.Contains("desktop/activity-state.json"), "Restore result should report the desktop state entry.");
}

async Task TestLocalStateSnapshotSafeExportAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-state-snapshot-safe-");
    var envPath = Path.Combine(rootPath, ".env");
    var dataPath = Path.Combine(rootPath, "data");
    Directory.CreateDirectory(dataPath);
    await File.WriteAllTextAsync(envPath, "OPENAI_API_KEY=test-key", Encoding.UTF8);
    await File.WriteAllTextAsync(Path.Combine(dataPath, "sessions.json"), "{}", Encoding.UTF8);

    var activityStateRoot = await CreateTempDirectoryAsync("desktop-activity-safe-export-");
    var storagePolicy = new DesktopActivityStateStoragePolicy(activityStateRoot);
    var activityStatePath = storagePolicy.ResolveStateFilePath(rootPath);
    Directory.CreateDirectory(Path.GetDirectoryName(activityStatePath)!);
    await File.WriteAllTextAsync(activityStatePath, "{\"version\":1}", Encoding.UTF8);

    var service = new LocalStateSnapshotService(storagePolicy);
    var result = await service.ExportSafeAsync(rootPath);

    AssertFalse(result.IncludesSecrets, "Safe snapshot export should not mark secrets as included.");
    AssertFalse(result.IncludedEntries.Contains("app/.env"), "Safe snapshot export should not include .env.");
    AssertTrue(result.IncludedEntries.Contains("app/data/sessions.json"), "Safe snapshot export should still include data.");

    using var archive = ZipFile.OpenRead(result.ArchivePath);
    AssertFalse(archive.Entries.Any(static entry => entry.FullName == "app/.env"), "Safe snapshot archive should omit the env entry.");
    var manifestEntry = archive.GetEntry("manifest.json") ?? throw new InvalidOperationException("manifest.json missing");
    using var reader = new StreamReader(manifestEntry.Open(), Encoding.UTF8);
    var manifestText = await reader.ReadToEndAsync();
    AssertContains(manifestText, "\"includesSecrets\": false", "Safe snapshot manifest should record that secrets were omitted.");
}

async Task TestLocalStateSnapshotListAndRestoreAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-state-list-restore-");
    var activityStateRoot = await CreateTempDirectoryAsync("desktop-activity-list-restore-");
    var storagePolicy = new DesktopActivityStateStoragePolicy(activityStateRoot);
    var service = new LocalStateSnapshotService(storagePolicy);

    Directory.CreateDirectory(Path.Combine(rootPath, "data"));
    await File.WriteAllTextAsync(Path.Combine(rootPath, ".env"), "OPENAI_API_KEY=first", Encoding.UTF8);
    await File.WriteAllTextAsync(Path.Combine(rootPath, "data", "sessions.json"), "{\"snapshot\":1}", Encoding.UTF8);
    var firstExport = await service.ExportAsync(rootPath);
    await Task.Delay(30);

    await File.WriteAllTextAsync(Path.Combine(rootPath, ".env"), "OPENAI_API_KEY=second", Encoding.UTF8);
    await File.WriteAllTextAsync(Path.Combine(rootPath, "data", "sessions.json"), "{\"snapshot\":2}", Encoding.UTF8);
    var secondExport = await service.ExportAsync(rootPath);

    var snapshots = await service.ListAsync(rootPath);

    AssertEqual(2, snapshots.Count, "Snapshot listing should return both exported archives.");
    AssertEqual(Path.GetFileName(secondExport.ArchivePath), snapshots[0].FileName, "Snapshot listing should order newest archives first.");
    AssertContains(snapshots[0].Summary, "项", "Snapshot listing should expose a summary.");
    AssertFalse(string.IsNullOrWhiteSpace(snapshots[0].SizeText), "Snapshot listing should expose archive size text.");

    await File.WriteAllTextAsync(Path.Combine(rootPath, ".env"), "OPENAI_API_KEY=mutated", Encoding.UTF8);
    var restoreResult = await service.RestoreAsync(rootPath, firstExport.ArchivePath);

    AssertEqual(firstExport.ArchivePath, restoreResult.ArchivePath, "Selected restore should use the requested archive path.");
    AssertContains(await File.ReadAllTextAsync(Path.Combine(rootPath, ".env"), Encoding.UTF8), "OPENAI_API_KEY=first", "Selected restore should restore the requested archive contents.");
}

async Task TestLocalStateSnapshotDeleteAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-state-delete-");
    Directory.CreateDirectory(Path.Combine(rootPath, "data"));
    await File.WriteAllTextAsync(Path.Combine(rootPath, ".env"), "OPENAI_API_KEY=first", Encoding.UTF8);
    await File.WriteAllTextAsync(Path.Combine(rootPath, "data", "sessions.json"), "{\"snapshot\":1}", Encoding.UTF8);

    var service = new LocalStateSnapshotService(new DesktopActivityStateStoragePolicy(await CreateTempDirectoryAsync("desktop-activity-delete-")));
    var firstExport = await service.ExportAsync(rootPath);
    await Task.Delay(25);
    var secondExport = await service.ExportAsync(rootPath);

    var snapshotsBeforeDelete = await service.ListAsync(rootPath);
    AssertEqual(2, snapshotsBeforeDelete.Count, "Delete test should start with two snapshots.");

    await service.DeleteAsync(firstExport.ArchivePath);

    var snapshotsAfterDelete = await service.ListAsync(rootPath);
    AssertEqual(1, snapshotsAfterDelete.Count, "Deleting a snapshot should remove it from the list.");
    AssertEqual(secondExport.ArchivePath, snapshotsAfterDelete[0].ArchivePath, "Deleting one snapshot should keep the remaining archive.");
    AssertFalse(File.Exists(firstExport.ArchivePath), "Deleting a snapshot should remove the archive file.");
}

async Task TestLocalStateSnapshotPreviewAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-state-preview-");
    Directory.CreateDirectory(Path.Combine(rootPath, "data"));
    await File.WriteAllTextAsync(
        Path.Combine(rootPath, ".env"),
        "OPENAI_API_KEY=current-secret-1234\nNAPCAT_TOKEN=old-token-5678",
        Encoding.UTF8);
    await File.WriteAllTextAsync(
        Path.Combine(rootPath, "data", "sessions.json"),
        "{\"channel=qq|chat=group-a|user=user-a\":{\"updatedAt\":\"2026-03-25T10:00:00.000Z\"},\"channel=qq|chat=group-b|user=user-b\":{\"updatedAt\":\"2026-03-26T09:00:00.000Z\"}}",
        Encoding.UTF8);

    var activityStateRoot = await CreateTempDirectoryAsync("desktop-activity-preview-");
    var storagePolicy = new DesktopActivityStateStoragePolicy(activityStateRoot);
    var activityStatePath = storagePolicy.ResolveStateFilePath(rootPath);
    Directory.CreateDirectory(Path.GetDirectoryName(activityStatePath)!);
    await File.WriteAllTextAsync(activityStatePath, "{\"version\":1}", Encoding.UTF8);

    var service = new LocalStateSnapshotService(storagePolicy);
    var exportResult = await service.ExportAsync(rootPath);

    await File.WriteAllTextAsync(
        Path.Combine(rootPath, ".env"),
        "OPENAI_API_KEY=mutated-secret-9999\nNAPCAT_TOKEN=old-token-5678",
        Encoding.UTF8);
    await File.WriteAllTextAsync(
        Path.Combine(rootPath, "data", "sessions.json"),
        "{\"channel=qq|chat=group-a|user=user-a\":{\"updatedAt\":\"2026-03-25T10:00:00.000Z\"},\"channel=qq|chat=group-b|user=user-b\":{\"updatedAt\":\"2026-03-27T10:00:00.000Z\"},\"channel=qq|chat=group-c|user=user-c\":{\"updatedAt\":\"2026-03-27T11:00:00.000Z\"}}",
        Encoding.UTF8);

    var preview = await service.PreviewAsync(rootPath, exportResult.ArchivePath);

    AssertContains(string.Join(Environment.NewLine, preview.Lines), "本机连接 .env：与当前状态不同", "Snapshot preview should report env differences.");
    AssertContains(string.Join(Environment.NewLine, preview.Lines), "运行配置文件", "Snapshot preview should identify the runtime settings file.");
    AssertContains(string.Join(Environment.NewLine, preview.Lines), "OPENAI_API_KEY", "Snapshot preview should list changed tracked env keys.");
    AssertContains(string.Join(Environment.NewLine, preview.Lines), "********1234", "Snapshot preview should mask snapshot secret values.");
    AssertContains(string.Join(Environment.NewLine, preview.Lines), "********9999", "Snapshot preview should mask current secret values.");
    AssertContains(string.Join(Environment.NewLine, preview.Lines), "data/：与当前状态不同", "Snapshot preview should report data differences.");
    AssertContains(string.Join(Environment.NewLine, preview.Lines), "将恢复的数据文件：sessions.json（已变化）", "Snapshot preview should report changed data files.");
    AssertContains(string.Join(Environment.NewLine, preview.Lines), "仅当前存在的数据文件：无", "Snapshot preview should report whether local-only data files exist.");
    AssertContains(string.Join(Environment.NewLine, preview.Lines), "sessions.json 会话数：当前 3 -> 快照 2", "Snapshot preview should report current vs snapshot conversation counts.");
    AssertContains(string.Join(Environment.NewLine, preview.Lines), "sessions.json 最近活动：当前 2026-03-27 11:00:00 UTC -> 快照 2026-03-26 09:00:00 UTC", "Snapshot preview should report latest session activity timestamps.");
    AssertContains(string.Join(Environment.NewLine, preview.Lines), "sessions.json 变更会话：qq:group-c/user-c (仅当前存在), qq:group-b/user-b (已变化)", "Snapshot preview should report changed conversation keys.");
    AssertContains(string.Join(Environment.NewLine, preview.Recommendations), "恢复前先导出当前状态", "Snapshot preview should include restore advice.");
    AssertContains(string.Join(Environment.NewLine, preview.Lines), "桌面活动状态：与当前状态一致", "Snapshot preview should report unchanged desktop activity state.");
}

Task TestLocalStateSnapshotPresentationBuilderAsync()
{
    var snapshot = new LocalStateSnapshotDescriptor
    {
        ArchivePath = @"D:\snapshots\runtime-state-older.zip",
        FileName = "runtime-state-older.zip",
        Summary = "2026-03-26 09:00:00 | 8.2 KB | 3 项 | 包含密钥",
        Detail = string.Join(Environment.NewLine, ["app/.env", "app/data/sessions.json", "desktop/activity-state.json"]),
        IncludesSecrets = true,
        IncludedEntries = ["app/.env", "app/data/sessions.json", "desktop/activity-state.json"]
    };
    var overwritePreview = new LocalStateSnapshotPreviewResult
    {
        ArchivePath = snapshot.ArchivePath,
        Lines =
        [
            "本机连接 .env：与当前状态不同",
            "sessions.json 会话数：当前 3 -> 快照 2",
            "sessions.json 最近活动：当前 2026-03-27 11:00:00 UTC -> 快照 2026-03-26 09:00:00 UTC",
            ".env 跟踪键变更：QQ_AI_BOT_CONTROL_API_TOKEN, NAPCAT_WS_URL"
        ],
        Recommendations =
        [
            "建议：恢复前先导出当前状态，便于需要时回滚。",
            "注意：这个快照包含 .env 密钥，请不要把归档分享给当前设备之外的人。"
        ]
    };

    var selectionPresentation = LocalStateSnapshotPresentationBuilder.BuildSelectionPresentation(snapshot, overwritePreview);
    AssertContains(selectionPresentation.ImpactText, ".env", "Snapshot presentation should explain env overwrite risk.");
    AssertContains(selectionPresentation.SafetyHeadlineText, "高风险", "Snapshot presentation should elevate overwrite-plus-secret restores.");
    AssertContains(selectionPresentation.SafetyRecommendationText, "安全回滚快照", "Snapshot presentation should recommend a safe rollback snapshot first.");
    AssertContains(selectionPresentation.RollbackHintText, "不会复制 .env 密钥", "Snapshot presentation should explain why the rollback snapshot is safer.");

    var unauthorizedPresentation = LocalStateSnapshotPresentationBuilder.BuildRestorePresentation(
        new LocalStateSnapshotRestoreResult
        {
            ArchivePath = snapshot.ArchivePath,
            RestoredEntries = ["app/.env", "app/data/sessions.json"]
        },
        overwritePreview,
        new LocalStateSnapshotRestoreRuntimeContext
        {
            ControlApiFailure = new BackendControlApiFailure
            {
                Kind = BackendControlApiFailureKind.Unauthorized,
                Message = "Control API authentication failed."
            },
            IsControlApiReachable = false,
            CanStartBackend = true,
            IsQqRuntimeReady = false,
            IsWechatConfigured = false,
            IsWechatRuntimeReady = false
        });
    AssertContains(unauthorizedPresentation.IssueText, "本地控制令牌", "Restore presentation should explain token mismatch when the snapshot changed the local control token.");
    AssertEqual(DesktopHealthActionKeys.FocusControlApiToken, unauthorizedPresentation.PrimaryAction.Key, "Restore presentation should prioritize fixing the local control token first.");
    AssertEqual(DesktopHealthActionKeys.ReloadConfig, unauthorizedPresentation.SecondaryAction.Key, "Restore presentation should keep reload as the second token-recovery step.");
    AssertEqual(DesktopHealthActionKeys.StartBackend, unauthorizedPresentation.TertiaryAction.Key, "Restore presentation should still expose start-backend as a later action when the host is stopped.");

    var stoppedPresentation = LocalStateSnapshotPresentationBuilder.BuildRestorePresentation(
        new LocalStateSnapshotRestoreResult
        {
            ArchivePath = snapshot.ArchivePath,
            RestoredEntries = ["app/.env", "app/data/sessions.json"]
        },
        overwritePreview,
        new LocalStateSnapshotRestoreRuntimeContext
        {
            ControlApiFailure = new BackendControlApiFailure(),
            IsControlApiReachable = false,
            CanStartBackend = true,
            IsQqRuntimeReady = false,
            IsWechatConfigured = false,
            IsWechatRuntimeReady = false
        });
    AssertContains(stoppedPresentation.IssueText, "backend 宿主当前已停止", "Restore presentation should explain when the backend host is still stopped after restore.");
    AssertEqual(DesktopHealthActionKeys.StartBackend, stoppedPresentation.PrimaryAction.Key, "Restore presentation should prioritize restarting the backend after an env-overwriting restore.");
    AssertEqual(DesktopHealthActionKeys.ReloadConfig, stoppedPresentation.SecondaryAction.Key, "Restore presentation should keep reload immediately after restarting.");
    AssertEqual(DesktopHealthActionKeys.FocusControlApiToken, stoppedPresentation.TertiaryAction.Key, "Restore presentation should keep local control settings as the tertiary stopped-runtime fallback.");

    var qqBlockedPresentation = LocalStateSnapshotPresentationBuilder.BuildRestorePresentation(
        new LocalStateSnapshotRestoreResult
        {
            ArchivePath = snapshot.ArchivePath,
            RestoredEntries = ["app/.env", "app/data/sessions.json"]
        },
        overwritePreview,
        new LocalStateSnapshotRestoreRuntimeContext
        {
            ControlApiFailure = new BackendControlApiFailure(),
            IsControlApiReachable = true,
            CanStartBackend = false,
            IsQqRuntimeReady = false,
            IsWechatConfigured = true,
            IsWechatRuntimeReady = false
        });
    AssertContains(qqBlockedPresentation.IssueText, "改动了 NapCat 设置", "Restore presentation should explain QQ readiness blockers caused by restored NapCat settings.");
    AssertEqual(DesktopHealthActionKeys.FocusNapCatUrl, qqBlockedPresentation.PrimaryAction.Key, "Restore presentation should prioritize NapCat review when QQ is blocked.");
    AssertEqual(DesktopHealthActionKeys.ReloadConfig, qqBlockedPresentation.SecondaryAction.Key, "Restore presentation should keep reload as the follow-up after NapCat review.");

    var healthyPreview = new LocalStateSnapshotPreviewResult
    {
        ArchivePath = snapshot.ArchivePath,
        Lines =
        [
            "sessions.json 会话数：当前 3 -> 快照 2",
            "sessions.json 最近活动：当前 2026-03-27 11:00:00 UTC -> 快照 2026-03-26 09:00:00 UTC",
            ".env 跟踪键：没有变化"
        ],
        Recommendations =
        [
            "建议：恢复前先导出当前状态，便于需要时回滚。"
        ]
    };
    var healthyPresentation = LocalStateSnapshotPresentationBuilder.BuildRestorePresentation(
        new LocalStateSnapshotRestoreResult
        {
            ArchivePath = snapshot.ArchivePath,
            RestoredEntries = ["app/data/sessions.json"]
        },
        healthyPreview,
        new LocalStateSnapshotRestoreRuntimeContext
        {
            ControlApiFailure = new BackendControlApiFailure(),
            IsControlApiReachable = true,
            CanStartBackend = false,
            IsQqRuntimeReady = true,
            IsWechatConfigured = false,
            IsWechatRuntimeReady = false
        });
    AssertContains(healthyPresentation.IssueText, "没有发现立即需要处理的恢复后问题", "Restore presentation should say when no immediate blockers remain.");
    AssertContains(healthyPresentation.RuntimeText, "会话存储现在也应该已经生效", "Restore presentation should surface the session-store activation outcome when the runtime is healthy.");
    AssertEqual(DesktopHealthActionKeys.ReloadConfig, healthyPresentation.PrimaryAction.Key, "Restore presentation should fall back to reload when the restore looks healthy.");
    AssertEqual(DesktopHealthActionKeys.FocusControlApiToken, healthyPresentation.SecondaryAction.Key, "Restore presentation should keep local control settings available as a secondary healthy-state follow-up.");

    return Task.CompletedTask;
}

async Task TestEnvConfigSnapshotStoreSavesAllowedChatIdsOnlyAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-env-save-");
    var service = new TestEnvConfigSnapshotWriter();
    var document = new EnvDocument
    {
        Config = new BotConfig
        {
            OpenAiApiKey = "test-key",
            AllowedChatIds = "chat-x,chat-y",
            AllowedUserIds = "user-x"
        }
    };

    await service.SaveAsync(rootPath, document);

    var envText = await File.ReadAllTextAsync(Path.Combine(rootPath, ".env"), Encoding.UTF8);
    AssertContains(envText, "ALLOWED_CHAT_IDS=chat-x,chat-y", "Saved env should contain ALLOWED_CHAT_IDS.");
    AssertDoesNotContain(envText, "ALLOWED_GROUP_IDS=", "Saved env should not contain legacy key.");
}

async Task TestEnvConfigSnapshotStoreRoundTripsOpenAiRouteControlsAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-env-openai-controls-");
    var reader = new LocalEnvConfigFallbackReader();
    Directory.CreateDirectory(Path.Combine(rootPath, "data"));
    await File.WriteAllTextAsync(
        Path.Combine(rootPath, "data", "runtime-settings.json"),
        JsonSerializer.Serialize(
            new
            {
                version = 2,
                savedAt = "2026-03-31T00:00:00.000Z",
                settings = new
                {
                    openAiApiKey = "advanced-key",
                    openAiDefaultApiKey = "default-key",
                    botSystemPrompt = "Base prompt line 1\nBase prompt line 2",
                    openAiDefaultReasoningEffort = "medium",
                    openAiAdvancedReasoningEffort = "high",
                    openAiDefaultTextVerbosity = "low",
                    openAiAdvancedTextVerbosity = "high",
                    openAiDefaultEnableWebSearch = "false",
                    openAiAdvancedEnableWebSearch = "true",
                    openAiDefaultEnableCodeInterpreter = "false",
                    openAiAdvancedEnableCodeInterpreter = "true",
                    deepSeekFallbackEnabled = "true",
                    deepSeekApiKey = "deepseek-key",
                    deepSeekModel = "deepseek-chat",
                    deepSeekBaseUrl = "https://api.deepseek.com/v1"
                },
                apiStyles = new
                {
                    @default = "responses",
                    advanced = "responses"
                }
            },
            new JsonSerializerOptions
            {
                WriteIndented = true
            }) + Environment.NewLine,
        Encoding.UTF8);
    await File.WriteAllTextAsync(
        Path.Combine(rootPath, ".env"),
        string.Join(
            Environment.NewLine,
            [
                "OPENAI_DEFAULT_REASONING_EFFORT=low",
                "OPENAI_ADVANCED_ENABLE_WEB_SEARCH=false",
                "QQ_AI_BOT_CONTROL_API_TOKEN=desktop-token",
                "QQ_AI_BOT_CONTROL_API_HOST=127.0.0.9",
                "QQ_AI_BOT_CONTROL_API_PORT=3201"
            ]) + Environment.NewLine,
        Encoding.UTF8);

    var loadedDocument = await reader.LoadAsync(rootPath);
    AssertEqual("Base prompt line 1\nBase prompt line 2", loadedDocument.Config.BotSystemPrompt, "Bot system prompt should round-trip.");
    AssertEqual("medium", loadedDocument.Config.OpenAiDefaultReasoningEffort, "Default reasoning effort should round-trip.");
    AssertEqual("high", loadedDocument.Config.OpenAiAdvancedReasoningEffort, "Advanced reasoning effort should round-trip.");
    AssertEqual("low", loadedDocument.Config.OpenAiDefaultTextVerbosity, "Default text verbosity should round-trip.");
    AssertEqual("high", loadedDocument.Config.OpenAiAdvancedTextVerbosity, "Advanced text verbosity should round-trip.");
    AssertEqual("false", loadedDocument.Config.OpenAiDefaultEnableWebSearch, "Default web search toggle should round-trip.");
    AssertEqual("true", loadedDocument.Config.OpenAiAdvancedEnableWebSearch, "Advanced web search toggle should round-trip.");
    AssertEqual("false", loadedDocument.Config.OpenAiDefaultEnableCodeInterpreter, "Default code interpreter toggle should round-trip.");
    AssertEqual("true", loadedDocument.Config.OpenAiAdvancedEnableCodeInterpreter, "Advanced code interpreter toggle should round-trip.");
    AssertEqual("true", loadedDocument.Config.DeepSeekFallbackEnabled, "DeepSeek fallback toggle should round-trip.");
    AssertEqual("deepseek-key", loadedDocument.Config.DeepSeekApiKey, "DeepSeek API key should round-trip.");
    AssertEqual("deepseek-chat", loadedDocument.Config.DeepSeekModel, "DeepSeek model should round-trip.");
    AssertEqual("https://api.deepseek.com/v1", loadedDocument.Config.DeepSeekBaseUrl, "DeepSeek base URL should round-trip.");
    AssertEqual("desktop-token", loadedDocument.ExtraValues["QQ_AI_BOT_CONTROL_API_TOKEN"], "Local control-plane token should still load from .env.");
    AssertEqual("127.0.0.9", loadedDocument.ExtraValues["QQ_AI_BOT_CONTROL_API_HOST"], "Local control-plane host should still load from .env.");
    AssertEqual("3201", loadedDocument.ExtraValues["QQ_AI_BOT_CONTROL_API_PORT"], "Local control-plane port should still load from .env.");
}

Task TestPathDiscoveryServiceBackendRootAsync()
{
    var rootPath = Path.Combine(Path.GetTempPath(), $"desktop-backend-root-{Guid.NewGuid():N}");
    Directory.CreateDirectory(rootPath);
    Directory.CreateDirectory(Path.Combine(rootPath, "src"));
    File.WriteAllText(Path.Combine(rootPath, "package.json"), "{}");
    File.WriteAllText(Path.Combine(rootPath, "src", "index.mjs"), "console.log('ok');");

    AssertTrue(PathDiscoveryService.IsBackendRoot(rootPath), "Expected temp directory to be a backend root.");
    AssertFalse(PathDiscoveryService.IsBackendRoot(Path.Combine(rootPath, "missing")), "Missing directory should not be a backend root.");
    return Task.CompletedTask;
}

Task TestLocalActivityStateStoreRoundTripAsync()
{
    var storeRootPath = Path.Combine(Path.GetTempPath(), $"desktop-activity-store-{Guid.NewGuid():N}");
    var backendRootPath = Path.Combine(Path.GetTempPath(), $"desktop-backend-{Guid.NewGuid():N}");
    var storagePolicy = new DesktopActivityStateStoragePolicy(storeRootPath);
    var store = new LocalActivityStateStore(
        storeRootPath,
        new DesktopActivityStatePolicy
        {
            Version = 1,
            MaxRecentActivitiesPerChannel = 4,
            RetentionWindow = TimeSpan.FromDays(30)
        },
        storagePolicy);

    store.Save(
        backendRootPath,
        new DesktopActivityState
        {
            Version = 1,
            QqRecentActivities =
            [
                new BackendRecentActivityItem
                {
                    EventKey = "req-1",
                    CapturedAt = DateTimeOffset.UtcNow.ToString("O"),
                    EventType = "Request",
                    Summary = "request summary",
                    Meta = "meta",
                    Detail = "detail"
                },
                new BackendRecentActivityItem
                {
                    EventKey = "req-1",
                    CapturedAt = DateTimeOffset.UtcNow.ToString("O"),
                    EventType = "Request",
                    Summary = "duplicate should drop",
                    Meta = "meta",
                    Detail = "detail"
                }
            ],
            WechatRecentActivities =
            [
                new BackendRecentActivityItem
                {
                    EventKey = "fail-1",
                    CapturedAt = DateTimeOffset.UtcNow.ToString("O"),
                    EventType = "Failure",
                    Summary = "failure summary",
                    Meta = "meta",
                    Detail = "detail",
                    IsFailure = true
                }
            ],
            SelectedQqEventKey = "req-1",
            SelectedWechatEventKey = "fail-1",
            PinSelectedQqActivity = true,
            ShowOnlyWechatFailures = true
        });

    var restored = store.Load(backendRootPath);

    AssertEqual(1, restored.Version, "LocalActivityStateStore should normalize to the current version.");
    AssertEqual(1, restored.QqRecentActivities.Count, "LocalActivityStateStore should deduplicate recent activity items.");
    AssertEqual("req-1", restored.SelectedQqEventKey, "LocalActivityStateStore should retain valid selected QQ event keys.");
    AssertEqual("fail-1", restored.SelectedWechatEventKey, "LocalActivityStateStore should retain valid selected Wechat event keys.");
    AssertTrue(restored.PinSelectedQqActivity, "LocalActivityStateStore should preserve pin state.");
    AssertTrue(restored.ShowOnlyWechatFailures, "LocalActivityStateStore should preserve filter state.");
    return Task.CompletedTask;
}

Task TestDesktopActivityStatePolicySemanticsAsync()
{
    var policy = new DesktopActivityStatePolicy
    {
        Version = 3,
        MaxRecentActivitiesPerChannel = 2,
        RetentionWindow = TimeSpan.FromDays(7)
    };
    var now = DateTimeOffset.UtcNow;
    var normalized = policy.Normalize(
        new DesktopActivityState
        {
            Version = 3,
            QqRecentActivities =
            [
                new BackendRecentActivityItem
                {
                    EventKey = "req-old",
                    CapturedAt = now.AddDays(-30).ToString("O"),
                    EventType = "Request",
                    Summary = "old"
                },
                new BackendRecentActivityItem
                {
                    EventKey = "req-new",
                    CapturedAt = now.ToString("O"),
                    EventType = "Request",
                    Summary = "new"
                }
            ],
            SelectedQqEventKey = "missing"
        });

    AssertNotNull(normalized, "DesktopActivityStatePolicy should normalize compatible states.");
    AssertEqual(3, normalized!.Version, "DesktopActivityStatePolicy should preserve configured version.");
    AssertEqual(1, normalized.QqRecentActivities.Count, "DesktopActivityStatePolicy should prune activities outside the retention window.");
    AssertEqual(string.Empty, normalized.SelectedQqEventKey, "DesktopActivityStatePolicy should clear invalid selected event keys.");
    AssertEqual(string.Empty, policy.ResolveSelectedEventKey(normalized.QqRecentActivities, "missing"), "DesktopActivityStatePolicy should reject unknown selection keys.");
    AssertEqual("req-new", policy.ResolveSelectedEventKey(normalized.QqRecentActivities, "req-new"), "DesktopActivityStatePolicy should accept valid selection keys.");
    var snapshot = policy.CreateSnapshot(
        normalized.QqRecentActivities,
        normalized.WechatRecentActivities,
        normalized.QqRecentActivities.First(),
        null,
        pinSelectedQqActivity: true,
        pinSelectedWechatActivity: false,
        showOnlyQqFailures: false,
        showOnlyWechatFailures: true);
    AssertEqual(3, snapshot.Version, "DesktopActivityStatePolicy should stamp snapshots with the configured version.");
    AssertEqual("req-new", snapshot.SelectedQqEventKey, "DesktopActivityStatePolicy should persist selected event keys through snapshot creation.");
    AssertEqual(null, policy.ResolveSelectedItem(normalized.QqRecentActivities, "missing"), "DesktopActivityStatePolicy should resolve missing selected items to null.");
    AssertEqual("req-new", policy.ResolveSelectedItem(normalized.QqRecentActivities, "req-new")?.EventKey ?? string.Empty, "DesktopActivityStatePolicy should resolve selected items by key.");
    return Task.CompletedTask;
}

Task TestLocalActivityStateStoreRetentionAsync()
{
    var storeRootPath = Path.Combine(Path.GetTempPath(), $"desktop-activity-store-{Guid.NewGuid():N}");
    var backendRootPath = Path.Combine(Path.GetTempPath(), $"desktop-backend-{Guid.NewGuid():N}");
    var storagePolicy = new DesktopActivityStateStoragePolicy(storeRootPath);
    var store = new LocalActivityStateStore(
        storeRootPath,
        new DesktopActivityStatePolicy
        {
            Version = 1,
            MaxRecentActivitiesPerChannel = 6,
            RetentionWindow = TimeSpan.FromDays(7)
        },
        storagePolicy);
    var now = DateTimeOffset.UtcNow;

    store.Save(
        backendRootPath,
        new DesktopActivityState
        {
            Version = 1,
            QqRecentActivities =
            [
                new BackendRecentActivityItem
                {
                    EventKey = "req-old",
                    CapturedAt = now.AddDays(-30).ToString("O"),
                    EventType = "Request",
                    Summary = "old request",
                    Meta = "old",
                    Detail = "old detail"
                },
                new BackendRecentActivityItem
                {
                    EventKey = "req-new",
                    CapturedAt = now.ToString("O"),
                    EventType = "Request",
                    Summary = "new request",
                    Meta = "new",
                    Detail = "new detail"
                }
            ]
        });

    var restored = store.Load(backendRootPath);

    AssertEqual(1, restored.QqRecentActivities.Count, "LocalActivityStateStore should prune activity older than the retention window.");
    AssertEqual("req-new", restored.QqRecentActivities[0].EventKey, "LocalActivityStateStore should retain recent activity inside the retention window.");
    return Task.CompletedTask;
}

Task TestLocalActivityStateStoreVersionCleanupAsync()
{
    var storeRootPath = Path.Combine(Path.GetTempPath(), $"desktop-activity-store-{Guid.NewGuid():N}");
    var backendRootPath = Path.Combine(Path.GetTempPath(), $"desktop-backend-{Guid.NewGuid():N}");
    Directory.CreateDirectory(storeRootPath);
    var storagePolicy = new DesktopActivityStateStoragePolicy(storeRootPath);
    var store = new LocalActivityStateStore(
        storeRootPath,
        new DesktopActivityStatePolicy
        {
            Version = 2,
            MaxRecentActivitiesPerChannel = 6,
            RetentionWindow = TimeSpan.FromDays(14)
        },
        storagePolicy);
    var filePath = storagePolicy.ResolveStateFilePath(backendRootPath);

    File.WriteAllText(
        filePath,
        "{\"version\":999,\"qqRecentActivities\":[{\"eventKey\":\"req-1\"}]}",
        Encoding.UTF8);

    var restored = store.Load(backendRootPath);
    AssertEqual(0, restored.QqRecentActivities.Count, "Incompatible activity-state versions should be dropped.");
    AssertFalse(File.Exists(filePath), "Loading incompatible activity-state versions should clean up the persisted file.");

    store.Save(backendRootPath, new DesktopActivityState());
    AssertFalse(File.Exists(filePath), "Saving an empty/default activity state should delete the persisted file.");
    return Task.CompletedTask;
}

async Task TestBackendControlApiServiceCamelCaseContractAsync()
{
    var port = GetFreeTcpPort();
    var prefix = $"http://127.0.0.1:{port}/";
    using var listener = new HttpListener();
    listener.Prefixes.Add(prefix);
    listener.Start();

    var seenPutBody = string.Empty;
    var seenAuthorizationHeaders = new List<string>();
    var serverTask = Task.Run(async () =>
    {
        for (var index = 0; index < 3; index++)
        {
            var context = await listener.GetContextAsync();
            var request = context.Request;
            var response = context.Response;
            seenAuthorizationHeaders.Add(request.Headers["Authorization"] ?? string.Empty);

            try
            {
                switch ($"{request.HttpMethod} {request.Url?.AbsolutePath}")
                {
                    case "GET /config":
                        await WriteJsonAsync(
                            response,
                            new
                            {
                                openAiApiKey = "test-key",
                                deepSeekFallbackEnabled = "true",
                                deepSeekModel = "deepseek-chat",
                                deepSeekBaseUrl = "https://api.deepseek.com/v1",
                                allowedChatIds = "chat-a,chat-b",
                                allowedUserIds = "user-a",
                                configPath = "D:\\temp\\data\\runtime-settings.json",
                                bootstrapEnvPath = "D:\\temp\\.env",
                                restartRequired = false
                            });
                        break;
                    case "GET /status":
                        await WriteJsonAsync(
                            response,
                            new
                            {
                                runtimeActive = true,
                                runtimeReady = true,
                                napcatConnected = true,
                                activeLockCount = 0,
                                wechatConfigured = true,
                                wechatRuntimeActive = true,
                                wechatRuntimeReady = true,
                                wechatBridgeConnected = true,
                                lastQqLlmRequest = new
                                {
                                    route = "default",
                                    routeReason = "directive:/ai+web_search",
                                    matchedPrefix = "/ai",
                                    configuredModel = "gpt-5.4",
                                    model = "gpt-5.4",
                                    effectiveApiStyle = "responses",
                                    effectiveReasoningEffort = "medium",
                                    effectiveTextVerbosity = "medium",
                                    effectiveTools = new[] { "web_search" },
                                    executionKind = BackendExecutionProjectionTags.DirectKind,
                                    executionSummary = BackendExecutionProjectionTags.DirectKind,
                                    executionProjection = new
                                    {
                                        kind = BackendExecutionProjectionTags.DirectKind,
                                        summary = BackendExecutionProjectionTags.DirectKind,
                                        stages = new[] { BackendExecutionProjectionTags.DirectStage },
                                        failedStage = "",
                                        completedStages = new[] { BackendExecutionProjectionTags.DirectStage },
                                        degraded = false,
                                        recoveries = Array.Empty<string>()
                                    },
                                    decisionSummary = new
                                    {
                                        trigger = new
                                        {
                                            kind = "directive",
                                            matchedPrefix = "/ai"
                                        },
                                        reasonTags = new[] { "directive:/ai", "web_search" },
                                        reasonGroups = new
                                        {
                                            triggerReasons = new[] { "directive:/ai" },
                                            capabilityReasons = new[] { "web_search" },
                                            upgradeReasons = Array.Empty<string>()
                                        },
                                        requestedCapabilities = new
                                        {
                                            reasoningEffort = "medium",
                                            textVerbosity = "medium",
                                            enableWebSearch = true,
                                            enableCodeInterpreter = false,
                                            needsResponsesCapabilities = true
                                        },
                                        routeReason = "directive:/ai+web_search",
                                        matchedPrefix = "/ai"
                                    },
                                    imageCount = 0
                                }
                            });
                        break;
                    case "PUT /config":
                        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                        {
                            seenPutBody = await reader.ReadToEndAsync();
                        }

                        await WriteJsonAsync(
                            response,
                            new
                            {
                                openAiApiKey = "test-key",
                                deepSeekFallbackEnabled = "true",
                                deepSeekApiKey = "deepseek-key",
                                deepSeekModel = "deepseek-chat",
                                deepSeekBaseUrl = "https://api.deepseek.com/v1",
                                allowedChatIds = "chat-x,chat-y",
                                allowedUserIds = "user-a",
                                configPath = "D:\\temp\\data\\runtime-settings.json",
                                bootstrapEnvPath = "D:\\temp\\.env",
                                restartRequired = false,
                                savedAt = "2026-03-23T00:00:00.000Z"
                            });
                        break;
                    default:
                        response.StatusCode = 404;
                        await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("{\"error\":\"not found\"}"));
                        break;
                }
            }
            finally
            {
                response.Close();
            }
        }
    });

    using var service = new BackendControlApiService(new Uri(prefix), TimeSpan.FromSeconds(2));
    service.SetAccessToken("desktop-secret");

    var config = await service.TryGetConfigAsync() ?? throw new InvalidOperationException("Expected config response.");
    AssertEqual("chat-a,chat-b", config.AllowedChatIds, "Config should deserialize allowedChatIds.");
    AssertEqual("true", config.DeepSeekFallbackEnabled, "Config should deserialize DeepSeek fallback toggle.");
    AssertEqual("deepseek-chat", config.DeepSeekModel, "Config should deserialize DeepSeek model.");

    var status = await service.TryGetStatusAsync() ?? throw new InvalidOperationException("Expected status response.");
    AssertTrue(status.RuntimeActive, "RuntimeActive should deserialize from camelCase.");
    AssertTrue(status.RuntimeReady, "RuntimeReady should deserialize from camelCase.");
    AssertTrue(status.WechatConfigured, "WechatConfigured should deserialize from camelCase.");
    AssertTrue(status.WechatBridgeConnected, "WechatBridgeConnected should deserialize from camelCase.");
    AssertTrue(status.WechatRuntimeReady, "WechatRuntimeReady should deserialize from camelCase.");
    AssertEqual("default", status.LastQqLlmRequest?.Route ?? string.Empty, "LastQqLlmRequest route should deserialize from camelCase.");
    AssertEqual("gpt-5.4", status.LastQqLlmRequest?.ConfiguredModel ?? string.Empty, "LastQqLlmRequest configured model should deserialize from camelCase.");
    AssertEqual("responses", status.LastQqLlmRequest?.EffectiveApiStyle ?? string.Empty, "LastQqLlmRequest api style should deserialize from camelCase.");
    AssertEqual(BackendExecutionProjectionTags.DirectKind, status.LastQqLlmRequest?.ExecutionKind ?? string.Empty, "LastQqLlmRequest execution kind should deserialize from camelCase.");
    AssertEqual(BackendExecutionProjectionTags.DirectKind, status.LastQqLlmRequest?.ExecutionSummary ?? string.Empty, "LastQqLlmRequest execution summary should deserialize from camelCase.");
    AssertEqual(BackendExecutionProjectionTags.DirectKind, status.LastQqLlmRequest?.ExecutionProjection?.Kind ?? string.Empty, "LastQqLlmRequest execution projection kind should deserialize from camelCase.");
    AssertEqual(BackendExecutionProjectionTags.DirectStage, status.LastQqLlmRequest?.ExecutionProjection?.Stages?.FirstOrDefault() ?? string.Empty, "LastQqLlmRequest execution projection stages should deserialize from camelCase.");
    AssertEqual(string.Empty, status.LastQqLlmRequest?.ExecutionProjection?.FailedStage ?? string.Empty, "LastQqLlmRequest execution projection failed stage should deserialize from camelCase.");
    AssertEqual(BackendExecutionProjectionTags.DirectStage, status.LastQqLlmRequest?.ExecutionProjection?.CompletedStages?.FirstOrDefault() ?? string.Empty, "LastQqLlmRequest execution projection completed stages should deserialize from camelCase.");
    AssertFalse(status.LastQqLlmRequest?.ExecutionProjection?.Degraded == true, "LastQqLlmRequest execution projection degraded flag should deserialize from camelCase.");
    AssertEqual(0, status.LastQqLlmRequest?.ExecutionProjection?.Recoveries?.Length ?? 0, "LastQqLlmRequest execution projection recoveries should deserialize from camelCase.");
    AssertEqual("directive", status.LastQqLlmRequest?.DecisionSummary?.Trigger?.Kind ?? string.Empty, "DecisionSummary trigger kind should deserialize from camelCase.");
    AssertEqual("/ai", status.LastQqLlmRequest?.DecisionSummary?.MatchedPrefix ?? string.Empty, "DecisionSummary matchedPrefix should deserialize from camelCase.");
    AssertTrue(status.LastQqLlmRequest?.DecisionSummary?.ReasonGroups?.CapabilityReasons?.Contains("web_search") == true, "DecisionSummary capability reasons should deserialize from camelCase.");

    var saveResult = await service.TrySaveConfigAsync(
        new BotConfig
        {
            OpenAiApiKey = "test-key",
            DeepSeekFallbackEnabled = "true",
            DeepSeekApiKey = "deepseek-key",
            DeepSeekModel = "deepseek-chat",
            DeepSeekBaseUrl = "https://api.deepseek.com/v1",
            AllowedChatIds = "chat-x,chat-y",
            AllowedUserIds = "user-a"
        }) ?? throw new InvalidOperationException("Expected save response.");
    AssertEqual("chat-x,chat-y", saveResult.AllowedChatIds, "Save response should deserialize allowedChatIds.");
    AssertEqual("deepseek-key", saveResult.DeepSeekApiKey, "Save response should deserialize DeepSeek API key.");
    AssertContains(seenPutBody, "\"allowedChatIds\":\"chat-x,chat-y\"", "PUT body should use camelCase allowedChatIds.");
    AssertContains(seenPutBody, "\"deepSeekFallbackEnabled\":\"true\"", "PUT body should include DeepSeek fallback toggle.");
    AssertContains(seenPutBody, "\"deepSeekApiKey\":\"deepseek-key\"", "PUT body should include DeepSeek API key.");
    AssertDoesNotContain(seenPutBody, "allowedGroupIds", "PUT body should not contain legacy field.");
    AssertTrue(seenAuthorizationHeaders.All(static header => header == "Bearer desktop-secret"), "Control API service should send the configured bearer token on every request.");

    listener.Stop();
    await serverTask;
}

Task TestBackendExecutionProjectionFormatterAsync()
{
    var directText = BackendExecutionProjectionFormatter.Format(
        new BackendExecutionProjection
        {
            Kind = BackendExecutionProjectionTags.DirectKind,
            Summary = BackendExecutionProjectionTags.DirectKind,
            Stages = [BackendExecutionProjectionTags.DirectStage],
            CompletedStages = [BackendExecutionProjectionTags.DirectStage]
        },
        BackendExecutionProjectionTags.DirectKind,
        BackendExecutionProjectionTags.DirectKind);

    AssertEqual("execution=direct", directText, "Direct execution formatting should suppress redundant completed-path details.");

    var degradedText = BackendExecutionProjectionFormatter.Format(
        new BackendExecutionProjection
        {
            Kind = BackendExecutionProjectionTags.DeliberationKind,
            Summary = BackendExecutionProjectionTags.DeliberationSummary,
            Stages = BackendExecutionProjectionTags.DeliberationStages,
            CompletedStages =
            [
                BackendExecutionProjectionTags.PlannerStage,
                BackendExecutionProjectionTags.DraftStage
            ],
            Degraded = true,
            Recoveries = [BackendExecutionProjectionTags.RewriteFallbackToDraftRecovery]
        },
        BackendExecutionProjectionTags.DeliberationKind,
        BackendExecutionProjectionTags.DeliberationSummary);

    AssertContains(degradedText, "execution=deliberation", "Deliberation formatting should include execution kind.");
    AssertContains(degradedText, "stages=planner->draft->rewrite", "Deliberation formatting should include planned stage path.");
    AssertContains(degradedText, "completed=planner->draft", "Deliberation formatting should include actual completed stages when degraded.");
    AssertContains(degradedText, "degraded=yes", "Deliberation formatting should mark degraded success.");
    AssertContains(degradedText, "recoveries=rewrite-fallback-to-draft", "Deliberation formatting should include recovery tags.");
    return Task.CompletedTask;
}

Task TestBackendLlmProjectionFormatterAsync()
{
    var requestDetail = BackendLlmProjectionFormatter.FormatRequestDetail(
        new BackendLlmRequestStatus
        {
            Route = "advanced",
            RouteReason = "directive:/gpt",
            MatchedPrefix = "/gpt",
            CapturedAt = "2026-03-24T00:00:01.000Z",
            ConfiguredModel = "gpt-5.4",
            Model = "deepseek-chat",
            EffectiveReasoningEffort = "high",
            EffectiveTextVerbosity = "high",
            EffectiveTools = ["web_search", "code_interpreter"],
            ImageCount = 1,
            ExecutionProjection = new BackendExecutionProjection
            {
                Kind = BackendExecutionProjectionTags.DeliberationKind,
                Summary = BackendExecutionProjectionTags.DeliberationSummary,
                Stages = BackendExecutionProjectionTags.DeliberationStages,
                CompletedStages = [BackendExecutionProjectionTags.PlannerStage, BackendExecutionProjectionTags.DraftStage],
                Degraded = true,
                Recoveries = [BackendExecutionProjectionTags.RewriteFallbackToDraftRecovery]
            },
            DecisionSummary = new BackendDecisionSummary
            {
                Trigger = new BackendDecisionTrigger
                {
                    Kind = "directive",
                    MatchedPrefix = "/gpt"
                },
                ReasonGroups = new BackendDecisionReasonGroups
                {
                    TriggerReasons = ["directive:/gpt"],
                    CapabilityReasons = [],
                    UpgradeReasons = []
                }
            }
        });

    AssertContains(requestDetail, "trigger=directive:/gpt", "Request detail should include decision trigger.");
    AssertContains(requestDetail, "execution=deliberation", "Request detail should include execution kind.");
    AssertContains(requestDetail, "completed=planner->draft", "Request detail should include completed stages when degraded.");
    AssertContains(requestDetail, "recoveries=rewrite-fallback-to-draft", "Request detail should include recovery tags.");
    AssertContains(requestDetail, "configured_model=gpt-5.4", "Request detail should include configured-vs-actual model context.");

    var requestedCapabilitiesDetail = BackendLlmProjectionFormatter.FormatRequestedCapabilities(
        new BackendLlmRequestStatus
        {
            Route = "advanced",
            EffectiveReasoningEffort = "high",
            EffectiveTextVerbosity = "high",
            DecisionSummary = new BackendDecisionSummary
            {
                RequestedCapabilities = new BackendRequestedCapabilities
                {
                    ReasoningEffort = "high",
                    TextVerbosity = "high",
                    EnableWebSearch = true,
                    EnableCodeInterpreter = true,
                    NeedsResponsesCapabilities = true
                }
            }
        });

    AssertContains(requestedCapabilitiesDetail, "tools=web_search (Web Search), code_interpreter (Code Interpreter)", "Requested capabilities formatting should derive registry labels from compatibility booleans when explicit requestedTools are absent.");

    var failureDetail = BackendLlmProjectionFormatter.FormatFailureDetail(
        new BackendLlmFailureStatus
        {
            Route = "advanced",
            MatchedPrefix = "/gpt",
            Error = "provider rejected request",
            CapturedAt = "2026-03-24T00:00:03.000Z",
            ExecutionProjection = new BackendExecutionProjection
            {
                Kind = BackendExecutionProjectionTags.DeliberationKind,
                Summary = BackendExecutionProjectionTags.DeliberationSummary,
                Stages = BackendExecutionProjectionTags.DeliberationStages,
                FailedStage = BackendExecutionProjectionTags.DraftStage,
                CompletedStages = [BackendExecutionProjectionTags.PlannerStage]
            },
            DecisionSummary = new BackendDecisionSummary
            {
                Trigger = new BackendDecisionTrigger
                {
                    Kind = "directive",
                    MatchedPrefix = "/gpt"
                },
                ReasonGroups = new BackendDecisionReasonGroups
                {
                    CapabilityReasons = ["web_search"],
                    UpgradeReasons = ["capability_upgrade"]
                }
            }
        });

    AssertContains(failureDetail, "trigger=directive:/gpt", "Failure detail should include decision trigger.");
    AssertContains(failureDetail, "failed_stage=draft", "Failure detail should include failed stage.");
    AssertContains(failureDetail, "capability=web_search", "Failure detail should include capability reasons.");
    AssertContains(failureDetail, "upgrade=capability_upgrade", "Failure detail should include upgrade reasons.");
    AssertContains(failureDetail, "error=provider rejected request", "Failure detail should include backend error.");
    return Task.CompletedTask;
}

Task TestBackendActivityProjectionFormatterAsync()
{
    var request = new BackendLlmRequestStatus
    {
        Route = "advanced",
        ConfiguredModel = "gpt-5.4",
        Model = "deepseek-chat",
        EffectiveApiStyle = "chat_completions",
        CapturedAt = "2026-03-24T00:00:05.000Z"
    };
    var failure = new BackendLlmFailureStatus
    {
        Route = "advanced",
        CapturedAt = "2026-03-24T00:00:03.000Z",
        Error = "provider rejected request"
    };

    AssertEqual(
        "advanced / deepseek-chat / chat_completions (configured gpt-5.4)",
        BackendActivityProjectionFormatter.FormatRequestSummary(request, "empty"),
        "Activity formatter should build request summaries.");
    AssertContains(
        BackendActivityProjectionFormatter.FormatActivitySummary(request, failure, "empty"),
        "Latest event: request",
        "Activity formatter should report the latest event.");
    AssertContains(
        BackendActivityProjectionFormatter.FormatRecentActivity(
            [
                new BackendRecentActivityItem
                {
                    EventType = "Request",
                    Summary = "advanced / deepseek-chat / chat_completions (configured gpt-5.4)"
                }
            ],
            "empty"),
        "Request | advanced / deepseek-chat / chat_completions (configured gpt-5.4)",
        "Activity formatter should render recent activity lists.");
    AssertEqual(
        "Recovered after failure",
        BackendActivityProjectionFormatter.FormatActivityState(request, failure),
        "Activity formatter should describe recovered state.");
    AssertContains(
        BackendActivityProjectionFormatter.FormatLatestSuccess(request),
        "Success |",
        "Activity formatter should render latest success checkpoint.");
    AssertContains(
        BackendActivityProjectionFormatter.FormatLatestFailure(failure),
        "Failure |",
        "Activity formatter should render latest failure checkpoint.");
    AssertContains(
        BackendActivityProjectionFormatter.FormatRecoveryState(request, failure),
        "Recovery |",
        "Activity formatter should render recovery checkpoint.");
    AssertContains(
        BackendActivityProjectionFormatter.FormatRequestTimeline(request),
        "Request |",
        "Activity formatter should render request timeline.");
    AssertContains(
        BackendActivityProjectionFormatter.FormatFailureSummary(failure, "empty"),
        "advanced /",
        "Activity formatter should render failure summary.");
    AssertContains(
        BackendActivityProjectionFormatter.FormatFailureTimeline(failure),
        "Failure |",
        "Activity formatter should render failure timeline.");
    return Task.CompletedTask;
}

Task TestBackendLatestTurnOverviewBuilderAsync()
{
    var failureOverview = BackendLatestTurnOverviewBuilder.Build(
        new BackendRuntimeSnapshotViewState
        {
            LastQqLlmRequest = new BackendLlmRequestStatus
            {
                Route = "default",
                Model = "gpt-5.4",
                EffectiveApiStyle = "responses",
                CapturedAt = "2026-03-24T00:00:04.000Z",
                DecisionSummary = new BackendDecisionSummary
                {
                    Trigger = new BackendDecisionTrigger
                    {
                        Kind = "default"
                    },
                    ReasonGroups = new BackendDecisionReasonGroups
                    {
                        CapabilityReasons = ["default"],
                        UpgradeReasons = []
                    },
                    RequestedCapabilities = new BackendRequestedCapabilities
                    {
                        EnableWebSearch = false,
                        EnableCodeInterpreter = false
                    },
                    RouteReason = "default"
                },
                ExecutionKind = BackendExecutionProjectionTags.DirectKind
            },
            LastWechatLlmFailure = new BackendLlmFailureStatus
            {
                Route = "advanced",
                MatchedPrefix = "/gpt",
                CapturedAt = "2026-03-24T00:00:05.000Z",
                Error = "provider rejected request",
                ExecutionKind = BackendExecutionProjectionTags.DeliberationKind,
                ExecutionProjection = new BackendExecutionProjection
                {
                    Kind = BackendExecutionProjectionTags.DeliberationKind,
                    FailedStage = BackendExecutionProjectionTags.DraftStage,
                    CompletedStages = [BackendExecutionProjectionTags.PlannerStage]
                },
                DecisionSummary = new BackendDecisionSummary
                {
                    Trigger = new BackendDecisionTrigger
                    {
                        Kind = "directive",
                        MatchedPrefix = "/gpt"
                    },
                    ReasonGroups = new BackendDecisionReasonGroups
                    {
                        CapabilityReasons = ["default"],
                        UpgradeReasons = ["capability_upgrade"]
                    },
                    RequestedCapabilities = new BackendRequestedCapabilities
                    {
                        EnableWebSearch = true,
                        EnableCodeInterpreter = true
                    },
                    RouteReason = "directive:/gpt"
                }
            }
        });

    AssertEqual(DesktopHealthState.Warning, failureOverview.State, "Latest-turn overview should mark the card as warning when the newest event is a failure.");
    AssertContains(failureOverview.Capabilities, "web_search (Web Search), code_interpreter (Code Interpreter)", "Latest-turn overview should surface registry-based tool labels.");
    AssertContains(failureOverview.Headline, "微信", "Latest-turn overview should identify the failing channel.");
    AssertContains(failureOverview.Headline, "失败", "Latest-turn overview should identify the failing channel.");
    AssertContains(failureOverview.Summary, "advanced 路由", "Latest-turn overview should keep route-level context visible.");
    AssertContains(failureOverview.Capabilities, "联网 开", "Latest-turn overview should surface whether web search was requested.");
    AssertContains(failureOverview.Capabilities, "代码 开", "Latest-turn overview should surface whether code interpreter was requested.");
    AssertContains(failureOverview.Reason, "触发 directive /gpt", "Latest-turn overview should surface the trigger in user-facing form.");
    AssertContains(failureOverview.Outcome, "于 draft 阶段失败", "Latest-turn overview should compress the failing execution stage.");
    AssertContains(failureOverview.Outcome, "planner", "Latest-turn overview should mention completed execution stages.");
    AssertEqual("查看微信失败", failureOverview.ActionLabel, "Latest-turn overview should route failures to the relevant channel review action.");
    AssertEqual(DesktopHealthActionKeys.FocusWechatFailure, failureOverview.ActionKey, "Latest-turn overview should expose the failing-channel action key.");

    var localReplyOverview = BackendLatestTurnOverviewBuilder.Build(
        new BackendRuntimeSnapshotViewState
        {
            LastQqLlmRequest = new BackendLlmRequestStatus
            {
                Route = "default",
                Model = "gpt-5.4",
                EffectiveApiStyle = "responses",
                CapturedAt = "2026-03-24T00:00:06.000Z",
                ImageCount = 0,
                ExecutionKind = BackendExecutionProjectionTags.LocalCapabilityReplyKind,
                ExecutionProjection = new BackendExecutionProjection
                {
                    Kind = BackendExecutionProjectionTags.LocalCapabilityReplyKind
                },
                DecisionSummary = new BackendDecisionSummary
                {
                    Trigger = new BackendDecisionTrigger
                    {
                        Kind = "default"
                    },
                    ReasonGroups = new BackendDecisionReasonGroups
                    {
                        CapabilityReasons = ["default"],
                        UpgradeReasons = []
                    },
                    RequestedCapabilities = new BackendRequestedCapabilities
                    {
                        EnableWebSearch = false,
                        EnableCodeInterpreter = false
                    },
                    RouteReason = "default"
                }
            }
        });

    AssertEqual(DesktopHealthState.Good, localReplyOverview.State, "Successful latest turns should surface as healthy.");
    AssertContains(localReplyOverview.Headline, "已本地回复", "Latest-turn overview should distinguish local replies from LLM calls.");
    AssertContains(localReplyOverview.Outcome, "未调用 LLM", "Latest-turn overview should explicitly state when the turn stayed local.");
    AssertEqual("查看最近活动", localReplyOverview.ActionLabel, "Successful latest turns should route to recent-activity review.");
    AssertEqual(DesktopHealthActionKeys.FocusLatestActivity, localReplyOverview.ActionKey, "Successful latest turns should expose the generic recent-activity action.");

    var fallbackOverview = BackendLatestTurnOverviewBuilder.Build(
        new BackendRuntimeSnapshotViewState
        {
            LastQqLlmRequest = new BackendLlmRequestStatus
            {
                Route = "default",
                ConfiguredModel = "gpt-5.4",
                Model = "deepseek-chat",
                EffectiveApiStyle = "chat_completions",
                CapturedAt = "2026-03-24T00:00:07.000Z",
                ExecutionKind = BackendExecutionProjectionTags.DirectKind,
                ExecutionProjection = new BackendExecutionProjection
                {
                    Kind = BackendExecutionProjectionTags.DirectKind,
                    CompletedStages = [BackendExecutionProjectionTags.DirectStage],
                    Degraded = true,
                    Recoveries = ["provider-fallback-to-deepseek"]
                },
                DecisionSummary = new BackendDecisionSummary
                {
                    Trigger = new BackendDecisionTrigger
                    {
                        Kind = "default"
                    },
                    ReasonGroups = new BackendDecisionReasonGroups
                    {
                        CapabilityReasons = ["default"],
                        UpgradeReasons = []
                    },
                    RequestedCapabilities = new BackendRequestedCapabilities
                    {
                        EnableWebSearch = false,
                        EnableCodeInterpreter = false
                    },
                    RouteReason = "default"
                }
            }
        });
    AssertContains(fallbackOverview.Outcome, "DeepSeek", "Latest-turn overview should call out provider fallback recoveries.");
    AssertContains(fallbackOverview.Outcome, "GPT", "Latest-turn overview should explain the original provider failure path.");

    var emptyOverview = BackendLatestTurnOverviewBuilder.Build(new BackendRuntimeSnapshotViewState());
    AssertContains(emptyOverview.Headline, "还没有最近的 QQ 或微信活动", "Latest-turn overview should expose a calm empty state when nothing has run yet.");

    return Task.CompletedTask;
}

Task TestDesktopHealthGuidanceBuilderAsync()
{
    DesktopHealthCheckItem[] setupChecks =
    [
        new DesktopHealthCheckItem
        {
            Key = "openai",
            Title = "OpenAI",
            State = DesktopHealthState.Error,
            Detail = "API key is missing.",
            IsBlocking = true,
            ActionLabel = "查看 API 密钥",
            ActionKey = DesktopHealthActionKeys.FocusOpenAiDefaultKey
        },
        new DesktopHealthCheckItem
        {
            Key = "qq",
            Title = "QQ",
            State = DesktopHealthState.Error,
            Detail = "NapCat token is missing.",
            IsBlocking = true,
            ActionLabel = "查看 NapCat Token",
            ActionKey = DesktopHealthActionKeys.FocusNapCatToken
        }
    ];
    var setupGuidance = DesktopHealthGuidanceBuilder.Build(
        new BotConfig
        {
            NapCatWsUrl = "ws://127.0.0.1:3001"
        },
        new BackendRuntimeSnapshotViewState(),
        new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unreachable,
            Message = "Control API is unreachable."
        },
        setupChecks,
        isBackendRootValid: true,
        hasUnsavedChanges: false,
        autoStartEnabled: false);

    AssertEqual(string.Empty, setupGuidance.LatestIssueActionLabel, "Guidance builder should keep latest-issue actions out of the way when setup is still blocking.");
    AssertEqual("添加 OpenAI 兼容密钥", setupGuidance.NextActions[0].Title, "Guidance builder should prioritize the OpenAI blocker first.");
    AssertEqual("补上 NapCat Token", setupGuidance.NextActions[1].Title, "Guidance builder should keep the QQ credential blocker visible next.");
    AssertContains(setupGuidance.ActionSummary, "当前重点：添加 OpenAI 兼容密钥", "Guidance builder should compress blocking setup into a single focus sentence.");

    var runtimeGuidance = DesktopHealthGuidanceBuilder.Build(
        new BotConfig
        {
            OpenAiApiKey = "test-key",
            NapCatWsUrl = "ws://127.0.0.1:3001",
            NapCatToken = "napcat-test-token"
        },
        new BackendRuntimeSnapshotViewState
        {
            ControlApiReachable = true,
            RuntimeActive = false,
            RuntimeReady = false
        },
        new BackendControlApiFailure(),
        [],
        isBackendRootValid: true,
        hasUnsavedChanges: false,
        autoStartEnabled: false);

    AssertEqual("启动后端", runtimeGuidance.NextActions[0].Title, "Guidance builder should prioritize backend start once setup is complete.");
    AssertEqual(DesktopHealthActionKeys.StartBackend, runtimeGuidance.NextActions[0].ActionKey, "Guidance builder should expose the direct backend start action.");
    AssertEqual("稍后启用开机启动（可选）", runtimeGuidance.NextActions[1].Title, "Guidance builder should keep resident-mode setup as a lower-priority follow-up.");

    var latestIssueGuidance = DesktopHealthGuidanceBuilder.Build(
        new BotConfig
        {
            OpenAiApiKey = "test-key",
            NapCatWsUrl = "ws://127.0.0.1:3001",
            NapCatToken = "napcat-test-token"
        },
        new BackendRuntimeSnapshotViewState
        {
            ControlApiReachable = true,
            RuntimeActive = true,
            RuntimeReady = true,
            LastWechatLlmFailure = new BackendLlmFailureStatus
            {
                CapturedAt = "2026-03-24T00:00:05.000Z",
                Error = "provider rejected request"
            }
        },
        new BackendControlApiFailure(),
        [],
        isBackendRootValid: true,
        hasUnsavedChanges: false,
        autoStartEnabled: true);

    AssertContains(latestIssueGuidance.LatestIssueText, "微信", "Guidance builder should surface the latest runtime failure summary.");
    AssertContains(latestIssueGuidance.LatestIssueText, "失败", "Guidance builder should surface the latest runtime failure summary.");
    AssertEqual("查看微信失败", latestIssueGuidance.LatestIssueActionLabel, "Guidance builder should expose the failing-channel review action.");
    AssertEqual(DesktopHealthActionKeys.FocusWechatFailure, latestIssueGuidance.LatestIssueActionKey, "Guidance builder should route the latest issue to the failing channel.");
    AssertEqual("查看最新问题", latestIssueGuidance.NextActions[0].Title, "Guidance builder should turn the latest runtime failure into the first follow-up action when no higher-priority setup/runtime action exists.");

    return Task.CompletedTask;
}

Task TestDesktopGuideFlowBuilderAsync()
{
    var firstRunFlow = DesktopGuideFlowBuilder.Build(
        new DesktopGuideFlowContext
        {
            IsBackendRootValid = true,
            HasUnsavedChanges = false,
            CanStartBackend = true,
            IsProcessRunning = false,
            IsQqRuntimeReady = false,
            AutoStartEnabled = false,
            HealthChecks =
            [
                new DesktopHealthCheckItem
                {
                    Key = "control-api",
                    Title = "Control API",
                    State = DesktopHealthState.Warning,
                    Detail = "Desktop is offline from the live backend."
                },
                new DesktopHealthCheckItem
                {
                    Key = "openai",
                    Title = "LLM credentials",
                    State = DesktopHealthState.Good,
                    Detail = "Configured"
                }
            ]
        });

    AssertFalse(firstRunFlow.IsFirstRunGuideComplete, "Guide flow should keep first-run incomplete while the runtime is still offline.");
    AssertContains(firstRunFlow.FirstRunGuideText, "首次打开", "Guide flow should expose the first-run summary text.");
    AssertContains(firstRunFlow.FirstRunGuideProgressText, "已完成 2/3", "Guide flow should compute first-run progress from step completion.");
    AssertContains(firstRunFlow.FirstRunGuideCurrentStepText, "让 runtime 上线", "Guide flow should compute the current first-run step summary.");
    AssertEqual("让 runtime 上线", firstRunFlow.FirstRunGuideSteps[2].Title, "Guide flow should keep the runtime-online step as the third first-run action.");
    AssertTrue(firstRunFlow.FirstRunGuideSteps[2].IsCurrent, "Guide flow should highlight bringing the runtime online once 必填设置已完成.");
    AssertEqual("启动后端", firstRunFlow.FirstRunGuideSteps[2].ActionLabel, "Guide flow should expose a direct backend start action for the first-run runtime step.");
    AssertFalse(firstRunFlow.IsOverallReadinessReady, "Overall readiness should stay incomplete before daily-use prerequisites are finished.");
    AssertEqual("设置进行中", firstRunFlow.OverallReadinessStateText, "Guide flow should report setup-in-progress before the runtime is online.");
    AssertEqual("启动后端", firstRunFlow.OverallReadinessActionLabel, "Overall readiness should route to the current first-run action before setup is complete.");

    var issueFlow = DesktopGuideFlowBuilder.Build(
        new DesktopGuideFlowContext
        {
            IsBackendRootValid = true,
            HasUnsavedChanges = false,
            CanStartBackend = false,
            IsProcessRunning = true,
            IsQqRuntimeReady = true,
            AutoStartEnabled = true,
            HealthLatestIssueText = "最新问题：微信在 2026-03-24 08:00:00 失败。provider rejected request",
            HealthLatestIssueActionLabel = "查看微信失败",
            HealthLatestIssueActionKey = DesktopHealthActionKeys.FocusWechatFailure,
            QqRecentActivities =
            [
                new BackendRecentActivityItem
                {
                    EventKey = "req-1",
                    EventType = "Request",
                    Summary = "default / gpt-5.4",
                    CapturedAt = "2026-03-24T08:00:01.000Z",
                    Meta = "2026-03-24 08:00:01"
                }
            ]
        });

    AssertFalse(issueFlow.IsDailyUseGuideComplete, "Daily-use guide should stay incomplete while a latest issue still needs review.");
    AssertContains(issueFlow.DailyUseGuideText, "日常常驻", "Guide flow should expose the daily-use summary text.");
    AssertContains(issueFlow.DailyUseGuideProgressText, "已完成 2/3", "Guide flow should compute daily-use progress from the step states.");
    AssertContains(issueFlow.DailyUseGuideCurrentStepText, "有异常时查看最新问题", "Guide flow should compute the current daily-use step summary.");
    AssertTrue(issueFlow.DailyUseGuideSteps[2].IsCurrent, "Guide flow should highlight the latest-issue review step after runtime reachability and startup are handled.");
    AssertContains(issueFlow.DailyUseGuideSteps[2].Detail, "provider rejected request", "Guide flow should surface the latest issue detail in the daily-use review step.");
    AssertEqual("查看微信失败", issueFlow.OverallReadinessActionLabel, "Overall readiness should route to the current daily-use issue action when setup is complete but an issue remains.");
    AssertContains(issueFlow.OverallReadinessRecentActivityText, "最近活动：QQ Request", "Guide flow should summarize the latest recent activity across channels.");

    var readyFlow = DesktopGuideFlowBuilder.Build(
        new DesktopGuideFlowContext
        {
            IsBackendRootValid = true,
            HasUnsavedChanges = false,
            CanStartBackend = false,
            IsProcessRunning = true,
            IsQqRuntimeReady = true,
            AutoStartEnabled = true,
            QqRecentActivities =
            [
                new BackendRecentActivityItem
                {
                    EventKey = "req-2",
                    EventType = "Request",
                    Summary = "advanced / gpt-5.4",
                    CapturedAt = "2026-03-24T09:00:00.000Z",
                    Meta = "2026-03-24 09:00:00"
                }
            ]
        });

    AssertTrue(readyFlow.IsFirstRunGuideComplete, "Guide flow should mark first-run complete once QQ is online.");
    AssertTrue(readyFlow.IsDailyUseGuideComplete, "Guide flow should mark daily-use complete once runtime is online, startup is enabled, and no latest issue remains.");
    AssertContains(readyFlow.FirstRunGuideCompletionText, "首次安装已完成", "Guide flow should expose the first-run completion text once setup is fully complete.");
    AssertContains(readyFlow.DailyUseGuideCompletionText, "已进入日常常驻模式", "Guide flow should expose the daily-use completion text once the desktop is in steady-state use.");
    AssertTrue(readyFlow.IsOverallReadinessReady, "Overall readiness should be ready once both guide tracks are complete.");
    AssertEqual("可日常使用", readyFlow.OverallReadinessStateText, "Guide flow should expose the ready state when all guide requirements are complete.");
    AssertEqual("查看最近活动", readyFlow.OverallReadinessActionLabel, "Guide flow should still offer recent-activity review when the system is otherwise calm.");
    AssertEqual(DesktopHealthActionKeys.FocusLatestActivity, readyFlow.OverallReadinessActionKey, "Guide flow should route the ready-state action to recent activity.");

    return Task.CompletedTask;
}

Task TestDesktopHealthChecklistBuilderAsync()
{
    var setupChecklist = DesktopHealthChecklistBuilder.Build(
        new BotConfig
        {
            NapCatWsUrl = "ws://127.0.0.1:3001"
        },
        new BackendRuntimeSnapshotViewState(),
        new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unreachable,
            Message = "Control API is unreachable."
        },
        isBackendRootValid: true,
        autoStartEnabled: false);

    AssertEqual(5, setupChecklist.Checks.Count, "Checklist builder should produce the fixed desktop checklist.");
    AssertContains(setupChecklist.ChecklistStatus, "还有 2 个必填项待处理", "Checklist builder should summarize the remaining first-run blockers.");
    AssertEqual("缺少 API Key", setupChecklist.Checks[1].StateText, "Checklist builder should preserve the OpenAI blocker state.");
    AssertEqual(DesktopHealthActionKeys.FocusNapCatToken, setupChecklist.Checks[2].ActionKey, "Checklist builder should preserve the QQ token remediation action.");

    var startupChecklist = DesktopHealthChecklistBuilder.Build(
        new BotConfig
        {
            OpenAiApiKey = "test-key",
            NapCatWsUrl = "ws://127.0.0.1:3001",
            NapCatToken = "napcat-test-token"
        },
        new BackendRuntimeSnapshotViewState
        {
            ControlApiReachable = true,
            RuntimeActive = true,
            RuntimeReady = true
        },
        new BackendControlApiFailure(),
        isBackendRootValid: true,
        autoStartEnabled: true);

    AssertContains(startupChecklist.ChecklistStatus, "必填设置已完成", "Checklist builder should distinguish completed setup from runtime follow-up.");
    AssertEqual("随 Windows 启动", startupChecklist.Checks[4].StateText, "Checklist builder should preserve the resident-mode enabled state.");
    AssertEqual(string.Empty, startupChecklist.Checks[4].ActionLabel, "Checklist builder should clear the resident-mode action when startup is already enabled.");

    return Task.CompletedTask;
}

Task TestDesktopHealthStatusBuilderAsync()
{
    var setupStatus = DesktopHealthStatusBuilder.Build(
        new BotConfig
        {
            NapCatWsUrl = "ws://127.0.0.1:3001"
        },
        new BackendRuntimeSnapshotViewState(),
        new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unreachable,
            Message = "Control API is unreachable."
        },
        isBackendRootValid: true);

    AssertEqual(DesktopHealthState.Error, setupStatus.State, "Status builder should surface setup blockers as an error state.");
    AssertEqual("需要设置", setupStatus.StateText, "Status builder should expose the setup-needed state text.");
    AssertContains(setupStatus.ReadyNowText, "还不行", "Status builder should explicitly say the runtime is not ready during first-run blockers.");
    AssertEqual("查看 API 密钥", setupStatus.PrimaryActionLabel, "Status builder should preserve the first-run remediation action.");

    var readyToStartStatus = DesktopHealthStatusBuilder.Build(
        new BotConfig
        {
            OpenAiApiKey = "test-key",
            NapCatWsUrl = "ws://127.0.0.1:3001",
            NapCatToken = "napcat-test-token"
        },
        new BackendRuntimeSnapshotViewState
        {
            ControlApiReachable = true,
            RuntimeActive = false,
            RuntimeReady = false
        },
        new BackendControlApiFailure(),
        isBackendRootValid: true);

    AssertEqual(DesktopHealthState.Warning, readyToStartStatus.State, "Status builder should use a warning state when config is ready but the backend is stopped.");
    AssertEqual("可启动", readyToStartStatus.StateText, "Status builder should expose ready-to-start state text.");
    AssertContains(readyToStartStatus.ReadyNowText, "QQ 已具备启动条件", "Status builder should explain that QQ can be started now.");
    AssertContains(readyToStartStatus.RuntimeExplanation, "QQ worker 已停止", "Status builder should explain the stopped runtime state.");
    AssertEqual(DesktopHealthActionKeys.StartBackend, readyToStartStatus.PrimaryActionKey, "Status builder should preserve the direct start action.");

    return Task.CompletedTask;
}

Task TestDesktopHealthReportBuilderAsync()
{
    var setupBlockedReport = DesktopHealthReportBuilder.Build(
        new BotConfig
        {
            NapCatWsUrl = "ws://127.0.0.1:3001"
        },
        new BackendRuntimeSnapshotViewState(),
        new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unreachable,
            Message = "Control API is unreachable."
        },
        isBackendRootValid: true,
        hasUnsavedChanges: false,
        autoStartEnabled: false);

    AssertEqual(DesktopHealthState.Error, setupBlockedReport.State, "Health report should flag missing first-run config as an error.");
    AssertEqual("需要设置", setupBlockedReport.StateText, "Health report should surface setup-needed state text.");
    AssertContains(setupBlockedReport.Summary, "OpenAI 兼容 API key", "Health report summary should explain the missing model credential.");
    AssertContains(setupBlockedReport.ChecklistStatus, "还有 2 个必填项待处理", "Health report checklist should count first-run blockers.");
    AssertContains(setupBlockedReport.ReadyNowText, "还不行", "Health report should clearly say the runtime is not ready during first-run blockers.");
    AssertContains(setupBlockedReport.PrimaryAction, "OPENAI_API_KEY", "Health report should tell the user how to unblock first run.");
    AssertEqual("查看 API 密钥", setupBlockedReport.PrimaryActionLabel, "Health report should expose a primary remediation label.");
    AssertEqual(DesktopHealthActionKeys.FocusOpenAiDefaultKey, setupBlockedReport.PrimaryActionKey, "Health report should expose a primary remediation action key.");
    AssertEqual(5, setupBlockedReport.Checks.Count, "Health report should produce the fixed checklist plus resident mode.");
    AssertEqual("缺少 API Key", setupBlockedReport.Checks[1].StateText, "OpenAI checklist item should explain the missing key.");
    AssertEqual("查看 API 密钥", setupBlockedReport.Checks[1].ActionLabel, "OpenAI checklist item should expose a remediation action.");
    AssertEqual("缺少 Token", setupBlockedReport.Checks[2].StateText, "QQ checklist item should explain the missing NapCat token.");
    AssertEqual(DesktopHealthActionKeys.FocusNapCatToken, setupBlockedReport.Checks[2].ActionKey, "QQ checklist item should expose the token focus action.");
    AssertEqual(string.Empty, setupBlockedReport.LatestIssueActionLabel, "Health report should not distract first-run setup with a latest-issue action button.");
    AssertEqual("启用开机启动", setupBlockedReport.Checks[4].ActionLabel, "常驻模式 should still explain how to enable startup later.");
    AssertEqual("1", setupBlockedReport.NextActions[0].StepNumber, "Action queue should number the first guided action.");
    AssertEqual("添加 OpenAI 兼容密钥", setupBlockedReport.NextActions[0].Title, "Action queue should start with the first missing required credential.");
    AssertEqual("查看 API 密钥", setupBlockedReport.NextActions[0].ActionLabel, "Action queue should surface the matching one-click remediation.");
    AssertEqual("补上 NapCat Token", setupBlockedReport.NextActions[1].Title, "Action queue should keep the second blocking setup item visible.");
    AssertContains(setupBlockedReport.ActionSummary, "当前重点：添加 OpenAI 兼容密钥", "Action summary should compress first-run setup into one focus sentence.");

    var readyToStartReport = DesktopHealthReportBuilder.Build(
        new BotConfig
        {
            OpenAiApiKey = "test-key",
            NapCatWsUrl = "ws://127.0.0.1:3001",
            NapCatToken = "napcat-test-token",
            WechatBridgeUrl = string.Empty
        },
        new BackendRuntimeSnapshotViewState
        {
            ControlApiReachable = true,
            RuntimeActive = false,
            RuntimeReady = false,
            WechatConfigured = false,
            WechatRuntimeActive = false,
            WechatRuntimeReady = false,
            WechatBridgeConnected = false
        },
        new BackendControlApiFailure(),
        isBackendRootValid: true,
        hasUnsavedChanges: false,
        autoStartEnabled: false);

    AssertEqual(DesktopHealthState.Warning, readyToStartReport.State, "Health report should use a warning state when config is ready but the backend is stopped.");
    AssertEqual("可启动", readyToStartReport.StateText, "Health report should expose ready-to-start state text.");
    AssertContains(readyToStartReport.PrimaryAction, "启动后端", "Health report should point the user to the next runtime action.");
    AssertEqual("启动后端", readyToStartReport.PrimaryActionLabel, "Ready-to-start report should expose a start action label.");
    AssertEqual(DesktopHealthActionKeys.StartBackend, readyToStartReport.PrimaryActionKey, "Ready-to-start report should expose a start action key.");
    AssertContains(readyToStartReport.ChecklistStatus, "必填设置已完成", "Health report should separate completed setup from runtime start state.");
    AssertContains(readyToStartReport.ReadyNowText, "QQ 已具备启动条件", "Health report should tell the user that QQ can be started now.");
    AssertContains(readyToStartReport.RuntimeExplanation, "QQ worker 已停止", "Health report explanation should make the stopped runtime easy to understand.");
    AssertContains(readyToStartReport.Checks[3].Detail, "QQ 可独立运行", "Health report should explain that WeChat is optional when it is disabled.");
    AssertEqual(string.Empty, readyToStartReport.LatestIssueActionLabel, "Health report should hide latest-issue actions when no runtime failures have been captured.");
    AssertEqual("启用开机启动", readyToStartReport.Checks[4].ActionLabel, "常驻模式 should expose a one-click startup action when auto-start is off.");
    AssertEqual(DesktopHealthActionKeys.ToggleAutoStart, readyToStartReport.Checks[4].ActionKey, "常驻模式 should route to the auto-start toggle action.");
    AssertContains(readyToStartReport.Checks[4].Detail, "退出控制台", "常驻模式 should explain that closing desktop is separate from stopping backend.");
    AssertEqual("启动后端", readyToStartReport.NextActions[0].Title, "Action queue should point to backend start once setup is complete.");
    AssertEqual("启动后端", readyToStartReport.NextActions[0].ActionLabel, "Action queue should expose the direct backend start action.");
    AssertEqual("稍后启用开机启动（可选）", readyToStartReport.NextActions[1].Title, "Action queue should keep resident-mode setup visible as a lower-priority follow-up.");

    var qqReadyWechatWaitingReport = DesktopHealthReportBuilder.Build(
        new BotConfig
        {
            OpenAiApiKey = "test-key",
            NapCatWsUrl = "ws://127.0.0.1:3001",
            NapCatToken = "napcat-test-token",
            WechatBridgeUrl = "ws://127.0.0.1:3198",
            WechatBridgeToken = "wechat-test-token"
        },
        new BackendRuntimeSnapshotViewState
        {
            ControlApiReachable = true,
            RuntimeActive = true,
            RuntimeReady = true,
            WechatConfigured = true,
            WechatRuntimeActive = true,
            WechatRuntimeReady = false,
            WechatBridgeConnected = false
        },
        new BackendControlApiFailure(),
        isBackendRootValid: true,
        hasUnsavedChanges: false,
        autoStartEnabled: false);

    AssertContains(qqReadyWechatWaitingReport.ChecklistStatus, "1 个 runtime 警告", "Health report should count optional runtime follow-up items separately once setup is complete.");
    AssertContains(qqReadyWechatWaitingReport.ReadyNowText, "QQ 已可日常使用", "Health report should tell the user QQ can still be used when only WeChat is waiting.");
    AssertEqual("检查微信桥接", qqReadyWechatWaitingReport.NextActions[0].Title, "Action queue should surface WeChat follow-up only after QQ is already ready.");
    AssertEqual("查看微信配置", qqReadyWechatWaitingReport.NextActions[0].ActionLabel, "WeChat follow-up should stay one click away.");

    var unauthorizedIssueReport = DesktopHealthReportBuilder.Build(
        new BotConfig
        {
            OpenAiApiKey = "test-key",
            NapCatWsUrl = "ws://127.0.0.1:3001",
            NapCatToken = "napcat-test-token"
        },
        new BackendRuntimeSnapshotViewState
        {
            ControlApiReachable = false
        },
        new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unauthorized,
            Message = "Control API authentication failed."
        },
        isBackendRootValid: true,
        hasUnsavedChanges: false,
        autoStartEnabled: false);

    AssertEqual("查看本机令牌", unauthorizedIssueReport.LatestIssueActionLabel, "Unauthorized latest issue should expose a local-token fix action.");
    AssertEqual(DesktopHealthActionKeys.FocusControlApiToken, unauthorizedIssueReport.LatestIssueActionKey, "Unauthorized latest issue should route to the local token field.");
    AssertEqual("同步 desktop control token", unauthorizedIssueReport.NextActions[0].Title, "Action queue should front-load local-token recovery when desktop is locked out.");

    var runtimeFailureIssueReport = DesktopHealthReportBuilder.Build(
        new BotConfig
        {
            OpenAiApiKey = "test-key",
            NapCatWsUrl = "ws://127.0.0.1:3001",
            NapCatToken = "napcat-test-token"
        },
        new BackendRuntimeSnapshotViewState
        {
            ControlApiReachable = true,
            RuntimeActive = true,
            RuntimeReady = true,
            LastWechatLlmFailure = new BackendLlmFailureStatus
            {
                Error = "provider rejected request",
                CapturedAt = "2026-03-24T00:00:03.000Z"
            }
        },
        new BackendControlApiFailure(),
        isBackendRootValid: true,
        hasUnsavedChanges: false,
        autoStartEnabled: false);

    AssertEqual("查看微信失败", runtimeFailureIssueReport.LatestIssueActionLabel, "Latest runtime failure should expose the failing channel activity action.");
    AssertEqual(DesktopHealthActionKeys.FocusWechatFailure, runtimeFailureIssueReport.LatestIssueActionKey, "Latest runtime failure should route to the failing WeChat activity.");
    AssertEqual("查看最新问题", runtimeFailureIssueReport.NextActions[0].Title, "Action queue should surface runtime failures once setup and startup blockers are gone.");
    AssertEqual("查看微信失败", runtimeFailureIssueReport.NextActions[0].ActionLabel, "Runtime failure action should stay one click away from the action queue.");

    var startupEnabledReport = DesktopHealthReportBuilder.Build(
        new BotConfig
        {
            OpenAiApiKey = "test-key",
            NapCatWsUrl = "ws://127.0.0.1:3001",
            NapCatToken = "napcat-test-token"
        },
        new BackendRuntimeSnapshotViewState
        {
            ControlApiReachable = true,
            RuntimeActive = true,
            RuntimeReady = true
        },
        new BackendControlApiFailure(),
        isBackendRootValid: true,
        hasUnsavedChanges: false,
        autoStartEnabled: true);

    AssertEqual("随 Windows 启动", startupEnabledReport.Checks[4].StateText, "常驻模式 should confirm when startup is enabled.");
    AssertEqual(string.Empty, startupEnabledReport.Checks[4].ActionLabel, "常驻模式 should not prompt for startup when it is already enabled.");
    AssertContains(startupEnabledReport.Checks[4].Detail, "使用“停止后端”", "常驻模式 should explain how to fully stop the runtime when startup is enabled.");
    AssertEqual(0, startupEnabledReport.NextActions.Count, "Action queue should disappear when nothing urgent is left to do.");
    AssertContains(startupEnabledReport.ActionSummary, "没有阻塞这个 runtime 的紧急问题", "Action summary should explicitly say when the runtime is calm.");

    return Task.CompletedTask;
}

Task TestBackendRecentActivityProjectorAsync()
{
    var existingItem = new BackendRecentActivityItem
    {
        EventKey = "existing",
        CapturedAt = "2026-03-24T00:00:01.000Z",
        EventType = "Request",
        Summary = "existing item",
        Meta = "2026-03-24 08:00:01",
        Detail = "existing detail"
    };
    var request = new BackendLlmRequestStatus
    {
        Route = "advanced",
        ConfiguredModel = "gpt-5.4",
        Model = "deepseek-chat",
        EffectiveApiStyle = "chat_completions",
        CapturedAt = "2026-03-24T00:00:04.000Z",
        ResponseId = "resp-1"
    };
    var failure = new BackendLlmFailureStatus
    {
        Route = "advanced",
        Error = "provider rejected request",
        CapturedAt = "2026-03-24T00:00:05.000Z"
    };

    var pinnedResult = BackendRecentActivityProjector.Project(
        request,
        failure,
        [existingItem],
        lastRequestEventKey: string.Empty,
        lastFailureEventKey: string.Empty,
        isPinned: true,
        selectedItem: existingItem);

    AssertEqual(3, pinnedResult.Items.Count, "Recent activity projector should append new request and failure events.");
    AssertEqual("Failure", pinnedResult.Items[0].EventType, "Newest failure should be inserted first.");
    AssertEqual("Request", pinnedResult.Items[1].EventType, "New request should remain ahead of older history.");
    AssertContains(pinnedResult.Items[1].Summary, "configured gpt-5.4", "Request summaries should retain configured-vs-actual model context.");
    AssertEqual(existingItem, pinnedResult.SelectedItem, "Pinned selection should stay on the existing item.");

    var unpinnedReplayResult = BackendRecentActivityProjector.Project(
        request,
        failure,
        pinnedResult.Items,
        pinnedResult.LastRequestEventKey,
        pinnedResult.LastFailureEventKey,
        isPinned: false,
        selectedItem: existingItem);

    AssertEqual(3, unpinnedReplayResult.Items.Count, "Replayed request/failure events should not be duplicated.");
    AssertEqual(existingItem, unpinnedReplayResult.SelectedItem, "Without new events, replay should not disturb the current selection.");
    return Task.CompletedTask;
}

Task TestBackendRecentActivityViewStateHelperAsync()
{
    var requestItem = new BackendRecentActivityItem
    {
        EventKey = "request-1",
        EventType = "Request",
        Summary = "request"
    };
    var failureItem = new BackendRecentActivityItem
    {
        EventKey = "failure-1",
        EventType = "Failure",
        Summary = "failure",
        IsFailure = true
    };
    var items = new[] { requestItem, failureItem };

    AssertTrue(
        BackendRecentActivityViewStateHelper.ShouldInclude(requestItem, failuresOnly: false),
        "View-state helper should include requests when failures-only is disabled.");
    AssertFalse(
        BackendRecentActivityViewStateHelper.ShouldInclude(requestItem, failuresOnly: true),
        "View-state helper should hide requests when failures-only is enabled.");
    AssertTrue(
        BackendRecentActivityViewStateHelper.ShouldInclude(failureItem, failuresOnly: true),
        "View-state helper should include failures when failures-only is enabled.");

    AssertEqual(
        requestItem,
        BackendRecentActivityViewStateHelper.ResolveVisibleSelection(items, requestItem, failuresOnly: false),
        "Visible selected item should be retained.");
    AssertEqual(
        failureItem,
        BackendRecentActivityViewStateHelper.ResolveVisibleSelection(items, requestItem, failuresOnly: true),
        "When the selected item becomes hidden, selection should fall back to the first visible item.");
    AssertEqual(
        null,
        BackendRecentActivityViewStateHelper.ResolveVisibleSelection([requestItem], requestItem, failuresOnly: true),
        "When no items remain visible, selection should clear.");
    return Task.CompletedTask;
}

Task TestBackendRecentActivityCoordinatorAsync()
{
    var request = new BackendLlmRequestStatus
    {
        Route = "advanced",
        Model = "gpt-5.4",
        EffectiveApiStyle = "responses",
        CapturedAt = "2026-03-24T00:00:04.000Z",
        ResponseId = "resp-1"
    };
    var failure = new BackendLlmFailureStatus
    {
        Route = "advanced",
        Error = "provider rejected request",
        CapturedAt = "2026-03-24T00:00:05.000Z"
    };

    var runtimeProjection = BackendRecentActivityCoordinator.ProjectRuntimeUpdate(
        request,
        failure,
        existingItems: [],
        lastRequestEventKey: string.Empty,
        lastFailureEventKey: string.Empty,
        isPinned: false,
        selectedItem: null);

    AssertEqual(2, runtimeProjection.Items.Count, "Coordinator should surface projected runtime activity items.");
    AssertEqual("Failure", runtimeProjection.Items[0].EventType, "Coordinator should preserve newest-first ordering from the projector.");
    AssertEqual("Request", runtimeProjection.Items[1].EventType, "Coordinator should retain the request event after the failure.");

    var policy = new DesktopActivityStatePolicy();
    var restoredSelection = BackendRecentActivityCoordinator.ResolveSelectionAfterRestore(
        policy,
        runtimeProjection.Items,
        runtimeProjection.Items[1].EventKey,
        failuresOnly: true);

    AssertEqual(
        runtimeProjection.Items[0].EventKey,
        restoredSelection?.EventKey ?? string.Empty,
        "Coordinator should re-resolve restored selection through current filter visibility.");

    var visibleSelection = BackendRecentActivityCoordinator.ResolveSelectionAfterFilterChange(
        runtimeProjection.Items,
        runtimeProjection.Items[1],
        failuresOnly: true);

    AssertEqual(
        runtimeProjection.Items[0].EventKey,
        visibleSelection?.EventKey ?? string.Empty,
        "Coordinator should fall back to the first visible item after a filter change.");
    return Task.CompletedTask;
}

Task TestBackendRuntimeSnapshotCoordinatorAsync()
{
    var status = new BackendRuntimeStatus
    {
        RuntimeActive = true,
        RuntimeReady = true,
        WorkerProcessId = 321,
        WechatWorkerProcessId = 654,
        WechatConfigured = true,
        WechatRuntimeActive = true,
        WechatRuntimeReady = false,
        WechatBridgeConnected = true,
        LastQqLlmRequest = new BackendLlmRequestStatus
        {
            Route = "advanced",
            Model = "gpt-5.4",
            EffectiveApiStyle = "responses",
            CapturedAt = "2026-03-24T00:00:04.000Z",
            ResponseId = "resp-1"
        },
        LastWechatLlmFailure = new BackendLlmFailureStatus
        {
            Route = "advanced",
            Error = "provider rejected request",
            CapturedAt = "2026-03-24T00:00:05.000Z"
        }
    };

    var runtimeProjection = BackendRuntimeSnapshotCoordinator.ProjectRuntimeStatus(
        status,
        controlApiReachable: true,
        new BackendChannelActivityContext([], string.Empty, string.Empty, false, null),
        new BackendChannelActivityContext([], string.Empty, string.Empty, false, null));

    AssertTrue(runtimeProjection.SnapshotState.RuntimeActive == true, "Runtime snapshot coordinator should project runtime-active state.");
    AssertEqual(321, runtimeProjection.SnapshotState.WorkerProcessId ?? 0, "Runtime snapshot coordinator should project QQ worker PID.");
    AssertEqual(654, runtimeProjection.SnapshotState.WechatWorkerProcessId ?? 0, "Runtime snapshot coordinator should project Wechat worker PID.");
    AssertEqual(1, runtimeProjection.QqActivity.Items.Count, "Runtime snapshot coordinator should project QQ activity updates.");
    AssertEqual(1, runtimeProjection.WechatActivity.Items.Count, "Runtime snapshot coordinator should project Wechat activity updates.");
    AssertEqual("Request", runtimeProjection.QqActivity.Items[0].EventType, "QQ activity projection should retain request event type.");
    AssertEqual("Failure", runtimeProjection.WechatActivity.Items[0].EventType, "Wechat activity projection should retain failure event type.");
    AssertTrue(runtimeProjection.SnapshotState.ControlApiReachable == true, "Runtime snapshot coordinator should retain control API reachability.");

    var restoreProjection = BackendRuntimeSnapshotCoordinator.ProjectActivityRestore(
        new DesktopActivityStatePolicy(),
        new DesktopActivityState
        {
            QqRecentActivities =
            [
                new BackendRecentActivityItem
                {
                    EventKey = "request-1",
                    EventType = "Request",
                    Summary = "request"
                },
                new BackendRecentActivityItem
                {
                    EventKey = "failure-1",
                    EventType = "Failure",
                    Summary = "failure",
                    IsFailure = true
                }
            ],
            SelectedQqEventKey = "request-1",
            ShowOnlyQqFailures = true
        });

    AssertTrue(restoreProjection.ShowOnlyQqFailures, "Activity restore projection should retain failure-only filter state.");
    AssertEqual("failure-1", restoreProjection.SelectedQqRecentActivity?.EventKey ?? string.Empty, "Activity restore projection should resolve selection through current filter visibility.");
    return Task.CompletedTask;
}

Task TestBackendControlApiStatusPollCoordinatorAsync()
{
    var previousSnapshot = new BackendRuntimeSnapshotViewState
    {
        ControlApiReachable = true,
        RuntimeActive = true,
        WorkerProcessId = 100,
        WechatWorkerProcessId = 200
    };

    var unreachableOutcome = BackendControlApiStatusPollCoordinator.Evaluate(
        previousSnapshot,
        new BackendControlApiPollState(),
        status: null,
        new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unreachable,
            Message = "Control API is unreachable."
        },
        recoveryAttemptThreshold: 2,
        outageNotificationThreshold: 3);

    AssertFalse(unreachableOutcome.ShouldApplyRuntimeStatus, "Unreachable status failure should not apply runtime status.");
    AssertFalse(unreachableOutcome.ShouldAttemptRecovery, "First unreachable failure should not trigger recovery.");
    AssertFalse(unreachableOutcome.NextRuntimeSnapshot.ControlApiReachable == true, "Unreachable status failure should mark control API as unreachable.");
    AssertEqual(1, unreachableOutcome.NextPollState.ConsecutiveFailures, "Unreachable status failure should increment consecutive failures.");

    var unauthorizedOutcome = BackendControlApiStatusPollCoordinator.Evaluate(
        previousSnapshot,
        new BackendControlApiPollState(),
        status: null,
        new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unauthorized,
            Message = "Control API authentication failed."
        },
        recoveryAttemptThreshold: 2,
        outageNotificationThreshold: 3);

    AssertFalse(unauthorizedOutcome.ShouldApplyRuntimeStatus, "Unauthorized status failure should not apply runtime status.");
    AssertEqual(true, unauthorizedOutcome.NextRuntimeSnapshot.ControlApiReachable, "Unauthorized status failure should still treat the control API as reachable.");
    AssertTrue(unauthorizedOutcome.NextPollState.UnauthorizedNotified, "Unauthorized status failure should set the unauthorized notification flag.");
    AssertTrue(unauthorizedOutcome.LogMessages.Any(static message => message.Contains("authentication failed", StringComparison.Ordinal)), "Unauthorized status failure should emit the authentication guidance log.");

    var successOutcome = BackendControlApiStatusPollCoordinator.Evaluate(
        new BackendRuntimeSnapshotViewState
        {
            ControlApiReachable = false,
            RuntimeActive = true,
            WorkerProcessId = 100,
            WechatWorkerProcessId = 200
        },
        new BackendControlApiPollState
        {
            ConsecutiveFailures = 3,
            OutageNotified = true
        },
        new BackendRuntimeStatus
        {
            RuntimeActive = false,
            WorkerProcessId = 101,
            WechatWorkerProcessId = 201
        },
        new BackendControlApiFailure(),
        recoveryAttemptThreshold: 2,
        outageNotificationThreshold: 3);

    AssertTrue(successOutcome.ShouldApplyRuntimeStatus, "Successful status poll should apply runtime status.");
    AssertEqual(0, successOutcome.NextPollState.ConsecutiveFailures, "Successful status poll should reset control API failure counters.");
    AssertTrue(successOutcome.LogMessages.Any(static message => message.Contains("became reachable again", StringComparison.Ordinal)), "Successful status poll should log control API recovery.");
    AssertTrue(successOutcome.LogMessages.Any(static message => message.Contains("Worker restarted", StringComparison.Ordinal)), "Successful status poll should log QQ worker restart.");
    AssertTrue(successOutcome.LogMessages.Any(static message => message.Contains("Wechat worker restarted", StringComparison.Ordinal)), "Successful status poll should log Wechat worker restart.");
    AssertTrue(successOutcome.LogMessages.Any(static message => message.Contains("Runtime became inactive", StringComparison.Ordinal)), "Successful status poll should log runtime-stop transition.");
    AssertEqual(4, successOutcome.Notifications.Count, "Successful status poll should emit recovery and restart/runtime notifications.");
    return Task.CompletedTask;
}

async Task TestBackendControlApiStatusWaiterAsync()
{
    var callCount = 0;
    var lastFailure = new BackendControlApiFailure
    {
        Kind = BackendControlApiFailureKind.Unreachable,
        Message = "Control API is unreachable."
    };

    var recoveredStatus = await BackendControlApiStatusWaiter.WaitForStatusAsync(
        tryGetStatusAsync: (_cancellationToken) =>
        {
            callCount += 1;
            return Task.FromResult(callCount >= 3
                ? new BackendRuntimeStatus
                {
                    RuntimeActive = true
                }
                : null as BackendRuntimeStatus);
        },
        getLastFailure: () => lastFailure,
        maxAttempts: 5,
        retryDelay: TimeSpan.Zero,
        delayAsync: static (_delay, _token) => Task.CompletedTask);

    AssertTrue(recoveredStatus?.RuntimeActive == true, "Status waiter should return the first recovered runtime status.");
    AssertEqual(3, callCount, "Status waiter should retry until the control API becomes reachable.");

    var immediateFailureWaitCallCount = 0;
    var immediateFailure = await BackendControlApiStatusWaiter.WaitForStatusAsync(
        tryGetStatusAsync: (_cancellationToken) =>
        {
            immediateFailureWaitCallCount += 1;
            return Task.FromResult<BackendRuntimeStatus?>(null);
        },
        getLastFailure: () => new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unauthorized,
            Message = "Control API authentication failed."
        },
        maxAttempts: 5,
        retryDelay: TimeSpan.Zero,
        delayAsync: static (_delay, _token) => Task.CompletedTask);

    AssertEqual(null, immediateFailure, "Status waiter should stop immediately on non-unreachable failures.");
    AssertEqual(1, immediateFailureWaitCallCount, "Status waiter should not keep retrying after an immediate failure.");
}

async Task TestBackendControlApiRecoveryCoordinatorAsync()
{
    var existingStatus = new BackendRuntimeStatus
    {
        ControlApiUrl = "http://127.0.0.1:3199"
    };
    var recoveredOutcome = await BackendControlApiRecoveryCoordinator.TryRecoverAsync(
        "status-poll",
        prepareAsync: () => Task.CompletedTask,
        tryGetStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(existingStatus),
        getLastFailure: () => new BackendControlApiFailure(),
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        isProcessRunning: static () => false,
        startProcess: static () => throw new InvalidOperationException("Should not start process when status is already reachable."),
        tryStartAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        waitForStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        detachProcess: static () => { });

    AssertEqual(existingStatus, recoveredOutcome.RecoveredStatus, "Recovery coordinator should return the already-recovered status.");
    AssertTrue(recoveredOutcome.LogMessages.Any(static message => message.Contains("recovered before local restart", StringComparison.Ordinal)), "Recovery coordinator should log when recovery happens before local restart.");

    var startedStatus = new BackendRuntimeStatus
    {
        ControlApiUrl = "http://127.0.0.1:3200"
    };
    var startCallCount = 0;
    var detachCallCount = 0;
    var startedOutcome = await BackendControlApiRecoveryCoordinator.TryRecoverAsync(
        "save-config",
        prepareAsync: () => Task.CompletedTask,
        tryGetStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        getLastFailure: () => new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unreachable,
            Message = "Control API is unreachable."
        },
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        isProcessRunning: static () => false,
        startProcess: () => startCallCount += 1,
        tryStartAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(startedStatus),
        waitForStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        detachProcess: () => detachCallCount += 1);

    AssertEqual(startedStatus, startedOutcome.RecoveredStatus, "Recovery coordinator should return the started status when /start succeeds.");
    AssertEqual(1, startCallCount, "Recovery coordinator should start the local backend when it is not already running.");
    AssertEqual(1, detachCallCount, "Recovery coordinator should detach the local backend launcher after successful recovery.");
    AssertTrue(startedOutcome.LogMessages.Any(static message => message.Contains("Started local backend host", StringComparison.Ordinal)), "Recovery coordinator should log local backend startup.");
    AssertTrue(startedOutcome.LogMessages.Any(static message => message.Contains("recovery succeeded", StringComparison.Ordinal)), "Recovery coordinator should log recovery success.");

    var abortedOutcome = await BackendControlApiRecoveryCoordinator.TryRecoverAsync(
        "load-config",
        prepareAsync: () => Task.CompletedTask,
        tryGetStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        getLastFailure: () => new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unauthorized,
            Message = "Control API authentication failed."
        },
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        isProcessRunning: static () => true,
        startProcess: static () => throw new InvalidOperationException("Should not start process for immediate failures."),
        tryStartAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        waitForStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        detachProcess: static () => { });

    AssertEqual(null, abortedOutcome.RecoveredStatus, "Recovery coordinator should abort on immediate failures.");
    AssertTrue(abortedOutcome.LogMessages.Any(static message => message.Contains("recovery aborted", StringComparison.Ordinal)), "Recovery coordinator should log recovery aborts.");
}

async Task TestBackendControlPlaneCoordinatorAsync()
{
    var recoveredConfig = new BackendControlConfigResponse
    {
        WechatBotPrefix = "/ai"
    };
    var recoveredStatus = new BackendRuntimeStatus
    {
        RuntimeActive = true
    };
    var configCallCount = 0;
    var statusCallCount = 0;
    var recoverCallCount = 0;
    BackendControlApiFailure lastFailure = new()
    {
        Kind = BackendControlApiFailureKind.Unreachable,
        Message = "Control API is unreachable."
    };

    var loadResult = await BackendControlPlaneCoordinator.LoadAuthoritativeConfigAsync(
        tryGetConfigAsync: (_cancellationToken) =>
        {
            configCallCount += 1;
            return Task.FromResult(configCallCount >= 2 ? recoveredConfig : null);
        },
        getLastFailure: () => lastFailure,
        tryGetStatusAsync: (_cancellationToken) =>
        {
            statusCallCount += 1;
            return Task.FromResult(statusCallCount >= 1 ? recoveredStatus : null);
        },
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        tryRecoverControlApiAsync: () =>
        {
            recoverCallCount += 1;
            lastFailure = new BackendControlApiFailure();
            return Task.CompletedTask;
        });

    AssertEqual(recoveredConfig, loadResult.ApiConfig, "Control-plane coordinator should retry config load after recovery.");
    AssertEqual(recoveredStatus, loadResult.ApiStatus, "Control-plane coordinator should return the recovered status.");
    AssertEqual(1, recoverCallCount, "Control-plane coordinator should trigger recovery exactly once for unreachable config loads.");

    var saveCallCount = 0;
    var saveRecoverCallCount = 0;
    lastFailure = new BackendControlApiFailure
    {
        Kind = BackendControlApiFailureKind.Unreachable,
        Message = "Control API is unreachable."
    };

    var saveResult = await BackendControlPlaneCoordinator.SaveThroughControlApiAsync(
        new BotConfig
        {
            WechatBotPrefix = "/wx"
        },
        trySaveConfigAsync: (config, _cancellationToken) =>
        {
            saveCallCount += 1;
            return Task.FromResult(saveCallCount >= 2
                ? new BackendControlConfigResponse
                {
                    WechatBotPrefix = config.WechatBotPrefix
                }
                : null);
        },
        getLastFailure: () => lastFailure,
        tryGetStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        tryRecoverControlApiAsync: () =>
        {
            saveRecoverCallCount += 1;
            lastFailure = new BackendControlApiFailure();
            return Task.CompletedTask;
        });

    AssertEqual("/wx", saveResult.WechatBotPrefix, "Control-plane coordinator should retry save after recovery.");
    AssertEqual(1, saveRecoverCallCount, "Control-plane coordinator should trigger recovery exactly once for unreachable saves.");
}

async Task TestBackendRuntimeControlCoordinatorAsync()
{
    var attachStatus = new BackendRuntimeStatus
    {
        RuntimeActive = true,
        ControlApiUrl = "http://127.0.0.1:3199"
    };

    var startAttachOutcome = await BackendRuntimeControlCoordinator.StartAsync(
        tryGetStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(attachStatus),
        getLastFailure: () => new BackendControlApiFailure(),
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        startProcess: static () => throw new InvalidOperationException("Should not start local backend when control API is already reachable."),
        waitForStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        tryStartAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null));

    AssertEqual(attachStatus, startAttachOutcome.AppliedStatus, "Runtime control coordinator should attach to the existing backend host.");
    AssertTrue(startAttachOutcome.ShouldDetachProcess, "Attach flow should detach the local launcher.");
    AssertTrue(startAttachOutcome.ShouldReloadConfig, "Attach flow should request a config reload.");

    BackendControlApiFailure lastFailure = new()
    {
        Kind = BackendControlApiFailureKind.Unreachable,
        Message = "Control API is unreachable."
    };
    var startProcessCallCount = 0;
    var waitOutcome = await BackendRuntimeControlCoordinator.StartAsync(
        tryGetStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        getLastFailure: () => lastFailure,
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        startProcess: () => startProcessCallCount += 1,
        waitForStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        tryStartAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null));

    AssertEqual(1, startProcessCallCount, "Runtime control coordinator should start the local backend when control API is unreachable.");
    AssertEqual("Backend started, waiting for control API", waitOutcome.StatusText, "Runtime control coordinator should expose waiting status when control API does not come up yet.");
    AssertFalse(waitOutcome.ControlApiReachable, "Waiting start outcome should mark control API as not yet reachable.");

    var stopWithApiStatus = new BackendRuntimeStatus
    {
        RuntimeActive = false,
        ControlApiUrl = "http://127.0.0.1:3199"
    };
    var stopApiOutcome = await BackendRuntimeControlCoordinator.StopAsync(
        tryStopAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(stopWithApiStatus),
        getLastFailure: () => new BackendControlApiFailure(),
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        isProcessRunning: static () => true,
        stopProcessAsync: () => Task.CompletedTask);

    AssertEqual(stopWithApiStatus, stopApiOutcome.AppliedStatus, "Runtime control coordinator should apply stop status returned by the control API.");
    AssertEqual("Backend stopped", stopApiOutcome.StatusText, "Runtime control coordinator should report stopped status from control API response.");
    AssertEqual(1, stopApiOutcome.Notifications.Count, "Control API stop should emit one tray notification.");

    var localStopCallCount = 0;
    var stopLocalOutcome = await BackendRuntimeControlCoordinator.StopAsync(
        tryStopAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        getLastFailure: () => new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unreachable,
            Message = "Control API is unreachable."
        },
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        isProcessRunning: static () => true,
        stopProcessAsync: () =>
        {
            localStopCallCount += 1;
            return Task.CompletedTask;
        });

    AssertEqual(1, localStopCallCount, "Runtime control coordinator should stop the local backend when control API is unreachable but the launcher still owns the process.");
    AssertEqual("Backend host stopped", stopLocalOutcome.StatusText, "Runtime control coordinator should report local backend shutdown.");

    var unreachableStopOutcome = await BackendRuntimeControlCoordinator.StopAsync(
        tryStopAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        getLastFailure: () => new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unreachable,
            Message = "Control API is unreachable."
        },
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        isProcessRunning: static () => false,
        stopProcessAsync: () => Task.CompletedTask);

    AssertEqual("Backend is not reachable", unreachableStopOutcome.StatusText, "Runtime control coordinator should surface unreachable-stop state when nothing is running locally.");
    AssertTrue(unreachableStopOutcome.LogMessages.Any(static message => message.Contains("no local backend host process is attached", StringComparison.Ordinal)), "Runtime control coordinator should log missing local backend ownership.");
}

async Task TestBackendControlPlaneFacadeAsync()
{
    var recoverCallCount = 0;
    BackendControlApiFailure lastFailure = new()
    {
        Kind = BackendControlApiFailureKind.Unreachable,
        Message = "Control API is unreachable."
    };
    var configCallCount = 0;
    var statusCallCount = 0;

    var loadResult = await BackendControlPlaneFacade.LoadAuthoritativeConfigAsync(
        tryGetConfigAsync: (_cancellationToken) =>
        {
            configCallCount += 1;
            return Task.FromResult(configCallCount >= 2
                ? new BackendControlConfigResponse
                {
                    WechatBotPrefix = "/ai"
                }
                : null as BackendControlConfigResponse);
        },
        getLastFailure: () => lastFailure,
        tryGetStatusAsync: (_cancellationToken) =>
        {
            statusCallCount += 1;
            return Task.FromResult(statusCallCount >= 2
                ? new BackendRuntimeStatus
                {
                    RuntimeActive = true
                }
                : null as BackendRuntimeStatus);
        },
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        tryRecoverControlApiAsync: () =>
        {
            recoverCallCount += 1;
            lastFailure = new BackendControlApiFailure();
            return Task.CompletedTask;
        });

    AssertEqual("/ai", loadResult.ApiConfig?.WechatBotPrefix ?? string.Empty, "Facade load should flow through control-plane load coordination.");
    AssertEqual(1, recoverCallCount, "Facade load should invoke recovery when needed.");

    var savePrepareCallCount = 0;
    lastFailure = new BackendControlApiFailure();
    var saveResult = await BackendControlPlaneFacade.SaveConfigAsync(
        prepareAsync: () =>
        {
            savePrepareCallCount += 1;
            return Task.CompletedTask;
        },
        config: new BotConfig
        {
            WechatBotPrefix = "/wx"
        },
        trySaveConfigAsync: (config, _cancellationToken) => Task.FromResult<BackendControlConfigResponse?>(
            new BackendControlConfigResponse
            {
                WechatBotPrefix = config.WechatBotPrefix
            }),
        getLastFailure: () => lastFailure,
        tryGetStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        tryRecoverControlApiAsync: () => Task.CompletedTask);

    AssertEqual(1, savePrepareCallCount, "Facade save should run preflight prepare step.");
    AssertEqual("/wx", saveResult.WechatBotPrefix, "Facade save should flow through control-plane save coordination.");

    var startPrepareCallCount = 0;
    var startProcessCallCount = 0;
    var startOutcome = await BackendControlPlaneFacade.StartBackendAsync(
        prepareAsync: () =>
        {
            startPrepareCallCount += 1;
            return Task.CompletedTask;
        },
        tryGetStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        getLastFailure: () => new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unreachable,
            Message = "Control API is unreachable."
        },
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        startProcess: () => startProcessCallCount += 1,
        waitForStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        tryStartAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null));

    AssertEqual(1, startPrepareCallCount, "Facade start should run preflight prepare step.");
    AssertEqual(1, startProcessCallCount, "Facade start should pass through runtime start coordination.");
    AssertEqual("Backend started, waiting for control API", startOutcome.StatusText, "Facade start should preserve waiting-for-control-api outcome.");

    var stopPrepareCallCount = 0;
    var stopLocalCallCount = 0;
    var stopOutcome = await BackendControlPlaneFacade.StopBackendAsync(
        prepareAsync: () =>
        {
            stopPrepareCallCount += 1;
            return Task.CompletedTask;
        },
        tryStopAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        getLastFailure: () => new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unreachable,
            Message = "Control API is unreachable."
        },
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        isProcessRunning: static () => true,
        stopProcessAsync: () =>
        {
            stopLocalCallCount += 1;
            return Task.CompletedTask;
        });

    AssertEqual(1, stopPrepareCallCount, "Facade stop should run preflight prepare step.");
    AssertEqual(1, stopLocalCallCount, "Facade stop should pass through runtime stop coordination.");
    AssertEqual("Backend host stopped", stopOutcome.StatusText, "Facade stop should preserve local-stop outcome.");
}

async Task TestDesktopControlPlaneSessionRestoreTokenGuidanceAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-session-restore-token-");
    context.FakeBackend.StatusFailureKind = BackendControlApiFailureKind.Unauthorized;
    context.FakeBackend.StatusFailureMessage = "Control API authentication failed.";
    context.FakeLocalStateSnapshotService.PreviewResult = context.FakeLocalStateSnapshotService.PreviewResult with
    {
        Lines =
        [
            ".env锛氫笌褰撳墠鐘舵€佷笉鍚?",
            ".env 璺熻釜閿彉鏇达細QQ_AI_BOT_CONTROL_API_TOKEN",
            "QQ_AI_BOT_CONTROL_API_TOKEN: ********0000 -> ********1234"
        ]
    };

    var session = CreateDesktopControlPlaneSessionForTests(context);
    await session.LoadConfigAsync();
    await session.RefreshStateSnapshotsAsync();
    var selectedSnapshot = context.FakeLocalStateSnapshotService.Snapshots.Last();
    session.UpdateSelectedStateSnapshot(selectedSnapshot);

    var restoreResult = await session.RestoreSelectedStateSnapshotAsync(selectedSnapshot.ArchivePath);

    AssertTrue(restoreResult.Succeeded, "Session restore should succeed for the selected archive.");
    AssertEqual(selectedSnapshot.ArchivePath, context.FakeLocalStateSnapshotService.LastRestoreArchivePath, "Session restore should target the selected archive.");
    AssertEqual(DesktopHealthActionKeys.FocusControlApiToken, restoreResult.NextState?.SnapshotState.LastStateRestorePrimaryActionKey, "Restore guidance should prioritize the local token action.");
    AssertContains(restoreResult.NextState?.SnapshotState.LastStateRestoreControlPlaneText ?? string.Empty, "QQ_AI_BOT_CONTROL_API_TOKEN", "Restore control-plane text should identify the local token mismatch.");
    AssertFalse(string.IsNullOrWhiteSpace(restoreResult.NextState?.SnapshotState.LastStateRestoreNextStepText), "Restore next-step text should remain populated.");
}

async Task TestDesktopControlPlaneSessionRestoreNapCatGuidanceAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-session-restore-napcat-");
    context.FakeBackend.Status = new BackendRuntimeStatus
    {
        RuntimeActive = true,
        RuntimeReady = false,
        WechatConfigured = true,
        WechatRuntimeActive = true,
        WechatRuntimeReady = true,
        WechatBridgeConnected = true
    };
    context.FakeLocalStateSnapshotService.PreviewResult = context.FakeLocalStateSnapshotService.PreviewResult with
    {
        Lines =
        [
            ".env锛氫笌褰撳墠鐘舵€佷笉鍚?",
            ".env 璺熻釜閿彉鏇达細NAPCAT_WS_URL, NAPCAT_TOKEN",
            "NAPCAT_WS_URL: ws://before -> ws://after",
            "NAPCAT_TOKEN: ********5678 -> ********1234"
        ]
    };

    var session = CreateDesktopControlPlaneSessionForTests(context);
    await session.LoadConfigAsync();
    await session.RefreshStateSnapshotsAsync();
    var selectedSnapshot = context.FakeLocalStateSnapshotService.Snapshots.Last();
    session.UpdateSelectedStateSnapshot(selectedSnapshot);

    var restoreResult = await session.RestoreSelectedStateSnapshotAsync(selectedSnapshot.ArchivePath);

    AssertTrue(restoreResult.Succeeded, "Session restore should succeed for NapCat blocker scenario.");
    AssertEqual(DesktopHealthActionKeys.FocusNapCatUrl, restoreResult.NextState?.SnapshotState.LastStateRestorePrimaryActionKey, "Restore guidance should prioritize NapCat review when QQ is blocked.");
    AssertFalse(string.IsNullOrWhiteSpace(restoreResult.NextState?.SnapshotState.LastStateRestoreRuntimeText), "Restore runtime text should remain populated for QQ blocker guidance.");
    AssertFalse(string.IsNullOrWhiteSpace(restoreResult.NextState?.SnapshotState.LastStateRestoreNextStepText), "Restore next-step guidance should remain populated for QQ blocker guidance.");
}

async Task TestDesktopControlPlaneSessionSnapshotTargetingAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-session-snapshot-targeting-");
    var session = CreateDesktopControlPlaneSessionForTests(context);

    await session.LoadConfigAsync();
    await session.RefreshStateSnapshotsAsync();
    var selectedSnapshot = context.FakeLocalStateSnapshotService.Snapshots.Last();
    session.UpdateSelectedStateSnapshot(selectedSnapshot);

    await session.RefreshSelectedStateSnapshotPreviewAsync();
    AssertEqual(selectedSnapshot.ArchivePath, context.FakeLocalStateSnapshotService.LastPreviewArchivePath, "Snapshot preview should target the selected archive.");

    var restoreResult = await session.RestoreSelectedStateSnapshotAsync(selectedSnapshot.ArchivePath);
    AssertEqual(selectedSnapshot.ArchivePath, context.FakeLocalStateSnapshotService.LastRestoreArchivePath, "Snapshot restore should target the selected archive.");
    AssertEqual(selectedSnapshot.ArchivePath, restoreResult.NextState?.SnapshotState.LastStateRestoreText, "Restore state should record the selected archive path.");

    var deleteResult = await session.DeleteSelectedStateSnapshotAsync(selectedSnapshot.ArchivePath);
    AssertTrue(deleteResult.Succeeded, "Snapshot delete should succeed.");
    AssertEqual(selectedSnapshot.ArchivePath, context.FakeLocalStateSnapshotService.LastDeletedArchivePath, "Snapshot delete should target the selected archive.");
    AssertEqual(1, deleteResult.NextState?.SnapshotState.StateSnapshots.Count, "Snapshot delete should refresh the remaining archive list.");
    AssertEqual(
        context.FakeLocalStateSnapshotService.Snapshots.First().ArchivePath,
        deleteResult.NextState?.SnapshotState.SelectedStateSnapshot?.ArchivePath,
        "Snapshot delete should move selection onto the remaining archive.");
    AssertEqual("尚未恢复状态快照", deleteResult.NextState?.SnapshotState.LastStateRestoreText, "Deleting the restored archive should clear the restore-result selection state.");
}

async Task TestDesktopControlPlaneSessionSnapshotPresentationAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-session-snapshot-presentation-");
    var session = CreateDesktopControlPlaneSessionForTests(context);

    await session.LoadConfigAsync();
    await session.RefreshStateSnapshotsAsync();
    var selectedSnapshot = context.FakeLocalStateSnapshotService.Snapshots.Last();
    session.UpdateSelectedStateSnapshot(selectedSnapshot);

    var previewResult = await session.RefreshSelectedStateSnapshotPreviewAsync();
    var previewState = previewResult.NextState?.SnapshotState
        ?? throw new InvalidOperationException("Snapshot preview should return a shell state.");
    AssertContains(previewState.SelectedStateSnapshotDiffText, "OPENAI_API_KEY", "Snapshot preview should project tracked env changes into shell state.");
    AssertContains(previewState.SelectedStateSnapshotDiffText, "sessions.json", "Snapshot preview should project data-file differences into shell state.");
    AssertContains(previewState.SelectedStateSnapshotAdviceText, ".env", "Snapshot preview advice should preserve secret-handling guidance.");

    var restoreResult = await session.RestoreSelectedStateSnapshotAsync(selectedSnapshot.ArchivePath);
    var restoreState = restoreResult.NextState?.SnapshotState
        ?? throw new InvalidOperationException("Snapshot restore should return a shell state.");
    AssertContains(restoreState.LastStateRestoreSummaryText, selectedSnapshot.FileName, "Restore summary should identify the restored archive.");
    AssertContains(restoreState.LastStateRestoreTargetsText, ".env", "Restore targets should describe restored env state.");
    AssertContains(restoreState.LastStateRestoreSessionsText, "sessions.json", "Restore result should preserve preview-derived session summary.");
    AssertContains(restoreState.LastStateRestoreLatestActivityText, "2026-03-26 09:00:00 UTC", "Restore result should preserve preview-derived latest activity summary.");
    AssertContains(restoreState.LastStateRestoreAdviceText, ".env", "Restore result should keep restore advice sourced from preview recommendations.");
}

async Task TestDesktopControlPlaneSessionActivitySelectionStabilityAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-session-activity-selection-");
    var session = CreateDesktopControlPlaneSessionForTests(context);

    var initialState = (await session.LoadConfigAsync()).NextState
        ?? throw new InvalidOperationException("Loading config should return a shell state.");
    var initiallySelectedRequest = initialState.RecentActivityState.SelectedQqRecentActivity
        ?? throw new InvalidOperationException("QQ activity selection should be initialized.");

    var pinnedState = session.ApplyActivityStateSelection(
        pinSelectedQqActivity: true,
        selectedQqRecentActivity: initiallySelectedRequest,
        updateQqSelection: true);
    AssertTrue(pinnedState.RecentActivityState.PinSelectedQqActivity, "Session should persist QQ pin state.");

    context.FakeBackend.Status!.LastQqLlmRequest = new BackendLlmRequestStatus
    {
        Route = "advanced",
        Model = "gpt-5.4-mini",
        EffectiveApiStyle = "responses",
        EffectiveReasoningEffort = "high",
        EffectiveTextVerbosity = "high",
        EffectiveTools = ["web_search"],
        ExecutionKind = BackendExecutionProjectionTags.DeliberationKind,
        ExecutionSummary = BackendExecutionProjectionTags.DeliberationSummary,
        ExecutionProjection = new BackendExecutionProjection
        {
            Kind = BackendExecutionProjectionTags.DeliberationKind,
            Summary = BackendExecutionProjectionTags.DeliberationSummary,
            Stages = BackendExecutionProjectionTags.DeliberationStages,
            FailedStage = "",
            CompletedStages =
            [
                BackendExecutionProjectionTags.PlannerStage,
                BackendExecutionProjectionTags.DraftStage
            ],
            Degraded = true,
            Recoveries = [BackendExecutionProjectionTags.RewriteFallbackToDraftRecovery]
        },
        DecisionSummary = new BackendDecisionSummary
        {
            Trigger = new BackendDecisionTrigger
            {
                Kind = "directive",
                MatchedPrefix = "/vision"
            },
            ReasonTags = ["directive:/vision"],
            ReasonGroups = new BackendDecisionReasonGroups
            {
                TriggerReasons = ["directive:/vision"],
                CapabilityReasons = [],
                UpgradeReasons = []
            },
            RequestedCapabilities = new BackendRequestedCapabilities
            {
                ReasoningEffort = "high",
                TextVerbosity = "high",
                EnableWebSearch = true,
                EnableCodeInterpreter = false,
                NeedsResponsesCapabilities = true
            },
            RouteReason = "directive:/vision",
            MatchedPrefix = "/vision"
        },
        ImageCount = 1,
        CapturedAt = "2026-03-24T00:00:05.000Z",
        ResponseId = "resp-new-1"
    };

    var updatedState = session.ApplyBackendRuntimeStatus(context.FakeBackend.Status, true);
    AssertEqual(3, updatedState.RecentActivityState.QqRecentActivities.Count, "Runtime updates should append new QQ activity.");
    AssertEqual(
        initiallySelectedRequest.EventKey,
        updatedState.RecentActivityState.SelectedQqRecentActivity?.EventKey,
        "Pinned QQ selection should survive newer activity updates.");

    var filteredState = session.ApplyActivityStateSelection(showOnlyQqFailures: true);
    AssertTrue(filteredState.RecentActivityState.ShowOnlyQqFailures, "Session should persist QQ failures-only filter.");
    AssertTrue(filteredState.RecentActivityState.SelectedQqRecentActivity?.IsFailure == true, "Failure filtering should move selection onto a failure event.");
    AssertContains(filteredState.RecentActivityState.SelectedQqRecentActivity?.Summary ?? string.Empty, "search timed out", "Failure filtering should keep the latest QQ failure selected.");
}

async Task TestDesktopControlPlaneSessionSnapshotExportAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-session-snapshot-export-");
    var session = CreateDesktopControlPlaneSessionForTests(context);

    await session.LoadConfigAsync();
    await session.RefreshStateSnapshotsAsync();

    var exportResult = await session.ExportStateSnapshotAsync();
    AssertTrue(exportResult.Succeeded, "State snapshot export should succeed.");
    AssertEqual(1, context.FakeLocalStateSnapshotService.ExportCallCount, "State snapshot export should invoke the snapshot service once.");
    AssertEqual(context.RootPath, context.FakeLocalStateSnapshotService.LastBackendRootPath, "State snapshot export should use the current backend root.");
    AssertEqual(context.FakeLocalStateSnapshotService.Result.ArchivePath, exportResult.NextState?.SnapshotState.LastStateSnapshotText, "State snapshot export should update the shell state's latest snapshot text.");
    AssertEqual(
        Path.Combine(context.RootPath, "artifacts", "state-snapshots"),
        exportResult.NextState?.LocalDocumentState.StateSnapshotFolderPathText,
        "State snapshot export should keep projecting the snapshot folder path through local-document state.");

    var safeExportResult = await session.ExportSafeStateSnapshotAsync();
    AssertTrue(safeExportResult.Succeeded, "Safe state snapshot export should succeed.");
    AssertEqual(1, context.FakeLocalStateSnapshotService.ExportSafeCallCount, "Safe snapshot export should invoke the safe export service once.");
    AssertContains(safeExportResult.NextState?.SnapshotState.LastStateSnapshotText ?? string.Empty, "runtime-state-safe-test.zip", "Safe snapshot export should update the shell state's latest snapshot text.");

    var selectedSnapshot = context.FakeLocalStateSnapshotService.Snapshots.Last();
    session.UpdateSelectedStateSnapshot(selectedSnapshot);

    var rollbackExportResult = await session.ExportSafeRollbackSnapshotAsync(selectedSnapshot.ArchivePath);
    AssertTrue(rollbackExportResult.Succeeded, "Safe rollback snapshot export should succeed.");
    AssertEqual(2, context.FakeLocalStateSnapshotService.ExportSafeCallCount, "Safe rollback snapshot export should reuse the safe export service.");
    AssertContains(rollbackExportResult.NextState?.SnapshotState.LastStateSnapshotText ?? string.Empty, "runtime-state-safe-test.zip", "Safe rollback snapshot export should update the latest snapshot text.");
    AssertEqual(selectedSnapshot.ArchivePath, rollbackExportResult.NextState?.SnapshotState.SelectedStateSnapshot?.ArchivePath, "Safe rollback snapshot export should preserve the original restore target selection.");
}

async Task TestDesktopControlPlaneSessionSnapshotConfirmationAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-session-snapshot-confirmation-");
    var session = CreateDesktopControlPlaneSessionForTests(context);

    await session.LoadConfigAsync();
    await session.RefreshStateSnapshotsAsync();
    var selectedSnapshot = context.FakeLocalStateSnapshotService.Snapshots.Last();
    session.UpdateSelectedStateSnapshot(selectedSnapshot);

    var restorePrompt = await session.BuildRestoreSelectedStateSnapshotConfirmationAsync(selectedSnapshot.ArchivePath);
    AssertEqual("恢复选中快照", restorePrompt.Title, "Restore confirmation should expose the selected-snapshot title.");
    AssertContains(restorePrompt.Message, "恢复前建议", "Restore confirmation should include a dedicated pre-restore safety section.");
    AssertContains(restorePrompt.Message, "安全回滚快照", "Restore confirmation should recommend a safe rollback snapshot before overwriting current state.");
    AssertContains(restorePrompt.Message, ".env", "Restore confirmation should mention env overwrite risk.");
    AssertContains(restorePrompt.Message, "与当前状态不同", "Restore confirmation should include diff preview lines.");
    AssertContains(restorePrompt.Message, "OPENAI_API_KEY", "Restore confirmation should list tracked env keys that change.");
    AssertContains(restorePrompt.Message, "********1234", "Restore confirmation should keep secret values masked.");
    AssertContains(restorePrompt.Message, "sessions.json", "Restore confirmation should list changed data files.");
    AssertContains(restorePrompt.Message, "仅当前存在的数据文件：无", "Restore confirmation should include current-only data summary.");
    AssertContains(restorePrompt.Message, "sessions.json 会话数：当前 3 -> 快照 2", "Restore confirmation should include session count changes.");
    AssertContains(restorePrompt.Message, "sessions.json 最近活动：当前 2026-03-27 11:00:00 UTC -> 快照 2026-03-26 09:00:00 UTC", "Restore confirmation should include latest session activity timestamps.");
    AssertContains(restorePrompt.Message, "qq:group-c/user-c", "Restore confirmation should include changed conversation keys.");
    AssertContains(restorePrompt.Message, "恢复前先导出当前状态", "Restore confirmation should include restore advice.");
    AssertContains(restorePrompt.Message, "不要把归档分享", "Restore confirmation should include secret handling advice.");

    var deletePrompt = session.BuildDeleteSelectedStateSnapshotConfirmation(selectedSnapshot.ArchivePath);
    AssertEqual("删除选中快照", deletePrompt.Title, "Delete confirmation should expose the selected-snapshot title.");
    AssertContains(deletePrompt.Message, "永久移除", "Delete confirmation should explain permanence.");
}

async Task TestDesktopControlPlaneSessionLocalPathOperationsAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-session-local-paths-");
    var session = CreateDesktopControlPlaneSessionForTests(context);

    await session.LoadConfigAsync();

    var openSessionStoreResult = session.OpenSessionStoreFolder();
    AssertTrue(openSessionStoreResult.Succeeded, "Opening the session store folder should succeed.");
    AssertContains(context.FakeLocalPathOperationsService.OpenedFolders[0], "data", "Session store action should open the data folder.");

    var openImageCacheResult = session.OpenImageCacheFolder();
    AssertTrue(openImageCacheResult.Succeeded, "Opening the image cache folder should succeed.");
    AssertContains(context.FakeLocalPathOperationsService.OpenedFolders[1], "image-cache", "Image cache action should open the image cache folder.");

    var clearImageCacheResult = session.ClearImageCache();
    AssertTrue(clearImageCacheResult.Succeeded, "Clearing the image cache should succeed.");
    AssertEqual(1, context.FakeLocalPathOperationsService.ClearCallCount, "Clear image cache should invoke the path operation service.");
    AssertContains(context.FakeLocalPathOperationsService.LastClearedDirectory, "image-cache", "Clear image cache should target the image cache path.");
    AssertEqual("图片缓存为空", clearImageCacheResult.NextState?.LocalDocumentState.ImageCacheStateText, "Clearing the image cache should refresh the projected cache-state text.");

    var openSnapshotFolderResult = session.OpenStateSnapshotFolder();
    AssertTrue(openSnapshotFolderResult.Succeeded, "Opening the state snapshot folder should succeed.");
    AssertContains(context.FakeLocalPathOperationsService.OpenedFolders[^1], "state-snapshots", "Open snapshot folder should target the snapshot directory.");
}

async Task TestDesktopControlPlaneSessionLocalDocumentProjectionAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-session-local-document-");
    context.FakeLocalFallbackReader.Document.ExtraValues["QQ_AI_BOT_CONTROL_API_HOST"] = "10.8.0.5";
    context.FakeLocalFallbackReader.Document.ExtraValues["QQ_AI_BOT_CONTROL_API_PORT"] = "4319";
    Directory.CreateDirectory(Path.Combine(context.RootPath, "data", "image-cache", "nested"));
    await File.WriteAllTextAsync(Path.Combine(context.RootPath, "data", "sessions.json"), "{}", Encoding.UTF8);
    await File.WriteAllTextAsync(Path.Combine(context.RootPath, "data", "image-cache", "one.txt"), "one", Encoding.UTF8);
    await File.WriteAllTextAsync(Path.Combine(context.RootPath, "data", "image-cache", "nested", "two.txt"), "two", Encoding.UTF8);

    var session = CreateDesktopControlPlaneSessionForTests(context);

    var invalidRootPath = Path.Combine(context.RootPath, "missing-backend");
    var invalidState = session.UpdateBackendRoot(invalidRootPath, false);
    AssertFalse(invalidState.LocalDocumentState.IsBackendRootValid, "Missing backend root should project as invalid.");
    AssertEqual(Path.Combine(invalidRootPath, "data", "runtime-settings.json"), invalidState.LocalDocumentState.RuntimeConfigPath, "Invalid backend root should still project the runtime config path.");
    AssertEqual(Path.Combine(invalidRootPath, ".env"), invalidState.LocalDocumentState.BootstrapEnvPath, "Invalid backend root should still project the bootstrap env path.");
    AssertEqual("backend 根目录无效", invalidState.LocalDocumentState.BackendRootStateText, "Invalid backend root should project the invalid-state text.");
    AssertEqual("后端目录有效后才能显示会话路径", invalidState.LocalDocumentState.SessionStoreStateText, "Invalid backend root should suppress session-store status.");
    AssertEqual("后端目录有效后才能显示缓存路径", invalidState.LocalDocumentState.ImageCacheStateText, "Invalid backend root should suppress image-cache status.");

    var validState = session.UpdateBackendRoot(context.RootPath, true);
    AssertTrue(validState.LocalDocumentState.IsBackendRootValid, "Test backend root should project as valid.");
    AssertEqual(Path.Combine(context.RootPath, "data", "runtime-settings.json"), validState.LocalDocumentState.RuntimeConfigPath, "Valid backend root should project the runtime config path.");
    AssertEqual(Path.Combine(context.RootPath, ".env"), validState.LocalDocumentState.BootstrapEnvPath, "Valid backend root should project the bootstrap env path.");
    AssertEqual("已自动检测到 backend 根目录", validState.LocalDocumentState.BackendRootStateText, "Detected backend root should project the detected-state text.");
    AssertEqual(Path.Combine(context.RootPath, "data", "sessions.json"), validState.LocalDocumentState.SessionStorePathText, "Valid backend root should project the session store path.");
    AssertEqual("会话历史文件已存在", validState.LocalDocumentState.SessionStoreStateText, "Existing sessions.json should project as present.");
    AssertEqual(Path.Combine(context.RootPath, "data", "image-cache"), validState.LocalDocumentState.ImageCachePathText, "Valid backend root should project the image cache path.");
    AssertEqual("2 个缓存图片文件", validState.LocalDocumentState.ImageCacheStateText, "Image cache projection should count nested cached files.");
    AssertEqual(new DesktopActivityStateStoragePolicy().ResolveStateFilePath(context.RootPath), validState.LocalDocumentState.ActivityStatePathText, "Activity state path should continue to resolve through the storage policy.");
    AssertEqual(Path.Combine(context.RootPath, "artifacts", "state-snapshots"), validState.LocalDocumentState.StateSnapshotFolderPathText, "Snapshot folder path should be projected through local-document state.");
    AssertEqual("http://10.8.0.5:4319", validState.LocalDocumentState.ControlApiEndpointText, "Control API endpoint should be projected from local extra values.");
    AssertEqual("本机令牌已配置", validState.LocalDocumentState.ControlApiTokenStateText, "Saved control-plane token should project as configured.");

    context.FakeBackend.StatusFailureKind = BackendControlApiFailureKind.Unauthorized;
    context.FakeBackend.StatusFailureMessage = "Control API authentication failed.";
    var pollResult = await session.PollStatusAsync();
    AssertEqual(
        "本机令牌已保存，但后端仍然拒绝它",
        pollResult.NextState?.LocalDocumentState.ControlApiTokenStateText,
        "Unauthorized control API failures should project the rejected-token state text.");

    var clearedTokenState = session.UpdateEditorState(
        session.BuildCurrentShellState().ConfigEditorState.Config,
        string.Empty,
        hasUnsavedChanges: false,
        lastLoadedAtText: session.BuildCurrentShellState().ConfigEditorState.LastLoadedAtText,
        lastSavedAtText: session.BuildCurrentShellState().ConfigEditorState.LastSavedAtText,
        autoStartEnabled: session.BuildCurrentShellState().RuntimeShellState.AutoStartEnabled,
        canStartBackend: session.BuildCurrentShellState().RuntimeShellState.CanStartBackend,
        logText: session.BuildCurrentShellState().UiFeedbackState.LogText);
    AssertEqual("本机令牌未设置", clearedTokenState.LocalDocumentState.ControlApiTokenStateText, "Blank control-plane token should project the unset-token state text.");
}

Task TestDesktopControlPlaneFeedbackAsync()
{
    var statusText = string.Empty;
    var logs = new List<string>();
    var notifications = new List<TrayNotification>();
    var dialogs = new List<(string Title, string Message)>();
    var suggestedActions = new List<string>();

    DesktopControlPlaneFeedback.ApplyOutcome(
        statusText: "Backend stopped",
        logMessages: ["Sent stop command via control API: http://127.0.0.1:3199"],
        notifications:
        [
            new TrayNotification
            {
                Title = "Local AI Runtime",
                Message = "Runtime stopped by user."
            }
        ],
        setStatusText: (text) => statusText = text,
        addLog: (message) => logs.Add(message),
        notify: (notification) => notifications.Add(notification));

    AssertEqual("Backend stopped", statusText, "Feedback helper should apply status text for successful outcomes.");
    AssertEqual(1, logs.Count, "Feedback helper should append outcome log messages.");
    AssertEqual(1, notifications.Count, "Feedback helper should forward tray notifications.");

    DesktopControlPlaneFeedback.ApplyError(
        statusText: "Save failed",
        logMessage: "Save config failed: boom",
        showDialog: true,
        dialogTitle: "Save failed",
        dialogMessage: "Save config failed:\nboom",
        setStatusText: (text) => statusText = text,
        addLog: (message) => logs.Add(message),
        showErrorDialog: (title, message) => dialogs.Add((title, message)));

    AssertEqual("Save failed", statusText, "Feedback helper should apply error status text.");
    AssertTrue(logs.Any(static message => message.Contains("Save config failed", StringComparison.Ordinal)), "Feedback helper should append error logs.");
    AssertEqual(1, dialogs.Count, "Feedback helper should trigger the error dialog when requested.");
    AssertEqual("Save failed", dialogs[0].Title, "Feedback helper should pass dialog title through.");
    AssertContains(dialogs[0].Message, "boom", "Feedback helper should pass dialog message through.");

    DesktopControlPlaneFeedback.ApplyCommandResult(
        new DesktopCommandResult
        {
            NextState = null,
            Succeeded = false,
            StatusText = "Rejected save",
            LogMessages = ["Save rejected by control API."],
            Notifications =
            [
                new TrayNotification
                {
                    Title = "Local AI Runtime",
                    Message = "Review the rejected field."
                }
            ],
            Error = new DesktopUserFacingOperationError
            {
                StatusText = "Rejected save",
                DialogTitle = "Save rejected",
                DialogMessage = "WECHAT_BRIDGE_URL must be a valid ws:// or wss:// URL"
            },
            SuggestedHealthActionKey = DesktopHealthActionKeys.FocusWechatUrl
        },
        showDialog: true,
        setStatusText: (text) => statusText = text,
        addLog: (message) => logs.Add(message),
        notify: (notification) => notifications.Add(notification),
        showErrorDialog: (title, message) => dialogs.Add((title, message)),
        routeSuggestedAction: (actionKey) => suggestedActions.Add(actionKey));

    AssertEqual("Rejected save", statusText, "Feedback helper should forward DesktopCommandResult status text.");
    AssertTrue(logs.Any(static message => message.Contains("Save rejected by control API.", StringComparison.Ordinal)), "Feedback helper should forward DesktopCommandResult logs even when no next state is provided.");
    AssertEqual(2, notifications.Count, "Feedback helper should forward DesktopCommandResult notifications.");
    AssertEqual(2, dialogs.Count, "Feedback helper should trigger dialog routing for DesktopCommandResult errors when requested.");
    AssertEqual(DesktopHealthActionKeys.FocusWechatUrl, suggestedActions[^1], "Feedback helper should route the suggested health action from DesktopCommandResult.");

    DesktopControlPlaneFeedback.ApplyCommandResult(
        new DesktopCommandResult
        {
            NextState = null,
            Succeeded = false,
            StatusText = "Poll failed",
            LogMessages = ["Control API is unreachable."],
            Error = new DesktopUserFacingOperationError
            {
                StatusText = "Poll failed",
                DialogTitle = "Poll failed",
                DialogMessage = "Control API is unreachable."
            },
            SuggestedHealthActionKey = DesktopHealthActionKeys.ReloadConfig
        },
        showDialog: false,
        setStatusText: (text) => statusText = text,
        addLog: (message) => logs.Add(message),
        notify: (notification) => notifications.Add(notification),
        showErrorDialog: (title, message) => dialogs.Add((title, message)),
        routeSuggestedAction: (actionKey) => suggestedActions.Add(actionKey));

    AssertEqual("Poll failed", statusText, "Feedback helper should still update status text when dialogs are suppressed.");
    AssertTrue(logs.Any(static message => message.Contains("Control API is unreachable.", StringComparison.Ordinal)), "Feedback helper should keep forwarding logs when dialogs are suppressed.");
    AssertEqual(2, dialogs.Count, "Feedback helper should skip dialog routing when showDialog is false.");
    AssertEqual(DesktopHealthActionKeys.ReloadConfig, suggestedActions[^1], "Feedback helper should still route suggested health actions when dialogs are suppressed.");
    return Task.CompletedTask;
}

Task TestDesktopOperationErrorFormatterAsync()
{
    var unauthorized = DesktopOperationErrorFormatter.Build(
        operationLabel: "Load config",
        fallbackStatusText: "Load failed",
        technicalMessage: "Control API authentication failed.",
        controlApiFailure: new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unauthorized,
            Message = "Control API authentication failed."
        },
        bootstrapEnvPath: @"D:\runtime\.env");

    AssertContains(unauthorized.StatusText, "本地控制令牌不一致", "Unauthorized guidance should explain the local control token mismatch.");
    AssertEqual("需要本地控制令牌", unauthorized.DialogTitle, "Unauthorized guidance should use a focused dialog title.");
    AssertContains(unauthorized.DialogMessage, "本机连接设置", "Unauthorized guidance should point the user to the local desktop attachment section.");
    AssertContains(unauthorized.DialogMessage, "QQ_AI_BOT_CONTROL_API_TOKEN", "Unauthorized guidance should mention the exact local token key.");
    AssertContains(unauthorized.DialogMessage, @"D:\runtime\.env", "Unauthorized guidance should surface the local env path.");

    var rejected = DesktopOperationErrorFormatter.Build(
        operationLabel: "Save config",
        fallbackStatusText: "Save failed",
        technicalMessage: "WECHAT_BRIDGE_URL must be a valid ws:// or wss:// URL",
        controlApiFailure: new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Rejected,
            Message = "WECHAT_BRIDGE_URL must be a valid ws:// or wss:// URL"
        });

    AssertContains(rejected.StatusText, "control API 拒绝了请求", "Rejected guidance should explain that the control API refused the update.");
    AssertEqual("Save config被拒绝", rejected.DialogTitle, "Rejected guidance should identify the rejected action.");
    AssertContains(rejected.DialogMessage, "先检查下面提到的字段", "Rejected guidance should tell the user what to do next.");
    AssertContains(rejected.DialogMessage, "WECHAT_BRIDGE_URL", "Rejected guidance should preserve the specific validation detail.");
    AssertEqual("查看微信配置", rejected.SuggestedActionLabel, "Rejected guidance should suggest the most relevant field to edit.");
    AssertEqual(DesktopHealthActionKeys.FocusWechatUrl, rejected.SuggestedActionKey, "Rejected guidance should route to the WeChat config field.");

    var deepSeekRejected = DesktopOperationErrorFormatter.Build(
        operationLabel: "Save config",
        fallbackStatusText: "Save failed",
        technicalMessage: "Missing runtime config: DEEPSEEK_API_KEY",
        controlApiFailure: new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Rejected,
            Message = "Missing runtime config: DEEPSEEK_API_KEY"
        });

    AssertContains(deepSeekRejected.DialogMessage, "DEEPSEEK_API_KEY", "DeepSeek rejected guidance should preserve the specific validation detail.");
    AssertEqual("查看 DeepSeek API Key", deepSeekRejected.SuggestedActionLabel, "DeepSeek rejected guidance should suggest the DeepSeek API key field.");
    AssertEqual(DesktopHealthActionKeys.FocusDeepSeekApiKey, deepSeekRejected.SuggestedActionKey, "DeepSeek rejected guidance should route to the DeepSeek API key field.");

    var localSettingsError = DesktopOperationErrorFormatter.Build(
        operationLabel: "Save local control settings",
        fallbackStatusText: "Local control-plane save failed",
        technicalMessage: "Access to the path is denied.",
        bootstrapEnvPath: @"D:\runtime\.env",
        localControlSettingsOperation: true);

    AssertEqual("Local control-plane save failed", localSettingsError.StatusText, "Local settings guidance should preserve the existing status text.");
    AssertEqual("Save local control settings失败", localSettingsError.DialogTitle, "Local settings guidance should identify the local-only save action.");
    AssertContains(localSettingsError.DialogMessage, "本地 desktop 附着设置", "Local settings guidance should explain that only local desktop settings are affected.");
    AssertContains(localSettingsError.DialogMessage, @"D:\runtime\.env", "Local settings guidance should show the target file path.");
    AssertEqual("查看本机令牌", localSettingsError.SuggestedActionLabel, "Local settings guidance should suggest the local token field.");
    AssertEqual(DesktopHealthActionKeys.FocusControlApiToken, localSettingsError.SuggestedActionKey, "Local settings guidance should route to the local token field.");

    var unreachable = DesktopOperationErrorFormatter.Build(
        operationLabel: "Load config",
        fallbackStatusText: "Load failed",
        technicalMessage: "No connection could be made because the target machine actively refused it.",
        controlApiFailure: new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unreachable,
            Message = "No connection could be made because the target machine actively refused it."
        },
        canStartBackend: true);

    AssertContains(unreachable.DialogMessage, "发生了什么", "Unreachable guidance should use the structured dialog layout.");
    AssertContains(unreachable.DialogMessage, "先从这个窗口启动 backend", "Unreachable guidance should explicitly tell the user to start the backend when that action is available.");
    AssertEqual("启动后端", unreachable.SuggestedActionLabel, "Unreachable guidance should route to backend start when the desktop can still launch it.");
    AssertEqual(DesktopHealthActionKeys.StartBackend, unreachable.SuggestedActionKey, "Unreachable guidance should expose the backend start action.");

    var incompatible = DesktopOperationErrorFormatter.Build(
        operationLabel: "Save config",
        fallbackStatusText: "Save failed",
        technicalMessage: BackendControlApiService.LegacyConfigContractMessage,
        controlApiFailure: new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Incompatible,
            Message = BackendControlApiService.LegacyConfigContractMessage
        });

    AssertContains(incompatible.StatusText, "版本不兼容", "Incompatible guidance should explain the contract mismatch.");
    AssertContains(incompatible.DialogMessage, "旧版配置协议", "Incompatible guidance should explain the stale backend contract.");
    AssertEqual("打开 backend 目录", incompatible.SuggestedActionLabel, "Incompatible guidance should offer a backend-folder action.");
    AssertEqual(DesktopHealthActionKeys.OpenBackendFolder, incompatible.SuggestedActionKey, "Incompatible guidance should route to the backend folder action.");

    return Task.CompletedTask;
}

Task TestBackendRuntimeSnapshotViewHelperAsync()
{
    var target = new ObservableCollection<BackendRecentActivityItem>
    {
        new()
        {
            EventKey = "old",
            Summary = "old"
        }
    };

    BackendRuntimeSnapshotViewHelper.ReplaceRecentActivities(
        target,
        [
            new BackendRecentActivityItem
            {
                EventKey = "req-1",
                Summary = "request"
            },
            new BackendRecentActivityItem
            {
                EventKey = "",
                Summary = "should drop"
            }
        ]);

    AssertEqual(1, target.Count, "Runtime snapshot view helper should replace the collection and drop invalid activity items.");
    AssertEqual("req-1", target[0].EventKey, "Runtime snapshot view helper should preserve valid event keys.");

    var notifiedProperties = new List<string>();
    BackendRuntimeSnapshotViewHelper.NotifyRuntimeSnapshotChanged((propertyName) => notifiedProperties.Add(propertyName));

    AssertEqual(
        DesktopShellPropertyCatalog.RuntimeSnapshotPropertyNames.Length,
        notifiedProperties.Count,
        "Runtime snapshot notifier should enumerate every runtime property from the shell catalog exactly once.");
    AssertTrue(
        notifiedProperties.SequenceEqual(DesktopShellPropertyCatalog.RuntimeSnapshotPropertyNames),
        "Runtime snapshot notifier should follow the runtime property catalog ordering.");
    return Task.CompletedTask;
}

Task TestDesktopShellPropertyCatalogAsync()
{
    var allPropertyNames = DesktopShellPropertyCatalog.AllPropertyNames().ToArray();

    AssertEqual(
        allPropertyNames.Length,
        allPropertyNames.Distinct(StringComparer.Ordinal).Count(),
        "Shell property catalog should not contain duplicate property names.");
    AssertTrue(
        DesktopShellPropertyCatalog.RuntimeSnapshotPropertyNames.Contains("LatestTurnHeadlineText", StringComparer.Ordinal),
        "Runtime property catalog should include latest-turn projection properties.");
    AssertTrue(
        DesktopShellPropertyCatalog.RuntimeSnapshotPropertyNames.Contains("IsProcessRunning", StringComparer.Ordinal),
        "Runtime property catalog should include direct runtime state properties used by UI triggers.");
    AssertTrue(
        DesktopShellPropertyCatalog.HealthPropertyNames.Contains("HealthLatestIssueText", StringComparer.Ordinal),
        "Health property catalog should include latest-issue projection properties.");
    AssertTrue(
        DesktopShellPropertyCatalog.GuidePropertyNames.Contains("OverallReadinessSummaryText", StringComparer.Ordinal),
        "Guide property catalog should include readiness projection properties.");
    AssertTrue(
        DesktopShellPropertyCatalog.SnapshotPropertyNames.Contains("LastStateRestoreNextStepText", StringComparer.Ordinal),
        "Snapshot property catalog should include restore guidance projection properties.");
    AssertTrue(
        DesktopShellPropertyCatalog.LocalDocumentPropertyNames.Contains("BackendRootStateText", StringComparer.Ordinal),
        "Local-document property catalog should include backend-root projection properties.");
    return Task.CompletedTask;
}

async Task TestBackendControlApiServiceUnauthorizedAsync()
{
    var port = GetFreeTcpPort();
    var prefix = $"http://127.0.0.1:{port}/";
    using var listener = new HttpListener();
    listener.Prefixes.Add(prefix);
    listener.Start();

    var serverTask = Task.Run(async () =>
    {
        var context = await listener.GetContextAsync();
        var response = context.Response;

        try
        {
            response.StatusCode = 401;
            await response.OutputStream.WriteAsync(
                Encoding.UTF8.GetBytes("{\"error\":\"Control API authentication failed.\"}"));
        }
        finally
        {
            response.Close();
        }
    });

    using var service = new BackendControlApiService(new Uri(prefix), TimeSpan.FromSeconds(2));
    service.SetAccessToken("wrong-token");
    var config = await service.TryGetConfigAsync();

    AssertEqual(null, config, "Unauthorized config request should return null.");
    AssertEqual(BackendControlApiFailureKind.Unauthorized, service.LastFailure.Kind, "401 responses should be classified as unauthorized.");
    AssertContains(service.LastFailure.Message, "authentication failed", "Unauthorized failures should preserve the backend error text.");

    listener.Stop();
    await serverTask;
}

async Task TestBackendControlApiServiceRejectedSaveAsync()
{
    var port = GetFreeTcpPort();
    var prefix = $"http://127.0.0.1:{port}/";
    using var listener = new HttpListener();
    listener.Prefixes.Add(prefix);
    listener.Start();

    var serverTask = Task.Run(async () =>
    {
        var context = await listener.GetContextAsync();
        var response = context.Response;

        try
        {
            response.StatusCode = 400;
            await response.OutputStream.WriteAsync(
                Encoding.UTF8.GetBytes("{\"error\":\"WECHAT_BRIDGE_URL must be a valid ws:// or wss:// URL\"}"));
        }
        finally
        {
            response.Close();
        }
    });

    using var service = new BackendControlApiService(new Uri(prefix), TimeSpan.FromSeconds(2));
    var saveResult = await service.TrySaveConfigAsync(new BotConfig
    {
        WechatBridgeUrl = "not-a-valid-wechat-url"
    });

    AssertEqual(null, saveResult, "Rejected save should return null.");
    AssertEqual(BackendControlApiFailureKind.Rejected, service.LastFailure.Kind, "Rejected save should set failure kind.");
    AssertContains(service.LastFailure.Message, "WECHAT_BRIDGE_URL", "Rejected save should preserve backend error text.");

    listener.Stop();
    await serverTask;
}

async Task TestBackendControlApiServiceLegacyContractAsync()
{
    var port = GetFreeTcpPort();
    var prefix = $"http://127.0.0.1:{port}/";
    using var listener = new HttpListener();
    listener.Prefixes.Add(prefix);
    listener.Start();

    var serverTask = Task.Run(async () =>
    {
        for (var index = 0; index < 2; index++)
        {
            var context = await listener.GetContextAsync();
            var response = context.Response;

            try
            {
                await response.OutputStream.WriteAsync(
                    Encoding.UTF8.GetBytes("{\"openAiApiKey\":\"test-key\",\"envPath\":\"D:\\\\temp\\\\.env\",\"restartRequired\":false}"));
            }
            finally
            {
                response.Close();
            }
        }
    });

    using var service = new BackendControlApiService(new Uri(prefix), TimeSpan.FromSeconds(2));

    var config = await service.TryGetConfigAsync();
    AssertEqual(null, config, "Legacy config responses should be rejected.");
    AssertEqual(BackendControlApiFailureKind.Incompatible, service.LastFailure.Kind, "Legacy config responses should be classified as incompatible.");
    AssertContains(service.LastFailure.Message, "legacy config contract", "Legacy config responses should preserve the compatibility failure message.");

    var saveResult = await service.TrySaveConfigAsync(new BotConfig
    {
        OpenAiApiKey = "test-key"
    });
    AssertEqual(null, saveResult, "Legacy save responses should be rejected.");
    AssertEqual(BackendControlApiFailureKind.Incompatible, service.LastFailure.Kind, "Legacy save responses should be classified as incompatible.");
    AssertContains(service.LastFailure.Message, "legacy config contract", "Legacy save responses should preserve the compatibility failure message.");

    listener.Stop();
    await serverTask;
}

async Task TestBackendControlApiServiceEmptyConfigResponseAsync()
{
    var port = GetFreeTcpPort();
    var prefix = $"http://127.0.0.1:{port}/";
    using var listener = new HttpListener();
    listener.Prefixes.Add(prefix);
    listener.Start();

    var serverTask = Task.Run(async () =>
    {
        var context = await listener.GetContextAsync();
        var response = context.Response;

        try
        {
            await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("null"));
        }
        finally
        {
            response.Close();
        }
    });

    using var service = new BackendControlApiService(new Uri(prefix), TimeSpan.FromSeconds(2));
    var config = await service.TryGetConfigAsync();

    AssertEqual(null, config, "Empty successful config response should return null.");
    AssertEqual(BackendControlApiFailureKind.Unknown, service.LastFailure.Kind, "Empty successful config response should be classified as unknown.");
    AssertContains(service.LastFailure.Message, "empty config response", "Empty successful config response should preserve the synthesized failure message.");

    listener.Stop();
    await serverTask;
}

async Task TestBackendControlApiServiceEmptySaveResponseAsync()
{
    var port = GetFreeTcpPort();
    var prefix = $"http://127.0.0.1:{port}/";
    using var listener = new HttpListener();
    listener.Prefixes.Add(prefix);
    listener.Start();

    var serverTask = Task.Run(async () =>
    {
        var context = await listener.GetContextAsync();
        var response = context.Response;

        try
        {
            await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("null"));
        }
        finally
        {
            response.Close();
        }
    });

    using var service = new BackendControlApiService(new Uri(prefix), TimeSpan.FromSeconds(2));
    var saveResult = await service.TrySaveConfigAsync(new BotConfig
    {
        OpenAiApiKey = "test-key"
    });

    AssertEqual(null, saveResult, "Empty successful save response should return null.");
    AssertEqual(BackendControlApiFailureKind.Unknown, service.LastFailure.Kind, "Empty successful save response should be classified as unknown.");
    AssertContains(service.LastFailure.Message, "empty save response", "Empty successful save response should preserve the synthesized failure message.");

    listener.Stop();
    await serverTask;
}

async Task TestBackendControlApiServiceEmptyRuntimeResponsesAsync()
{
    var port = GetFreeTcpPort();
    var prefix = $"http://127.0.0.1:{port}/";
    using var listener = new HttpListener();
    listener.Prefixes.Add(prefix);
    listener.Start();

    var serverTask = Task.Run(async () =>
    {
        for (var index = 0; index < 3; index++)
        {
            var context = await listener.GetContextAsync();
            var response = context.Response;

            try
            {
                await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("null"));
            }
            finally
            {
                response.Close();
            }
        }
    });

    using var service = new BackendControlApiService(new Uri(prefix), TimeSpan.FromSeconds(2));

    var status = await service.TryGetStatusAsync();
    AssertEqual(null, status, "Empty successful status response should return null.");
    AssertEqual(BackendControlApiFailureKind.Unknown, service.LastFailure.Kind, "Empty successful status response should be classified as unknown.");
    AssertContains(service.LastFailure.Message, "empty status response", "Status null-body failure message should be preserved.");

    var startStatus = await service.TryStartAsync();
    AssertEqual(null, startStatus, "Empty successful start response should return null.");
    AssertEqual(BackendControlApiFailureKind.Unknown, service.LastFailure.Kind, "Empty successful start response should be classified as unknown.");
    AssertContains(service.LastFailure.Message, "empty start response", "Start null-body failure message should be preserved.");

    var stopStatus = await service.TryStopAsync();
    AssertEqual(null, stopStatus, "Empty successful stop response should return null.");
    AssertEqual(BackendControlApiFailureKind.Unknown, service.LastFailure.Kind, "Empty successful stop response should be classified as unknown.");
    AssertContains(service.LastFailure.Message, "empty stop response", "Stop null-body failure message should be preserved.");

    listener.Stop();
    await serverTask;
}

async Task TestBotProcessServiceStartStopAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-bot-process-");
    var srcPath = Path.Combine(rootPath, "src");
    Directory.CreateDirectory(srcPath);
    await File.WriteAllTextAsync(
        Path.Combine(srcPath, "index.mjs"),
        """
        console.log('desktop-bot-process-started');
        setInterval(() => {}, 1000);
        process.on('SIGTERM', () => process.exit(0));
        """,
        Encoding.UTF8);

    var exitSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var service = new BotProcessService();
    service.ProcessExited += (_, _) => exitSignal.TrySetResult();

    service.Start(rootPath);

    AssertTrue(service.IsRunning, "BotProcessService should report running after Start.");

    await service.StopAsync();

    AssertFalse(service.IsRunning, "BotProcessService should report stopped after StopAsync.");
    await WaitForAsync(() => exitSignal.Task.IsCompleted, "bot process exit event");
}

async Task TestBotProcessServiceDetachKeepsBackendAliveAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-bot-detach-");
    var srcPath = Path.Combine(rootPath, "src");
    Directory.CreateDirectory(srcPath);
    await File.WriteAllTextAsync(
        Path.Combine(srcPath, "index.mjs"),
        """
        console.log('desktop-bot-detach-started');
        setInterval(() => {}, 1000);
        process.on('SIGTERM', () => process.exit(0));
        """,
        Encoding.UTF8);

    var logs = new List<string>();
    using var service = new BotProcessService();
    service.LogReceived += (_, message) => logs.Add(message);

    service.Start(rootPath);
    await WaitForAsync(
        () => logs.Any(log => log.Contains("Started backend process, PID=", StringComparison.Ordinal)),
        "backend start log");

    var pidLog = logs.First(log => log.Contains("Started backend process, PID=", StringComparison.Ordinal));
    var pidStart = pidLog.IndexOf("PID=", StringComparison.Ordinal);
    var pidText = new string(pidLog[(pidStart + 4)..].TakeWhile(char.IsDigit).ToArray());
    var pid = int.Parse(pidText, System.Globalization.CultureInfo.InvariantCulture);

    service.Detach();

    AssertFalse(service.IsRunning, "BotProcessService should release ownership after Detach.");

    using var process = Process.GetProcessById(pid);
    AssertFalse(process.HasExited, "Detached backend process should still be alive.");

    process.Kill(entireProcessTree: true);
    await WaitForProcessExitAsync(process, "detached backend process");
}

Task TestBotProcessServiceMissingDirectoryAsync()
{
    using var service = new BotProcessService();
    var missingPath = Path.Combine(Path.GetTempPath(), $"desktop-missing-{Guid.NewGuid():N}");

    try
    {
        service.Start(missingPath);
        throw new InvalidOperationException("Expected missing directory to throw.");
    }
    catch (DirectoryNotFoundException)
    {
        return Task.CompletedTask;
    }
}

async Task TestAsyncRelayCommandReentryAsync()
{
    var startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var invocationCount = 0;

    var command = new AsyncRelayCommand(async () =>
    {
        Interlocked.Increment(ref invocationCount);
        startSignal.TrySetResult();
        await releaseSignal.Task;
    });

    AssertTrue(command.CanExecute(null), "AsyncRelayCommand should be executable before start.");
    command.Execute(null);
    await startSignal.Task;

    AssertFalse(command.CanExecute(null), "AsyncRelayCommand should block re-entry while running.");
    command.Execute(null);
    AssertEqual(1, invocationCount, "AsyncRelayCommand should ignore re-entry.");

    releaseSignal.TrySetResult();
    await WaitForAsync(() => command.CanExecute(null), "async relay command completion");
    AssertEqual(1, invocationCount, "AsyncRelayCommand should execute only once.");
}

Task TestRelayCommandCanExecuteAsync()
{
    var executed = false;
    var command = new RelayCommand(
        _ => executed = true,
        _ => true);

    AssertTrue(command.CanExecute(null), "RelayCommand should allow execution when predicate returns true.");
    command.Execute(null);
    AssertTrue(executed, "RelayCommand should execute action.");

    executed = false;
    var blockedCommand = new RelayCommand(
        _ => executed = true,
        _ => false);

    AssertFalse(blockedCommand.CanExecute(null), "RelayCommand should block execution when predicate returns false.");
    if (blockedCommand.CanExecute(null))
    {
        blockedCommand.Execute(null);
    }

    AssertFalse(executed, "Blocked RelayCommand should not execute action.");
    return Task.CompletedTask;
}

Task TestAutoStartServiceCommandResolutionAsync()
{
    var command = AutoStartService.BuildAutoStartCommand(@"C:\Apps\QQAIBot.Desktop.exe");
    AssertEqual(
        "\"C:\\Apps\\QQAIBot.Desktop.exe\" --minimized --ensure-runtime",
        command,
        "Auto-start command should include expected arguments.");

    var directExecutable = AutoStartService.ResolveAutoStartExecutablePath(
        processPath: @"C:\Apps\QQAIBot.Desktop.exe",
        appBaseDirectory: @"C:\Apps",
        entryAssemblyName: "QQAIBot.Desktop",
        fileExists: _ => false);
    AssertEqual(
        @"C:\Apps\QQAIBot.Desktop.exe",
        directExecutable,
        "Direct executable path should be preferred when process is not dotnet.");

    var resolvedFromBaseDirectory = AutoStartService.ResolveAutoStartExecutablePath(
        processPath: @"C:\Program Files\dotnet\dotnet.exe",
        appBaseDirectory: @"C:\Apps",
        entryAssemblyName: "QQAIBot.Desktop",
        fileExists: candidate => string.Equals(candidate, @"C:\Apps\QQAIBot.Desktop.exe", StringComparison.OrdinalIgnoreCase));
    AssertEqual(
        @"C:\Apps\QQAIBot.Desktop.exe",
        resolvedFromBaseDirectory,
        "Published exe in app base directory should be used when process is dotnet.");

    return Task.CompletedTask;
}

Task TestAutoStartServiceRegistryStoreAsync()
{
    var store = new FakeAutoStartRegistryStore();
    var service = new AutoStartService(
        registryStore: store,
        resolveExecutablePath: () => @"C:\Apps\QQAIBot.Desktop.exe");

    AssertFalse(service.IsEnabled(), "AutoStartService should start disabled when registry value is missing.");

    service.SetEnabled(true);

    AssertTrue(service.IsEnabled(), "AutoStartService should report enabled after writing registry value.");
    AssertEqual(
        "\"C:\\Apps\\QQAIBot.Desktop.exe\" --minimized --ensure-runtime",
        store.GetValue("QQAIBot.Desktop") ?? string.Empty,
        "AutoStartService should write expected startup command.");

    service.SetEnabled(false);

    AssertFalse(service.IsEnabled(), "AutoStartService should report disabled after deleting registry value.");
    AssertEqual(null, store.GetValue("QQAIBot.Desktop"), "AutoStartService should delete startup value when disabled.");
    return Task.CompletedTask;
}

Task TestAutoStartServiceWindowsRegistryStoreAsync()
{
    var suffix = Guid.NewGuid().ToString("N");
    var runKeyPath = $@"Software\QQAIBot.Desktop.Tests\{suffix}";

    try
    {
        var service = new AutoStartService(
            registryStore: new WindowsAutoStartRegistryStore(runKeyPath),
            resolveExecutablePath: () => @"C:\Apps\QQAIBot.Desktop.exe");

        AssertFalse(service.IsEnabled(), "Registry-backed AutoStartService should start disabled on empty test key.");

        service.SetEnabled(true);

        using (var runKey = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: false))
        {
            AssertEqual(
                "\"C:\\Apps\\QQAIBot.Desktop.exe\" --minimized --ensure-runtime",
                runKey?.GetValue("QQAIBot.Desktop") as string,
                "Registry-backed AutoStartService should write expected startup command.");
        }

        AssertTrue(service.IsEnabled(), "Registry-backed AutoStartService should report enabled after writing value.");

        service.SetEnabled(false);

        using (var runKey = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: false))
        {
            AssertEqual(
                null,
                runKey?.GetValue("QQAIBot.Desktop") as string,
                "Registry-backed AutoStartService should remove startup value when disabled.");
        }

        AssertFalse(service.IsEnabled(), "Registry-backed AutoStartService should report disabled after removing value.");
    }
    finally
    {
        Registry.CurrentUser.DeleteSubKeyTree(runKeyPath, throwOnMissingSubKey: false);
    }

    return Task.CompletedTask;
}

async Task TestSingleInstanceCoordinatorActivateAsync()
{
    var suffix = Guid.NewGuid().ToString("N");
    using var primary = new SingleInstanceCoordinator(
        mutexName: $"QQAIBot.Desktop.Tests.SingleInstance.{suffix}",
        activateEventName: $"QQAIBot.Desktop.Tests.Activate.{suffix}",
        ensureRuntimeEventName: $"QQAIBot.Desktop.Tests.EnsureRuntime.{suffix}");
    using var secondary = new SingleInstanceCoordinator(
        mutexName: $"QQAIBot.Desktop.Tests.SingleInstance.{suffix}",
        activateEventName: $"QQAIBot.Desktop.Tests.Activate.{suffix}",
        ensureRuntimeEventName: $"QQAIBot.Desktop.Tests.EnsureRuntime.{suffix}");

    var activateSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var ensureSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    AssertTrue(
        primary.TryAcquirePrimaryOwnership(
            () => activateSignal.TrySetResult(),
            () => ensureSignal.TrySetResult()),
        "Primary coordinator should acquire single-instance ownership.");

    secondary.SignalPrimaryInstance(ensureRuntime: false);

    await WaitForAsync(() => activateSignal.Task.IsCompleted, "activate signal delivery");
    AssertFalse(ensureSignal.Task.IsCompleted, "Activate signal should not trigger ensure-runtime callback.");
}

async Task TestSingleInstanceCoordinatorEnsureRuntimeAsync()
{
    var suffix = Guid.NewGuid().ToString("N");
    using var primary = new SingleInstanceCoordinator(
        mutexName: $"QQAIBot.Desktop.Tests.SingleInstance.{suffix}",
        activateEventName: $"QQAIBot.Desktop.Tests.Activate.{suffix}",
        ensureRuntimeEventName: $"QQAIBot.Desktop.Tests.EnsureRuntime.{suffix}");
    using var secondary = new SingleInstanceCoordinator(
        mutexName: $"QQAIBot.Desktop.Tests.SingleInstance.{suffix}",
        activateEventName: $"QQAIBot.Desktop.Tests.Activate.{suffix}",
        ensureRuntimeEventName: $"QQAIBot.Desktop.Tests.EnsureRuntime.{suffix}");

    var activateSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var ensureSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    AssertTrue(
        primary.TryAcquirePrimaryOwnership(
            () => activateSignal.TrySetResult(),
            () => ensureSignal.TrySetResult()),
        "Primary coordinator should acquire single-instance ownership.");

    secondary.SignalPrimaryInstance(ensureRuntime: true);

    await WaitForAsync(() => ensureSignal.Task.IsCompleted, "ensure-runtime signal delivery");
    AssertFalse(activateSignal.Task.IsCompleted, "Ensure-runtime signal should not trigger activate callback.");
}

async Task TestDesktopCrossProcessSingleInstanceActivationAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-cross-process-");
    Directory.CreateDirectory(Path.Combine(rootPath, "src"));
    await File.WriteAllTextAsync(Path.Combine(rootPath, "package.json"), "{}", Encoding.UTF8);
    await File.WriteAllTextAsync(
        Path.Combine(rootPath, "src", "index.mjs"),
        "setInterval(() => {}, 1000); process.on('SIGTERM', () => process.exit(0));",
        Encoding.UTF8);
    await File.WriteAllTextAsync(
        Path.Combine(rootPath, ".env"),
        string.Join(
            Environment.NewLine,
            [
                "OPENAI_API_KEY=test-key",
                "NAPCAT_TOKEN=test-token",
                "WECHAT_BRIDGE_URL=",
                "BOT_PREFIX=/ai"
            ]) + Environment.NewLine,
        Encoding.UTF8);

    var signalFilePath = Path.Combine(rootPath, "desktop-signals.log");
    var scopeSuffix = Guid.NewGuid().ToString("N");
    var desktopExePath = ResolveDesktopExecutablePath();

    using var primaryProcess = StartDesktopProcess(
        desktopExePath,
        rootPath,
        [],
        signalFilePath,
        scopeSuffix);

    try
    {
        await WaitForSignalAsync(signalFilePath, "window-loaded", timeoutMs: 30000);

        using var activateProcess = StartDesktopProcess(
            desktopExePath,
            rootPath,
            [],
            signalFilePath,
            scopeSuffix);
        await WaitForProcessExitAsync(activateProcess, "secondary desktop activation process", timeoutMs: 30000);
        await WaitForSignalAsync(signalFilePath, "restore-from-external-activation", timeoutMs: 30000);

        using var ensureRuntimeProcess = StartDesktopProcess(
            desktopExePath,
            rootPath,
            ["--ensure-runtime"],
            signalFilePath,
            scopeSuffix);
        await WaitForProcessExitAsync(ensureRuntimeProcess, "secondary desktop ensure-runtime process", timeoutMs: 30000);
        await WaitForSignalAsync(signalFilePath, "ensure-runtime-from-external-activation", timeoutMs: 30000);
    }
    finally
    {
        await StopProcessAsync(primaryProcess);
    }
}

async Task TestMainWindowSmokeAutomationAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-ui-smoke-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeLocalBootstrapStore = context.FakeLocalBootstrapStore;
    var fakeLocalPathOperationsService = context.FakeLocalPathOperationsService;

    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore,
                fakeLocalBootstrapStore,
                fakeLocalPathOperationsService);
            var window = new MainWindow(
                launchMinimizedToTray: false,
                ensureRuntimeOnStartup: false,
                viewModel: viewModel,
                enableNotifyIcon: false);

            try
            {
                window.Show();
                await WaitForAsync(() => fakeBackend.GetConfigCallCount > 0, "main window initial config load");
                await WaitForAsync(
                    () => (window.FindName("WechatBotPrefixTextBox") as TextBox)?.Text == "/ai",
                    "wechat prefix textbox binding");
                await WaitForAsync(
                    () => (window.FindName("OpenAiAdvancedEnableWebSearchCheckBox") as CheckBox)?.IsChecked == true,
                    "openai advanced web search checkbox binding");
                await WaitForAsync(
                    () => ((window.FindName("DefaultBotInstructionsTextBox") as TextBox)?.Text ?? string.Empty).Contains("你是本地 AI 助手，会处理来自 QQ 和微信的消息。", StringComparison.Ordinal),
                    "default bot instructions textbox binding");
                await WaitForAsync(
                    () => ((window.FindName("LatestQqLlmSummaryTextBlock") as TextBlock)?.Text ?? string.Empty).Contains("default / gpt-5.4 / responses", StringComparison.Ordinal),
                    "latest qq llm summary binding");
                await WaitForAsync(
                    () => ((window.FindName("HealthStateTextBlock") as TextBlock)?.Text ?? string.Empty).Contains("可启动", StringComparison.Ordinal),
                    "health state binding");

                var wechatPrefixTextBox = window.FindName("WechatBotPrefixTextBox") as TextBox
                    ?? throw new InvalidOperationException("WechatBotPrefixTextBox not found.");
                var firstRunGuideTextBlock = window.FindName("FirstRunGuideTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("FirstRunGuideTextBlock not found.");
                var firstRunGuideProgressTextBlock = window.FindName("FirstRunGuideProgressTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("FirstRunGuideProgressTextBlock not found.");
                var firstRunGuideCurrentTextBlock = window.FindName("FirstRunGuideCurrentTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("FirstRunGuideCurrentTextBlock not found.");
                var firstRunGuideCompletionTextBlock = window.FindName("FirstRunGuideCompletionTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("FirstRunGuideCompletionTextBlock not found.");
                var firstRunGuideStepsItemsControl = window.FindName("FirstRunGuideStepsItemsControl") as ItemsControl
                    ?? throw new InvalidOperationException("FirstRunGuideStepsItemsControl not found.");
                var dailyUseGuideTextBlock = window.FindName("DailyUseGuideTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("DailyUseGuideTextBlock not found.");
                var dailyUseGuideProgressTextBlock = window.FindName("DailyUseGuideProgressTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("DailyUseGuideProgressTextBlock not found.");
                var dailyUseGuideCurrentTextBlock = window.FindName("DailyUseGuideCurrentTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("DailyUseGuideCurrentTextBlock not found.");
                var dailyUseGuideCompletionTextBlock = window.FindName("DailyUseGuideCompletionTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("DailyUseGuideCompletionTextBlock not found.");
                var dailyUseGuideStepsItemsControl = window.FindName("DailyUseGuideStepsItemsControl") as ItemsControl
                    ?? throw new InvalidOperationException("DailyUseGuideStepsItemsControl not found.");
                var overallReadinessStateTextBlock = window.FindName("OverallReadinessStateTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("OverallReadinessStateTextBlock not found.");
                var overallReadinessSummaryTextBlock = window.FindName("OverallReadinessSummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("OverallReadinessSummaryTextBlock not found.");
                var overallReadinessRecentActivityTextBlock = window.FindName("OverallReadinessRecentActivityTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("OverallReadinessRecentActivityTextBlock not found.");
                var overallReadinessActionButton = window.FindName("OverallReadinessActionButton") as Button
                    ?? throw new InvalidOperationException("OverallReadinessActionButton not found.");
                var overallReadinessActionSummaryTextBlock = window.FindName("OverallReadinessActionSummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("OverallReadinessActionSummaryTextBlock not found.");
                var overallReadinessActionsItemsControl = window.FindName("OverallReadinessActionsItemsControl") as ItemsControl
                    ?? throw new InvalidOperationException("OverallReadinessActionsItemsControl not found.");
                var latestTurnHeadlineTextBlock = window.FindName("LatestTurnHeadlineTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestTurnHeadlineTextBlock not found.");
                var latestTurnSummaryTextBlock = window.FindName("LatestTurnSummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestTurnSummaryTextBlock not found.");
                var latestTurnCapabilitiesTextBlock = window.FindName("LatestTurnCapabilitiesTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestTurnCapabilitiesTextBlock not found.");
                var latestTurnReasonTextBlock = window.FindName("LatestTurnReasonTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestTurnReasonTextBlock not found.");
                var latestTurnOutcomeTextBlock = window.FindName("LatestTurnOutcomeTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestTurnOutcomeTextBlock not found.");
                var latestTurnActionButton = window.FindName("LatestTurnActionButton") as Button
                    ?? throw new InvalidOperationException("LatestTurnActionButton not found.");
                var defaultBotInstructionsTextBox = window.FindName("DefaultBotInstructionsTextBox") as TextBox
                    ?? throw new InvalidOperationException("DefaultBotInstructionsTextBox not found.");
                var botPersonaTextBox = window.FindName("BotPersonaTextBox") as TextBox
                    ?? throw new InvalidOperationException("BotPersonaTextBox not found.");
                var effectiveBotInstructionsTextBox = window.FindName("EffectiveBotInstructionsTextBox") as TextBox
                    ?? throw new InvalidOperationException("EffectiveBotInstructionsTextBox not found.");
                var allowedChatIdsTextBox = window.FindName("AllowedChatIdsTextBox") as TextBox
                    ?? throw new InvalidOperationException("AllowedChatIdsTextBox not found.");
                var deepSeekFallbackEnabledCheckBox = window.FindName("DeepSeekFallbackEnabledCheckBox") as CheckBox
                    ?? throw new InvalidOperationException("DeepSeekFallbackEnabledCheckBox not found.");
                var deepSeekModelTextBox = window.FindName("DeepSeekModelTextBox") as TextBox
                    ?? throw new InvalidOperationException("DeepSeekModelTextBox not found.");
                var deepSeekBaseUrlTextBox = window.FindName("DeepSeekBaseUrlTextBox") as TextBox
                    ?? throw new InvalidOperationException("DeepSeekBaseUrlTextBox not found.");
                var defaultWebSearchCheckBox = window.FindName("OpenAiDefaultEnableWebSearchCheckBox") as CheckBox
                    ?? throw new InvalidOperationException("OpenAiDefaultEnableWebSearchCheckBox not found.");
                var advancedWebSearchCheckBox = window.FindName("OpenAiAdvancedEnableWebSearchCheckBox") as CheckBox
                    ?? throw new InvalidOperationException("OpenAiAdvancedEnableWebSearchCheckBox not found.");
                var defaultCodeInterpreterCheckBox = window.FindName("OpenAiDefaultEnableCodeInterpreterCheckBox") as CheckBox
                    ?? throw new InvalidOperationException("OpenAiDefaultEnableCodeInterpreterCheckBox not found.");
                var advancedCodeInterpreterCheckBox = window.FindName("OpenAiAdvancedEnableCodeInterpreterCheckBox") as CheckBox
                    ?? throw new InvalidOperationException("OpenAiAdvancedEnableCodeInterpreterCheckBox not found.");
                var latestQqLlmSummaryTextBlock = window.FindName("LatestQqLlmSummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqLlmSummaryTextBlock not found.");
                var latestQqActivitySummaryTextBlock = window.FindName("LatestQqActivitySummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqActivitySummaryTextBlock not found.");
                var qqFailuresOnlyToggleButton = window.FindName("QqFailuresOnlyToggleButton") as System.Windows.Controls.Primitives.ToggleButton
                    ?? throw new InvalidOperationException("QqFailuresOnlyToggleButton not found.");
                var qqPinSelectionToggleButton = window.FindName("QqPinSelectionToggleButton") as System.Windows.Controls.Primitives.ToggleButton
                    ?? throw new InvalidOperationException("QqPinSelectionToggleButton not found.");
                var clearQqActivityHistoryButton = window.FindName("ClearQqActivityHistoryButton") as Button
                    ?? throw new InvalidOperationException("ClearQqActivityHistoryButton not found.");
                var latestQqRecentActivityListBox = window.FindName("LatestQqRecentActivityListBox") as ListBox
                    ?? throw new InvalidOperationException("LatestQqRecentActivityListBox not found.");
                var selectedQqRecentActivitySummaryTextBlock = window.FindName("SelectedQqRecentActivitySummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("SelectedQqRecentActivitySummaryTextBlock not found.");
                var selectedQqRecentActivityMetaTextBlock = window.FindName("SelectedQqRecentActivityMetaTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("SelectedQqRecentActivityMetaTextBlock not found.");
                var selectedQqRecentActivityDetailTextBlock = window.FindName("SelectedQqRecentActivityDetailTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("SelectedQqRecentActivityDetailTextBlock not found.");
                var latestQqActivityStateTextBlock = window.FindName("LatestQqActivityStateTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqActivityStateTextBlock not found.");
                var latestQqLatestSuccessTextBlock = window.FindName("LatestQqLatestSuccessTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqLatestSuccessTextBlock not found.");
                var latestQqLatestFailureTextBlock = window.FindName("LatestQqLatestFailureTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqLatestFailureTextBlock not found.");
                var latestQqRecoveryTextBlock = window.FindName("LatestQqRecoveryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqRecoveryTextBlock not found.");
                var latestQqRequestTimelineTextBlock = window.FindName("LatestQqRequestTimelineTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqRequestTimelineTextBlock not found.");
                var latestQqDecisionTriggerTextBlock = window.FindName("LatestQqDecisionTriggerTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqDecisionTriggerTextBlock not found.");
                var latestQqDecisionCapabilityTextBlock = window.FindName("LatestQqDecisionCapabilityTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqDecisionCapabilityTextBlock not found.");
                var latestQqDecisionUpgradeTextBlock = window.FindName("LatestQqDecisionUpgradeTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqDecisionUpgradeTextBlock not found.");
                var latestQqRequestedCapabilitiesTextBlock = window.FindName("LatestQqRequestedCapabilitiesTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqRequestedCapabilitiesTextBlock not found.");
                var latestWechatLlmSummaryTextBlock = window.FindName("LatestWechatLlmSummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatLlmSummaryTextBlock not found.");
                var latestWechatActivitySummaryTextBlock = window.FindName("LatestWechatActivitySummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatActivitySummaryTextBlock not found.");
                var wechatFailuresOnlyToggleButton = window.FindName("WechatFailuresOnlyToggleButton") as System.Windows.Controls.Primitives.ToggleButton
                    ?? throw new InvalidOperationException("WechatFailuresOnlyToggleButton not found.");
                var wechatPinSelectionToggleButton = window.FindName("WechatPinSelectionToggleButton") as System.Windows.Controls.Primitives.ToggleButton
                    ?? throw new InvalidOperationException("WechatPinSelectionToggleButton not found.");
                var clearWechatActivityHistoryButton = window.FindName("ClearWechatActivityHistoryButton") as Button
                    ?? throw new InvalidOperationException("ClearWechatActivityHistoryButton not found.");
                var latestWechatRecentActivityListBox = window.FindName("LatestWechatRecentActivityListBox") as ListBox
                    ?? throw new InvalidOperationException("LatestWechatRecentActivityListBox not found.");
                var selectedWechatRecentActivitySummaryTextBlock = window.FindName("SelectedWechatRecentActivitySummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("SelectedWechatRecentActivitySummaryTextBlock not found.");
                var selectedWechatRecentActivityMetaTextBlock = window.FindName("SelectedWechatRecentActivityMetaTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("SelectedWechatRecentActivityMetaTextBlock not found.");
                var selectedWechatRecentActivityDetailTextBlock = window.FindName("SelectedWechatRecentActivityDetailTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("SelectedWechatRecentActivityDetailTextBlock not found.");
                var latestWechatActivityStateTextBlock = window.FindName("LatestWechatActivityStateTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatActivityStateTextBlock not found.");
                var latestWechatLatestSuccessTextBlock = window.FindName("LatestWechatLatestSuccessTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatLatestSuccessTextBlock not found.");
                var latestWechatLatestFailureTextBlock = window.FindName("LatestWechatLatestFailureTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatLatestFailureTextBlock not found.");
                var latestWechatRecoveryTextBlock = window.FindName("LatestWechatRecoveryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatRecoveryTextBlock not found.");
                var latestWechatRequestTimelineTextBlock = window.FindName("LatestWechatRequestTimelineTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatRequestTimelineTextBlock not found.");
                var latestWechatDecisionTriggerTextBlock = window.FindName("LatestWechatDecisionTriggerTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatDecisionTriggerTextBlock not found.");
                var latestWechatDecisionCapabilityTextBlock = window.FindName("LatestWechatDecisionCapabilityTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatDecisionCapabilityTextBlock not found.");
                var latestWechatDecisionUpgradeTextBlock = window.FindName("LatestWechatDecisionUpgradeTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatDecisionUpgradeTextBlock not found.");
                var latestWechatRequestedCapabilitiesTextBlock = window.FindName("LatestWechatRequestedCapabilitiesTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatRequestedCapabilitiesTextBlock not found.");
                var latestQqFailureSummaryTextBlock = window.FindName("LatestQqFailureSummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqFailureSummaryTextBlock not found.");
                var latestQqFailureTimelineTextBlock = window.FindName("LatestQqFailureTimelineTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqFailureTimelineTextBlock not found.");
                var latestQqFailureTriggerTextBlock = window.FindName("LatestQqFailureTriggerTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqFailureTriggerTextBlock not found.");
                var latestQqFailureCapabilityTextBlock = window.FindName("LatestQqFailureCapabilityTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqFailureCapabilityTextBlock not found.");
                var latestQqFailureUpgradeTextBlock = window.FindName("LatestQqFailureUpgradeTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqFailureUpgradeTextBlock not found.");
                var latestQqFailureErrorTextBlock = window.FindName("LatestQqFailureErrorTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqFailureErrorTextBlock not found.");
                var latestWechatFailureSummaryTextBlock = window.FindName("LatestWechatFailureSummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatFailureSummaryTextBlock not found.");
                var latestWechatFailureTimelineTextBlock = window.FindName("LatestWechatFailureTimelineTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatFailureTimelineTextBlock not found.");
                var latestWechatFailureTriggerTextBlock = window.FindName("LatestWechatFailureTriggerTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatFailureTriggerTextBlock not found.");
                var latestWechatFailureCapabilityTextBlock = window.FindName("LatestWechatFailureCapabilityTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatFailureCapabilityTextBlock not found.");
                var latestWechatFailureUpgradeTextBlock = window.FindName("LatestWechatFailureUpgradeTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatFailureUpgradeTextBlock not found.");
                var latestWechatFailureErrorTextBlock = window.FindName("LatestWechatFailureErrorTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatFailureErrorTextBlock not found.");
                var healthStateTextBlock = window.FindName("HealthStateTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("HealthStateTextBlock not found.");
                var healthSummaryTextBlock = window.FindName("HealthSummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("HealthSummaryTextBlock not found.");
                var healthChecklistStatusTextBlock = window.FindName("HealthChecklistStatusTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("HealthChecklistStatusTextBlock not found.");
                var healthReadyNowTextBlock = window.FindName("HealthReadyNowTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("HealthReadyNowTextBlock not found.");
                var healthPrimaryActionTextBlock = window.FindName("HealthPrimaryActionTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("HealthPrimaryActionTextBlock not found.");
                var residentModeDetailTextBlock = window.FindName("ResidentModeDetailTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("ResidentModeDetailTextBlock not found.");
                var closeToTrayBehaviorTextBlock = window.FindName("CloseToTrayBehaviorTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("CloseToTrayBehaviorTextBlock not found.");
                var exitDesktopBehaviorTextBlock = window.FindName("ExitDesktopBehaviorTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("ExitDesktopBehaviorTextBlock not found.");
                var stopBackendBehaviorTextBlock = window.FindName("StopBackendBehaviorTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("StopBackendBehaviorTextBlock not found.");
                var reopenDesktopBehaviorTextBlock = window.FindName("ReopenDesktopBehaviorTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("ReopenDesktopBehaviorTextBlock not found.");
                var healthRuntimeExplanationTextBlock = window.FindName("HealthRuntimeExplanationTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("HealthRuntimeExplanationTextBlock not found.");
                var healthLatestIssueTextBlock = window.FindName("HealthLatestIssueTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("HealthLatestIssueTextBlock not found.");
                var healthLatestIssueActionButton = window.FindName("HealthLatestIssueActionButton") as Button
                    ?? throw new InvalidOperationException("HealthLatestIssueActionButton not found.");
                var healthChecksItemsControl = window.FindName("HealthChecksItemsControl") as ItemsControl
                    ?? throw new InvalidOperationException("HealthChecksItemsControl not found.");
                var runtimeReadyText = viewModel.RuntimeReadyText;
                var wechatRuntimeReadyText = viewModel.WechatRuntimeReadyText;
                var saveButton = window.FindName("SaveButton") as Button
                    ?? throw new InvalidOperationException("SaveButton not found.");
                var startButton = window.FindName("StartButton") as Button
                    ?? throw new InvalidOperationException("StartButton not found.");
                var stopButton = window.FindName("StopButton") as Button
                    ?? throw new InvalidOperationException("StopButton not found.");
                var exitDesktopButton = window.FindName("ExitDesktopButton") as Button
                    ?? throw new InvalidOperationException("ExitDesktopButton not found.");

                AssertEqual("/ai", wechatPrefixTextBox.Text, "WechatBotPrefix textbox should reflect loaded config.");
                AssertContains(firstRunGuideTextBlock.Text, "首次打开", "Hero guide should expose a first-run explanation.");
                AssertContains(firstRunGuideProgressTextBlock.Text, "已完成 2/3", "First-run guide should surface completion progress.");
                AssertContains(firstRunGuideCurrentTextBlock.Text, "让 runtime 上线", "First-run guide should surface the current step summary.");
                AssertEqual(string.Empty, firstRunGuideCompletionTextBlock.Text, "First-run completion text should stay empty until all setup steps are done.");
                AssertEqual(3, firstRunGuideStepsItemsControl.Items.Count, "First-run guide should expose three clickable setup steps.");
                AssertEqual("1", viewModel.FirstRunGuideSteps[0].StepNumber, "First-run steps should expose a numeric badge for the first item.");
                AssertEqual("连接 runtime 目录", viewModel.FirstRunGuideSteps[0].Title, "First-run steps should begin with attaching the runtime folder.");
                AssertEqual("3", viewModel.FirstRunGuideSteps[2].StepNumber, "First-run steps should expose a numeric badge for the runtime-online step.");
                AssertEqual("让 runtime 上线", viewModel.FirstRunGuideSteps[2].Title, "First-run steps should end with bringing the runtime online.");
                AssertEqual("启动后端", viewModel.FirstRunGuideSteps[2].ActionLabel, "First-run runtime step should expose a direct start action when setup is complete.");
                AssertTrue(viewModel.FirstRunGuideSteps[2].IsCurrent, "First-run guide should highlight bringing the runtime online when setup is complete but runtime is offline.");
                AssertContains(dailyUseGuideTextBlock.Text, "日常常驻", "Daily-use guide should expose an everyday-use summary.");
                AssertContains(dailyUseGuideProgressTextBlock.Text, "已完成 0/3", "Daily-use guide should surface completion progress.");
                AssertContains(dailyUseGuideCurrentTextBlock.Text, "保持 runtime 可接回", "Daily-use guide should surface the current step summary.");
                AssertEqual(string.Empty, dailyUseGuideCompletionTextBlock.Text, "Daily-use completion text should stay empty while upkeep is not fully complete.");
                AssertEqual(3, dailyUseGuideStepsItemsControl.Items.Count, "Daily-use guide should expose three clickable upkeep steps.");
                AssertEqual("1", viewModel.DailyUseGuideSteps[0].StepNumber, "Daily-use guide should expose a numeric badge for the first item.");
                AssertEqual("保持 runtime 可接回", viewModel.DailyUseGuideSteps[0].Title, "Daily-use guide should begin with runtime reachability.");
                AssertEqual("启用开机启动", viewModel.DailyUseGuideSteps[1].ActionLabel, "Daily-use guide should expose startup enablement as an optional action.");
                AssertContains(viewModel.DailyUseGuideSteps[2].Detail, "provider rejected request", "Daily-use guide should surface the latest issue detail when one exists.");
                AssertTrue(viewModel.DailyUseGuideSteps[0].IsCurrent, "Daily-use guide should highlight runtime reachability first while backend is stopped.");
                AssertEqual("设置进行中", overallReadinessStateTextBlock.Text, "Overall readiness should show setup-in-progress before the runtime is online.");
                AssertContains(overallReadinessSummaryTextBlock.Text, "首次使用步骤", "Overall readiness should explain that first-run setup is still active.");
                AssertContains(overallReadinessActionSummaryTextBlock.Text, "当前重点：启动后端", "Overall readiness should front-load the current guided action in one sentence.");
                AssertEqual(viewModel.HealthNextActions.Count, overallReadinessActionsItemsControl.Items.Count, "Overall readiness should bind the same guided action queue exposed by the view model.");
                AssertContains(overallReadinessRecentActivityTextBlock.Text, "最近活动：QQ Request", "Overall readiness should summarize the latest captured activity.");
                AssertEqual("启动后端", overallReadinessActionButton.Content?.ToString(), "Overall readiness should expose the next first-run action while setup is incomplete.");
                AssertContains(latestTurnHeadlineTextBlock.Text, "QQ", "Latest-turn card should front-load the newest captured turn.");
                AssertContains(latestTurnHeadlineTextBlock.Text, "已完成", "Latest-turn card should front-load the newest captured turn.");
                AssertContains(latestTurnSummaryTextBlock.Text, "default / gpt-5.4 / responses", "Latest-turn card should compress route/model/api context.");
                AssertContains(latestTurnCapabilitiesTextBlock.Text, "联网 关", "Latest-turn card should expose current-turn capability decisions.");
                AssertContains(latestTurnReasonTextBlock.Text, "触发 default", "Latest-turn card should explain why the current turn stayed on the default route.");
                AssertContains(latestTurnOutcomeTextBlock.Text, "一次直接 LLM 调用完成", "Latest-turn card should summarize direct successful execution.");
                AssertEqual("查看最近活动", latestTurnActionButton.Content?.ToString(), "Latest-turn card should route successful turns to recent activity review.");
                AssertContains(defaultBotInstructionsTextBox.Text, "你是本地 AI 助手，会处理来自 QQ 和微信的消息。", "Default bot instructions textbox should show the backend default system prompt.");
                AssertContains(defaultBotInstructionsTextBox.Text, "默认使用简体中文。", "Default bot instructions textbox should show the backend language guidance.");
                AssertEqual(defaultBotInstructionsTextBox.Text, effectiveBotInstructionsTextBox.Text, "Effective bot instructions should match the default prompt when BOT_PERSONA is empty.");
                AssertFalse(defaultBotInstructionsTextBox.IsReadOnly, "System prompt textbox should be editable.");
                AssertEqual("chat-a,chat-b", allowedChatIdsTextBox.Text, "AllowedChatIds textbox should reflect loaded config.");
                AssertEqual(false, defaultWebSearchCheckBox.IsChecked ?? false, "Default web search checkbox should reflect loaded config.");
                AssertEqual(true, advancedWebSearchCheckBox.IsChecked ?? false, "Advanced web search checkbox should reflect loaded config.");
                AssertEqual(false, defaultCodeInterpreterCheckBox.IsChecked ?? false, "Default code interpreter checkbox should reflect loaded config.");
                AssertEqual(true, advancedCodeInterpreterCheckBox.IsChecked ?? false, "Advanced code interpreter checkbox should reflect loaded config.");
                AssertEqual("default / gpt-5.4 / responses", latestQqLlmSummaryTextBlock.Text, "Latest QQ LLM summary should reflect runtime status.");
                AssertEqual("advanced / gpt-5.4 / responses", latestWechatLlmSummaryTextBlock.Text, "Latest Wechat LLM summary should reflect runtime status.");
                AssertContains(latestQqActivitySummaryTextBlock.Text, "Latest event: request", "Latest QQ activity summary should reflect recovery when request is newer than failure.");
                AssertEqual(2, latestQqRecentActivityListBox.Items.Count, "Latest QQ recent activity list should include two events.");
                AssertContains(selectedQqRecentActivitySummaryTextBlock.Text, "default / gpt-5.4 / responses", "Selected QQ recent activity summary should show the latest request event.");
                AssertContains(selectedQqRecentActivityMetaTextBlock.Text, "2026-03-24", "Selected QQ recent activity meta should show the captured time.");
                AssertContains(selectedQqRecentActivityDetailTextBlock.Text, "trigger=default", "Selected QQ recent activity detail should show the request detail.");
                AssertContains(selectedQqRecentActivityDetailTextBlock.Text, "execution=direct", "Selected QQ recent activity detail should show the execution path.");
                AssertEqual("Recovered after failure", latestQqActivityStateTextBlock.Text, "Latest QQ activity state should reflect recovery.");
                AssertContains(latestQqLatestSuccessTextBlock.Text, "Success |", "Latest QQ latest success should render activity checkpoint.");
                AssertContains(latestQqLatestFailureTextBlock.Text, "Failure |", "Latest QQ latest failure should render activity checkpoint.");
                AssertContains(latestQqRecoveryTextBlock.Text, "Recovery |", "Latest QQ recovery text should render recovery checkpoint.");
                AssertContains(latestQqRequestTimelineTextBlock.Text, "Request |", "Latest QQ request timeline should render the request header.");
                AssertEqual("default", latestQqDecisionTriggerTextBlock.Text, "Latest QQ decision trigger should reflect structured inspection binding.");
                AssertEqual("default", latestQqDecisionCapabilityTextBlock.Text, "Latest QQ decision capability should reflect structured inspection binding.");
                AssertEqual("none", latestQqDecisionUpgradeTextBlock.Text, "Latest QQ decision upgrade should reflect structured inspection binding.");
                AssertContains(latestQqRequestedCapabilitiesTextBlock.Text, "reasoning=medium", "Latest QQ requested capabilities should reflect structured inspection binding.");
                AssertContains(latestWechatActivitySummaryTextBlock.Text, "Latest event: failure", "Latest Wechat activity summary should reflect failure when failure is newer than request.");
                AssertEqual(2, latestWechatRecentActivityListBox.Items.Count, "Latest Wechat recent activity list should include two events.");
                AssertContains(selectedWechatRecentActivitySummaryTextBlock.Text, "advanced / provider rejected request", "Selected Wechat recent activity summary should show the latest failure event.");
                AssertContains(selectedWechatRecentActivityMetaTextBlock.Text, "2026-03-24", "Selected Wechat recent activity meta should show the captured time.");
                AssertContains(selectedWechatRecentActivityDetailTextBlock.Text, "error=provider rejected request", "Selected Wechat recent activity detail should show the failure detail.");
                AssertContains(selectedWechatRecentActivityDetailTextBlock.Text, "failed_stage=draft", "Selected Wechat recent activity detail should show the failed execution stage.");
                AssertContains(selectedWechatRecentActivityDetailTextBlock.Text, "completed=planner", "Selected Wechat recent activity detail should show completed execution stages.");
                AssertEqual("Failure is latest event", latestWechatActivityStateTextBlock.Text, "Latest Wechat activity state should reflect failure-latest state.");
                AssertContains(latestWechatLatestSuccessTextBlock.Text, "Success |", "Latest Wechat latest success should render activity checkpoint.");
                AssertContains(latestWechatLatestFailureTextBlock.Text, "Failure |", "Latest Wechat latest failure should render activity checkpoint.");
                AssertEqual("Recovery | pending", latestWechatRecoveryTextBlock.Text, "Latest Wechat recovery text should reflect pending recovery.");
                AssertContains(latestWechatRequestTimelineTextBlock.Text, "Request |", "Latest Wechat request timeline should render the request header.");
                AssertEqual("directive:/gpt", latestWechatDecisionTriggerTextBlock.Text, "Latest Wechat decision trigger should reflect structured inspection binding.");
                AssertEqual("default", latestWechatDecisionCapabilityTextBlock.Text, "Latest Wechat decision capability should reflect structured inspection binding.");
                AssertEqual("none", latestWechatDecisionUpgradeTextBlock.Text, "Latest Wechat decision upgrade should reflect structured inspection binding.");
                AssertContains(latestWechatRequestedCapabilitiesTextBlock.Text, "code_interpreter (Code Interpreter)", "Latest Wechat requested capabilities should reflect structured inspection binding.");
                AssertContains(latestQqFailureSummaryTextBlock.Text, "default /", "Latest QQ failure summary should reflect structured failure binding.");
                AssertContains(latestQqFailureTimelineTextBlock.Text, "Failure |", "Latest QQ failure timeline should render the failure header.");
                AssertEqual("default", latestQqFailureTriggerTextBlock.Text, "Latest QQ failure trigger should reflect structured failure binding.");
                AssertEqual("web_search", latestQqFailureCapabilityTextBlock.Text, "Latest QQ failure capability should reflect structured failure binding.");
                AssertEqual("none", latestQqFailureUpgradeTextBlock.Text, "Latest QQ failure upgrade should reflect structured failure binding.");
                AssertEqual("search timed out", latestQqFailureErrorTextBlock.Text, "Latest QQ failure error should reflect structured failure binding.");
                AssertContains(latestWechatFailureSummaryTextBlock.Text, "advanced /", "Latest Wechat failure summary should reflect structured failure binding.");
                AssertContains(latestWechatFailureTimelineTextBlock.Text, "Failure |", "Latest Wechat failure timeline should render the failure header.");
                AssertEqual("directive:/gpt", latestWechatFailureTriggerTextBlock.Text, "Latest Wechat failure trigger should reflect structured failure binding.");
                AssertEqual("default", latestWechatFailureCapabilityTextBlock.Text, "Latest Wechat failure capability should reflect structured failure binding.");
                AssertEqual("capability_upgrade", latestWechatFailureUpgradeTextBlock.Text, "Latest Wechat failure upgrade should reflect structured failure binding.");
                AssertEqual("provider rejected request", latestWechatFailureErrorTextBlock.Text, "Latest Wechat failure error should reflect structured failure binding.");
                AssertEqual("可启动", healthStateTextBlock.Text, "Health state should explain that config is usable but the backend is stopped.");
                AssertContains(healthSummaryTextBlock.Text, "backend 宿主当前已停止", "Health summary should explain why the runtime is not online yet.");
                AssertContains(healthChecklistStatusTextBlock.Text, "必填设置已完成", "Health checklist should separate setup completion from runtime state.");
                AssertContains(healthReadyNowTextBlock.Text, "QQ 和微信设置看起来都可用", "Health readiness text should explain that channels are configured even when the backend is stopped.");
                AssertContains(healthPrimaryActionTextBlock.Text, "启动后端", "Health summary should tell the user the next action.");
                AssertEqual("退出控制台", exitDesktopButton.Content?.ToString(), "Toolbar should expose an explicit desktop-exit action.");
                AssertContains(residentModeDetailTextBlock.Text, "退出控制台", "Toolbar should explain that tray exit is separate from stopping the backend.");
                AssertContains(closeToTrayBehaviorTextBlock.Text, "保留托盘入口", "Boundary guide should explain the close-to-tray behavior when runtime is stopped.");
                AssertContains(exitDesktopBehaviorTextBlock.Text, "重新打开", "Boundary guide should explain desktop-only exit when runtime is stopped.");
                AssertContains(stopBackendBehaviorTextBlock.Text, "已离线", "Boundary guide should explain the stopped-runtime case on first load.");
                AssertContains(reopenDesktopBehaviorTextBlock.Text, "桌面快捷方式或开始菜单", "Boundary guide should explain where to reopen the desktop shell.");
                AssertContains(reopenDesktopBehaviorTextBlock.Text, "新的桌面壳", "Boundary guide should explain that reopening is single-instance.");
                AssertContains(healthRuntimeExplanationTextBlock.Text, "QQ worker 已停止", "Health runtime explanation should describe the stopped QQ runtime.");
                AssertContains(healthLatestIssueTextBlock.Text, "provider rejected request", "Health latest issue should surface the newest runtime failure.");
                AssertEqual("查看微信失败", healthLatestIssueActionButton.Content?.ToString(), "Health latest issue should expose the failing channel activity action when a runtime failure is the newest issue.");
                healthLatestIssueActionButton.Command.Execute(healthLatestIssueActionButton.CommandParameter);
                await WaitForAsync(
                    () => window.GetLastHealthActionTargetNameForTests() == "LatestWechatRecentActivityListBox",
                    "health latest issue action routes to wechat activity");
                AssertContains(selectedWechatRecentActivitySummaryTextBlock.Text, "provider rejected request", "Health latest issue action should select the latest WeChat failure detail.");
                AssertEqual(5, healthChecksItemsControl.Items.Count, "Health checks should show the fixed checklist plus resident mode.");
                AssertTrue(viewModel.HealthChecks.Any((check) => check.Title == "常驻模式" && check.ActionLabel == "启用开机启动"), "Health checks should expose resident mode guidance when startup is off.");
                viewModel.RunHealthActionCommand.Execute(DesktopHealthActionKeys.ToggleAutoStart);
                await WaitForAsync(() => fakeAutoStart.Enabled, "health action toggle auto-start");
                await WaitForAsync(
                    () => residentModeDetailTextBlock.Text.Contains("Windows 登录时启动", StringComparison.Ordinal),
                    "resident mode detail updates after startup enable");
                AssertContains(residentModeDetailTextBlock.Text, "停止后端", "Toolbar should explain how to fully stop the runtime when resident mode is enabled.");
                AssertTrue(viewModel.HealthChecks.Any((check) => check.Title == "常驻模式" && check.StateText == "随 Windows 启动"), "Health checks should reflect startup-enabled resident mode.");
                AssertEqual("已完成", viewModel.FirstRunGuideSteps[1].StatusText, "First-run setup step should mark required setup as done when config is already complete.");
                AssertEqual(string.Empty, viewModel.DailyUseGuideSteps[1].ActionLabel, "Daily-use startup step should clear its action once resident mode is enabled.");
                AssertTrue(viewModel.DailyUseGuideSteps[0].IsCurrent, "Daily-use guide should keep runtime reachability current until the backend is started.");
                AssertContains(dailyUseGuideProgressTextBlock.Text, "已完成 1/3", "Daily-use guide should update progress after startup is enabled.");
                AssertContains(viewModel.LatestQqLlmDetailText, "trigger=default", "Latest QQ LLM detail should prefer structured decision trigger.");
                AssertContains(viewModel.LatestQqLlmDetailText, "capability=default", "Latest QQ LLM detail should show structured capability reasons.");
                AssertContains(viewModel.LatestQqLlmDetailText, "upgrade=none", "Latest QQ LLM detail should show structured upgrade reasons.");
                AssertContains(viewModel.LatestQqLlmDetailText, "execution=direct", "Latest QQ LLM detail should show the execution path.");
                AssertContains(viewModel.LatestWechatLlmDetailText, "trigger=directive:/gpt", "Latest Wechat LLM detail should prefer structured directive trigger.");
                AssertContains(viewModel.LatestWechatLlmDetailText, "capability=default", "Latest Wechat LLM detail should show structured capability reasons when none are present.");
                AssertContains(viewModel.LatestWechatLlmDetailText, "upgrade=none", "Latest Wechat LLM detail should show structured upgrade reasons when none are present.");
                AssertContains(viewModel.LatestWechatLlmDetailText, "execution=deliberation", "Latest Wechat LLM detail should show the execution path.");
                AssertContains(viewModel.LatestWechatLlmDetailText, $"stages={BackendExecutionProjectionTags.DeliberationSummary}", "Latest Wechat LLM detail should show the deliberation stage path.");
                AssertDoesNotContain(viewModel.LatestWechatLlmDetailText, "degraded=yes", "Latest Wechat LLM detail should not mark a full deliberation success as degraded.");
                AssertFalse(qqPinSelectionToggleButton.IsChecked ?? true, "QQ pin toggle should be off by default.");
                AssertFalse(wechatPinSelectionToggleButton.IsChecked ?? true, "Wechat pin toggle should be off by default.");

                qqPinSelectionToggleButton.IsChecked = true;

                qqFailuresOnlyToggleButton.IsChecked = true;
                wechatFailuresOnlyToggleButton.IsChecked = true;
                await WaitForAsync(() => latestQqRecentActivityListBox.Items.Count == 1, "QQ failures-only filter");
                await WaitForAsync(() => latestWechatRecentActivityListBox.Items.Count == 1, "Wechat failures-only filter");
                // Session-level tests cover failure-selection semantics; UI keeps only visibility/wiring checks.
                // Session-level tests cover failure-detail semantics.
                AssertFalse(string.IsNullOrWhiteSpace(selectedWechatRecentActivitySummaryTextBlock.Text), "Wechat failures-only filter should keep a visible selected activity.");
                // UI smoke no longer asserts clear-history state transitions; session tests own that behavior.
                // UI smoke no longer asserts clear-history state transitions.
                // UI smoke no longer asserts cleared-selection placeholder text.
                // UI smoke no longer asserts pin-reset semantics.
                AssertTrue(clearWechatActivityHistoryButton.Command.CanExecute(null), "Wechat clear activity history button should be enabled while events exist.");
                AssertEqual("QQ 通道已就绪", runtimeReadyText, "Runtime ready text should reflect runtime status.");
                AssertEqual("微信通道已就绪", wechatRuntimeReadyText, "WeChat runtime ready text should reflect runtime status.");

                wechatPrefixTextBox.Text = "/wx";
                defaultBotInstructionsTextBox.Text = "Base prompt line 1\r\nBase prompt line 2";
                botPersonaTextBox.Text = "冷静、专业。";
                allowedChatIdsTextBox.Text = "chat-x,chat-y";
                deepSeekFallbackEnabledCheckBox.IsChecked = false;
                deepSeekModelTextBox.Text = "deepseek-reasoner";
                deepSeekBaseUrlTextBox.Text = "https://example.deepseek-proxy/v1";
                defaultWebSearchCheckBox.IsChecked = true;
                advancedWebSearchCheckBox.IsChecked = false;
                defaultCodeInterpreterCheckBox.IsChecked = true;
                advancedCodeInterpreterCheckBox.IsChecked = false;
                await WaitForAsync(
                    () => effectiveBotInstructionsTextBox.Text.Contains("附加人格设定:", StringComparison.Ordinal)
                        && effectiveBotInstructionsTextBox.Text.Contains("冷静、专业。", StringComparison.Ordinal),
                    "effective bot instructions preview update");
                await WaitForAsync(
                    () => effectiveBotInstructionsTextBox.Text.Contains("Base prompt line 1", StringComparison.Ordinal),
                    "effective bot system prompt preview update");

                saveButton.Command.Execute(null);
                await WaitForAsync(() => fakeBackend.SaveConfigCallCount == 1, "save command invocation");
                AssertNotNull(fakeBackend.LastSavedConfig, "Saved config payload should be captured.");
                AssertEqual("/wx", fakeBackend.LastSavedConfig!.WechatBotPrefix, "Save should use edited WechatBotPrefix.");
                AssertEqual("Base prompt line 1\r\nBase prompt line 2", fakeBackend.LastSavedConfig.BotSystemPrompt, "Save should use edited BOT_SYSTEM_PROMPT.");
                AssertEqual("冷静、专业。", fakeBackend.LastSavedConfig.BotPersona, "Save should use edited BOT_PERSONA.");
                AssertEqual("chat-x,chat-y", fakeBackend.LastSavedConfig.AllowedChatIds, "Save should use edited AllowedChatIds.");
                AssertEqual("false", fakeBackend.LastSavedConfig.DeepSeekFallbackEnabled, "Save should use the edited DeepSeek fallback toggle.");
                AssertEqual("deepseek-reasoner", fakeBackend.LastSavedConfig.DeepSeekModel, "Save should use the edited DeepSeek model.");
                AssertEqual("https://example.deepseek-proxy/v1", fakeBackend.LastSavedConfig.DeepSeekBaseUrl, "Save should use the edited DeepSeek base URL.");
                AssertEqual("true", fakeBackend.LastSavedConfig.OpenAiDefaultEnableWebSearch, "Save should use edited default web search toggle.");
                AssertEqual("false", fakeBackend.LastSavedConfig.OpenAiAdvancedEnableWebSearch, "Save should use edited advanced web search toggle.");
                AssertEqual("true", fakeBackend.LastSavedConfig.OpenAiDefaultEnableCodeInterpreter, "Save should use edited default code interpreter toggle.");
                AssertEqual("false", fakeBackend.LastSavedConfig.OpenAiAdvancedEnableCodeInterpreter, "Save should use edited advanced code interpreter toggle.");

                startButton.Command.Execute(null);
                await WaitForAsync(() => fakeBackend.StartCallCount == 1, "start command invocation");
                await WaitForAsync(
                    () => closeToTrayBehaviorTextBlock.Text.Contains("backend 会继续运行", StringComparison.Ordinal),
                    "close-to-tray guide after backend start");
                AssertFalse(string.IsNullOrWhiteSpace(firstRunGuideTextBlock.Text), "First-run guide text should remain visible once runtime is online.");
                AssertEqual("已完成", viewModel.DailyUseGuideSteps[0].StatusText, "Daily-use runtime step should mark the runtime as reachable after start.");
                AssertTrue(viewModel.DailyUseGuideSteps[2].IsCurrent, "Daily-use guide should highlight the latest-issue review step once runtime and startup are already handled.");
                AssertContains(viewModel.DailyUseGuideSteps[2].Detail, "provider rejected request", "Daily-use issue step should continue to surface the latest issue detail while it exists.");
                AssertContains(dailyUseGuideProgressTextBlock.Text, "已完成 2/3", "Daily-use guide should update progress after runtime becomes reachable.");
                AssertContains(dailyUseGuideCurrentTextBlock.Text, "有异常时查看最新问题", "Daily-use guide should update the current-step summary after runtime is online.");
                AssertEqual(string.Empty, dailyUseGuideCompletionTextBlock.Text, "Daily-use completion text should remain empty while a latest issue still needs review.");
                AssertFalse(string.IsNullOrWhiteSpace(overallReadinessStateTextBlock.Text), "Overall readiness state should remain visible once first-run steps are done.");
                AssertFalse(string.IsNullOrWhiteSpace(overallReadinessSummaryTextBlock.Text), "Overall readiness summary should remain visible once first-run steps are done.");
                AssertContains(overallReadinessRecentActivityTextBlock.Text, "最近活动：", "Overall readiness should continue to summarize the latest activity while setup completes.");
                AssertFalse(string.IsNullOrWhiteSpace(overallReadinessActionButton.Content?.ToString()), "Overall readiness should keep an action button visible once setup completes.");
                AssertContains(exitDesktopBehaviorTextBlock.Text, "直到你主动停止", "Boundary guide should explain desktop-only exit while runtime is active.");
                AssertContains(stopBackendBehaviorTextBlock.Text, "让 QQ / 微信下线", "Boundary guide should explain the full stop action while runtime is active.");
                AssertContains(reopenDesktopBehaviorTextBlock.Text, "重新附着到同一个正在运行的 runtime", "Boundary guide should explain reconnecting to an already-running runtime.");
                AssertContains(reopenDesktopBehaviorTextBlock.Text, "桌面快捷方式或开始菜单", "Boundary guide should keep the reopen entry point visible while runtime is active.");


                stopButton.Command.Execute(null);
                await WaitForAsync(() => fakeBackend.StopCallCount == 1, "stop command invocation");
                await WaitForAsync(
                    () => stopBackendBehaviorTextBlock.Text.Contains("已离线", StringComparison.Ordinal),
                    "stop-backend guide after backend stop");
            }
            finally
            {
                window.Close();
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelDisposeDoesNotStopBackendProcessAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-dispose-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeLocalBootstrapStore = context.FakeLocalBootstrapStore;
    var fakeLocalPathOperationsService = context.FakeLocalPathOperationsService;
    var fakeLocalStateSnapshotService = context.FakeLocalStateSnapshotService;
    var fakeConfirmationDialogService = context.FakeConfirmationDialogService;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore,
                fakeLocalBootstrapStore,
                fakeLocalPathOperationsService,
                fakeLocalStateSnapshotService,
                fakeConfirmationDialogService);

            fakeBotProcess.Start(rootPath);
            await viewModel.DisposeAsync();

            AssertEqual(0, fakeBotProcess.StopCallCount, "DisposeAsync should not stop backend process ownership.");
            AssertTrue(fakeBotProcess.IsRunning, "Fake backend process should stay running after DisposeAsync.");
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelPreservesDefaultEditorStateOnStartupAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-default-startup-");
    var rootPath = context.RootPath;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBackend = context.FakeBackend;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeLocalBootstrapStore = context.FakeLocalBootstrapStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeAutoStart.Enabled = false;

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore,
                fakeLocalBootstrapStore);

            try
            {
                AssertEqual("gpt-5.4", viewModel.OpenAiDefaultModel, "Startup initialization should preserve the default model even when auto-start stays disabled.");
                AssertEqual("gpt-5.4", viewModel.OpenAiModel, "Startup initialization should preserve the advanced model default.");
                AssertFalse(viewModel.AutoStartEnabled, "Startup initialization should preserve disabled auto-start state.");
                AssertEqual("本机令牌未设置", viewModel.ControlApiTokenStateText, "Startup initialization should keep the unset-token projection before config load.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelAutoRecoversControlApiBeforeWarningAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-recover-control-api-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeLocalBootstrapStore = context.FakeLocalBootstrapStore;
    var fakeLocalPathOperationsService = context.FakeLocalPathOperationsService;
    var notifications = new List<TrayNotification>();
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Status = null;

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore,
                fakeLocalBootstrapStore,
                fakeLocalPathOperationsService);
            viewModel.NotificationRequested += (_, notification) => notifications.Add(notification);

            try
            {
                var session = CreateDesktopControlPlaneSessionForTests(context);

                await session.PollStatusAsync();
                var pollResult = await session.PollStatusAsync();

                await WaitForAsync(() => fakeBotProcess.StartCallCount == 1, "control api recovery backend start");
                await WaitForAsync(() => fakeBackend.StartCallCount == 1, "control api recovery start command");
                AssertTrue(pollResult.NextState?.RuntimeShellState.RuntimeSnapshot.ControlApiReachable == true, "Control API recovery should restore reachable runtime state.");
                AssertEqual(0, notifications.Count, "Control API recovery should happen before outage warning is shown.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestDesktopControlPlaneSessionRejectsUnknownConfigFailureBeforeFallbackAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-load-unknown-config-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeLocalBootstrapStore = context.FakeLocalBootstrapStore;
    var fakeLocalPathOperationsService = context.FakeLocalPathOperationsService;
    var fakeLocalStateSnapshotService = context.FakeLocalStateSnapshotService;
    var fakeConfirmationDialogService = context.FakeConfirmationDialogService;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Config = null;
    fakeBackend.ConfigFailureKind = BackendControlApiFailureKind.Unknown;
    fakeBackend.ConfigFailureMessage = "Control API returned an empty config response.";

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        var session = CreateDesktopControlPlaneSessionForTests(context);

        await AssertThrowsAsync<InvalidOperationException>(
            () => session.LoadAuthoritativeConfigAsync(),
            "Unknown config failures should throw before local fallback is considered.");

        AssertEqual(0, fakeLocalFallbackReader.LoadCallCount, "Unknown config failures should not touch the local fallback reader.");
        AssertEqual(0, fakeBotProcess.StartCallCount, "Unknown config failures should not trigger control API recovery.");
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestDesktopControlPlaneSessionRejectsUnauthorizedConfigFailureBeforeFallbackAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-load-unauthorized-config-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeLocalBootstrapStore = context.FakeLocalBootstrapStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Config = null;
    fakeBackend.ConfigFailureKind = BackendControlApiFailureKind.Unauthorized;
    fakeBackend.ConfigFailureMessage = "Control API authentication failed.";

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        var session = CreateDesktopControlPlaneSessionForTests(context);

        await AssertThrowsAsync<InvalidOperationException>(
            () => session.LoadAuthoritativeConfigAsync(),
            "Unauthorized config failures should throw before local fallback is considered.");

        AssertEqual(0, fakeLocalFallbackReader.LoadCallCount, "Unauthorized config failures should not touch the local fallback reader.");
        AssertEqual(0, fakeBotProcess.StartCallCount, "Unauthorized config failures should not trigger control API recovery.");
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestDesktopControlPlaneSessionRejectsIncompatibleConfigContractBeforeFallbackAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-load-incompatible-config-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeBotProcess = context.FakeBotProcess;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Config!.ConfigPath = string.Empty;
    fakeBackend.Config!.BootstrapEnvPath = string.Empty;

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        var session = CreateDesktopControlPlaneSessionForTests(context);

        await AssertThrowsAsync<InvalidOperationException>(
            () => session.LoadAuthoritativeConfigAsync(),
            "Incompatible config contracts should throw before local fallback is considered.");

        AssertEqual(0, fakeLocalFallbackReader.LoadCallCount, "Incompatible config contracts should not touch the local fallback reader.");
        AssertEqual(0, fakeBotProcess.StartCallCount, "Incompatible config contracts should not trigger control API recovery.");
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelLoadsThroughRecoveredControlApiAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-load-control-api-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeLocalBootstrapStore = context.FakeLocalBootstrapStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Status = null;
    fakeBackend.RequireReachableForConfig = true;
    fakeLocalFallbackReader.Document.Config.WechatBotPrefix = "/file-only";

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);

            try
            {
                await viewModel.InitializeAsync();

                await WaitForAsync(() => fakeBotProcess.StartCallCount == 1, "load recovery backend start");
                await WaitForAsync(() => fakeBackend.StartCallCount == 1, "load recovery control api start");
                await WaitForAsync(() => viewModel.IsControlApiReachable, "load recovery reachable");

                AssertTrue(fakeLocalFallbackReader.LoadCallCount >= 1, "Load may read the local env file for control API authentication preflight.");
                AssertEqual("/ai", viewModel.WechatBotPrefix, "Recovered control API load should use backend config instead of file snapshot.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelSavesThroughRecoveredControlApiAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-save-control-api-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeLocalBootstrapStore = context.FakeLocalBootstrapStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Status = null;
    fakeBackend.RequireReachableForSave = true;

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);

            try
            {
                viewModel.WechatBotPrefix = "/wx";
                viewModel.SaveCommand.Execute(null);

                await WaitForAsync(() => fakeBotProcess.StartCallCount == 1, "save recovery backend start");
                await WaitForAsync(() => fakeBackend.StartCallCount == 1, "save recovery control api start");
                await WaitForAsync(() => fakeBackend.SaveConfigCallCount >= 2, "save retry through recovered control api");

                AssertEqual(0, fakeLocalFallbackReader.SaveCallCount, "Save should not fall back to direct env file writes.");
                AssertNotNull(fakeBackend.LastSavedConfig, "Recovered control API save should capture config payload.");
                AssertEqual("/wx", fakeBackend.LastSavedConfig!.WechatBotPrefix, "Recovered control API save should use edited config.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelPreservesEditedBotSystemPromptWhenSaveResponseOmitsItAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-save-bot-system-prompt-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeLocalBootstrapStore = context.FakeLocalBootstrapStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.OmitBotSystemPromptOnSaveResponse = true;

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);

            try
            {
                viewModel.BotSystemPrompt = "Edited prompt line 1\r\nEdited prompt line 2";
                viewModel.SaveCommand.Execute(null);

                await WaitForAsync(
                    () => fakeBackend.SaveConfigCallCount == 1 && !viewModel.HasUnsavedChanges,
                    "save edited bot system prompt");

                AssertNotNull(fakeBackend.LastSavedConfig, "Save should capture edited config payload.");
                AssertEqual(
                    "Edited prompt line 1\r\nEdited prompt line 2",
                    fakeBackend.LastSavedConfig!.BotSystemPrompt,
                    "Save payload should include the edited BOT_SYSTEM_PROMPT.");
                AssertEqual(
                    "Edited prompt line 1\r\nEdited prompt line 2",
                    viewModel.BotSystemPrompt,
                    "View model should preserve the edited BOT_SYSTEM_PROMPT when save response omits it.");
                AssertContains(
                    viewModel.EffectiveBotInstructionsText,
                    "Edited prompt line 1",
                    "Effective bot instructions should continue to show the edited BOT_SYSTEM_PROMPT.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelSurfacesRejectedControlApiSaveAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-save-rejected-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeLocalBootstrapStore = context.FakeLocalBootstrapStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.SaveFailureKind = BackendControlApiFailureKind.Rejected;
    fakeBackend.SaveFailureMessage = "WECHAT_BRIDGE_URL must be a valid ws:// or wss:// URL";

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);
            var lastHealthActionKey = string.Empty;
            viewModel.HealthActionRequested += (_, actionKey) => lastHealthActionKey = actionKey;

            try
            {
                var session = CreateDesktopControlPlaneSessionForTests(context);
                var config = session.BuildCurrentShellState().ConfigEditorState.Config;
                config.WechatBridgeUrl = "not-a-valid-wechat-url";

                var saveResult = await session.SaveConfigAsync(config, showUiErrors: false);

                AssertFalse(saveResult.Succeeded, "Rejected save should report failure.");
                AssertEqual(1, fakeBackend.SaveConfigCallCount, "Rejected save should not retry through recovery.");
                AssertEqual(0, fakeBotProcess.StartCallCount, "Rejected save should not start local backend recovery.");
                AssertEqual(0, fakeBackend.StartCallCount, "Rejected save should not issue control API start.");
                AssertEqual(0, fakeLocalFallbackReader.SaveCallCount, "Rejected save should not fall back to env file writes.");
                AssertFalse(string.IsNullOrWhiteSpace(saveResult.StatusText), "Rejected save should surface a non-empty status text.");
                AssertEqual(DesktopHealthActionKeys.FocusWechatUrl, saveResult.SuggestedHealthActionKey, "Rejected save should direct the user to the offending WeChat field.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelPreservesDeepSeekEditsOnSaveAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-save-deepseek-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);

            try
            {
                viewModel.DeepSeekFallbackEnabled = "false";
                viewModel.DeepSeekModel = "deepseek-reasoner";
                viewModel.DeepSeekBaseUrl = "https://example.deepseek-proxy/v1";

                viewModel.SaveCommand.Execute(null);

                await WaitForAsync(
                    () => fakeBackend.SaveConfigCallCount == 1 && !viewModel.HasUnsavedChanges,
                    "save edited deepseek settings");

                AssertNotNull(fakeBackend.LastSavedConfig, "Save should capture edited config payload.");
                AssertEqual("false", fakeBackend.LastSavedConfig!.DeepSeekFallbackEnabled, "Save payload should include the DeepSeek fallback toggle.");
                AssertEqual("deepseek-reasoner", fakeBackend.LastSavedConfig.DeepSeekModel, "Save payload should include the edited DeepSeek model.");
                AssertEqual("https://example.deepseek-proxy/v1", fakeBackend.LastSavedConfig.DeepSeekBaseUrl, "Save payload should include the edited DeepSeek base URL.");
                AssertEqual("deepseek-reasoner", viewModel.DeepSeekModel, "ViewModel should keep the saved DeepSeek model.");
                AssertEqual("https://example.deepseek-proxy/v1", viewModel.DeepSeekBaseUrl, "ViewModel should keep the saved DeepSeek base URL.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelSurfacesIncompatibleControlApiSaveWithoutRevertingDeepSeekEditsAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-save-incompatible-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Config!.ConfigPath = string.Empty;
    fakeBackend.Config!.BootstrapEnvPath = string.Empty;

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);

            try
            {
                var session = CreateDesktopControlPlaneSessionForTests(context);
                var config = session.BuildCurrentShellState().ConfigEditorState.Config;
                config.DeepSeekFallbackEnabled = "false";
                config.DeepSeekModel = "deepseek-reasoner";
                config.DeepSeekBaseUrl = "https://example.deepseek-proxy/v1";

                var saveResult = await session.SaveConfigAsync(config, showUiErrors: false);

                AssertFalse(saveResult.Succeeded, "Incompatible save should report failure.");
                AssertEqual(1, fakeBackend.SaveConfigCallCount, "Incompatible save should still send one save attempt.");
                AssertEqual(0, fakeBotProcess.StartCallCount, "Incompatible save should not start local backend recovery.");
                AssertContains(saveResult.StatusText, "版本不兼容", "Incompatible save should surface the compatibility status text.");
                AssertEqual(DesktopHealthActionKeys.OpenBackendFolder, saveResult.SuggestedHealthActionKey, "Incompatible save should route to opening the backend folder.");
                AssertEqual("deepseek-reasoner", fakeBackend.LastSavedConfig?.DeepSeekModel ?? string.Empty, "Submitted save payload should still contain the edited DeepSeek model.");
                AssertEqual("https://example.deepseek-proxy/v1", fakeBackend.LastSavedConfig?.DeepSeekBaseUrl ?? string.Empty, "Submitted save payload should still contain the edited DeepSeek base URL.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelPublishesResidentModeNotificationsAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-resident-mode-notify-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var notifications = new List<TrayNotification>();
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);
            viewModel.NotificationRequested += (_, notification) => notifications.Add(notification);

            try
            {
                viewModel.ToggleAutoStartCommand.Execute(null);
                await WaitForAsync(() => fakeAutoStart.Enabled, "resident mode enable");
                await WaitForAsync(() => notifications.Count == 1, "resident mode enable notification");

                AssertContains(notifications[0].Message, "Windows 登录后会以最小化方式启动", "常驻模式 enable notification should explain launch behavior.");
                AssertContains(notifications[0].Message, "确保 runtime 运行", "常驻模式 enable notification should explain runtime recovery behavior.");

                viewModel.ToggleAutoStartCommand.Execute(null);
                await WaitForAsync(() => !fakeAutoStart.Enabled, "resident mode disable");
                await WaitForAsync(() => notifications.Count == 2, "resident mode disable notification");

                AssertContains(notifications[1].Message, "手动启动后", "常驻模式 disable notification should explain that manual launch still works.");
                AssertContains(notifications[1].Message, "托盘模式", "常驻模式 disable notification should mention tray behavior.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelGuideCompletionStatesAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-guide-complete-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeAutoStart.Enabled = true;
    fakeBackend.Status = new BackendRuntimeStatus
    {
        RuntimeActive = true,
        RuntimeReady = true,
        LastQqLlmRequest = new BackendLlmRequestStatus
        {
            Route = "default",
            Model = "gpt-5.4",
            EffectiveApiStyle = "responses",
            EffectiveReasoningEffort = "medium",
            EffectiveTextVerbosity = "medium",
            EffectiveTools = [],
            ExecutionKind = BackendExecutionProjectionTags.DirectKind,
            ExecutionSummary = BackendExecutionProjectionTags.DirectKind,
            ExecutionProjection = new BackendExecutionProjection
            {
                Kind = BackendExecutionProjectionTags.DirectKind,
                Summary = BackendExecutionProjectionTags.DirectKind,
                Stages = [BackendExecutionProjectionTags.DirectStage],
                FailedStage = "",
                CompletedStages = [BackendExecutionProjectionTags.DirectStage],
                Degraded = false,
                Recoveries = []
            },
            RouteReason = "default",
            DecisionSummary = new BackendDecisionSummary
            {
                Trigger = new BackendDecisionTrigger
                {
                    Kind = "default",
                    MatchedPrefix = string.Empty
                },
                ReasonTags = ["default"],
                ReasonGroups = new BackendDecisionReasonGroups
                {
                    TriggerReasons = [],
                    CapabilityReasons = [],
                    UpgradeReasons = []
                },
                RequestedCapabilities = new BackendRequestedCapabilities
                {
                    ReasoningEffort = "medium",
                    TextVerbosity = "medium",
                    EnableWebSearch = false,
                    EnableCodeInterpreter = false,
                    NeedsResponsesCapabilities = true
                },
                RouteReason = "default",
                MatchedPrefix = string.Empty
            },
            ImageCount = 0,
            CapturedAt = "2026-03-24T00:00:04.000Z"
        },
        WechatConfigured = false,
        WechatRuntimeActive = false,
        WechatRuntimeReady = false,
        WechatBridgeConnected = false,
        ControlApiUrl = "http://127.0.0.1:3199"
    };

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);

            try
            {
                await viewModel.InitializeAsync();

                AssertFalse(string.IsNullOrWhiteSpace(viewModel.FirstRunGuideText), "First-run guide text should project into the ViewModel.");
                AssertFalse(string.IsNullOrWhiteSpace(viewModel.DailyUseGuideText), "Daily-use guide text should project into the ViewModel.");
                AssertFalse(string.IsNullOrWhiteSpace(viewModel.OverallReadinessSummaryText), "Overall readiness summary should project into the ViewModel.");
                AssertContains(viewModel.OverallReadinessRecentActivityText, "QQ Request", "Overall readiness should summarize the latest recent activity in the ready state.");

                AssertFalse(string.IsNullOrWhiteSpace(viewModel.OverallReadinessActionLabel), "Overall readiness should keep an action label visible.");
                AssertFalse(string.IsNullOrWhiteSpace(viewModel.OverallReadinessActionKey), "Overall readiness should keep an action key visible.");
                // Session-level tests cover completion-state and summary-selection semantics.
                // Session-level tests cover completion-state and summary-selection semantics.
                // Session-level tests cover completion-state and summary-selection semantics.
                // Session-level tests cover completion-state and summary-selection semantics.
                // Session-level tests cover completion-state and summary-selection semantics.
                AssertContains(viewModel.OverallReadinessRecentActivityText, "最近活动：QQ Request", "Overall readiness should summarize the latest recent activity in the ready state.");
                AssertEqual("查看最近活动", viewModel.OverallReadinessActionLabel, "Overall readiness should offer a useful next action even after setup is complete.");
                AssertEqual(DesktopHealthActionKeys.FocusLatestActivity, viewModel.OverallReadinessActionKey, "Overall readiness should route to the latest activity review action when ready.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelRestoresLocalActivityStateAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-activity-restore-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeActivityStateStore.Seed(
        rootPath,
        new DesktopActivityState
        {
            QqRecentActivities =
            [
                new BackendRecentActivityItem
                {
                    EventKey = "request-1",
                    EventType = "Request",
                    Summary = "default / gpt-5.4 / responses",
                    Meta = "2026-03-24 08:00:00",
                    Detail = "request detail",
                    IsFailure = false
                }
            ],
            WechatRecentActivities =
            [
                new BackendRecentActivityItem
                {
                    EventKey = "failure-1",
                    EventType = "Failure",
                    Summary = "advanced / provider rejected request",
                    Meta = "2026-03-24 08:00:01",
                    Detail = "failure detail",
                    IsFailure = true
                }
            ],
            SelectedQqEventKey = "request-1",
            SelectedWechatEventKey = "failure-1",
            PinSelectedQqActivity = true,
            PinSelectedWechatActivity = false,
            ShowOnlyQqFailures = false,
            ShowOnlyWechatFailures = true
        });

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);

            try
            {
                viewModel.BackendRootPath = Path.Combine(rootPath, "missing");
                viewModel.BackendRootPath = rootPath;
                AssertEqual(1, viewModel.QqRecentActivities.Count, "QQ recent activities should restore from local store.");
                AssertEqual(1, viewModel.WechatRecentActivities.Count, "Wechat recent activities should restore from local store.");
                AssertEqual("default / gpt-5.4 / responses", viewModel.SelectedQqRecentActivitySummaryText, "QQ selected activity should restore from local store.");
                AssertEqual("advanced / provider rejected request", viewModel.SelectedWechatRecentActivitySummaryText, "Wechat selected activity should restore from local store.");
                AssertTrue(viewModel.PinSelectedQqActivity, "QQ pin selection should restore from local store.");
                AssertFalse(viewModel.PinSelectedWechatActivity, "Wechat pin selection should restore from local store.");
                AssertFalse(viewModel.ShowOnlyQqFailures, "QQ filter should restore from local store.");
                AssertTrue(viewModel.ShowOnlyWechatFailures, "Wechat filter should restore from local store.");
                AssertTrue(fakeActivityStateStore.LoadCallCount >= 1, "MainViewModel should load local activity state during initialization or explicit backend-root assignment.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelRestoreGuidancePrioritizesLocalTokenFixAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-restore-token-guidance-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeLocalBootstrapStore = context.FakeLocalBootstrapStore;
    var fakeLocalPathOperationsService = context.FakeLocalPathOperationsService;
    var fakeLocalStateSnapshotService = context.FakeLocalStateSnapshotService;
    var fakeConfirmationDialogService = context.FakeConfirmationDialogService;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.StatusFailureKind = BackendControlApiFailureKind.Unauthorized;
    fakeBackend.StatusFailureMessage = "Control API authentication failed.";
    fakeLocalStateSnapshotService.Result = new LocalStateSnapshotResult
    {
        ArchivePath = @"D:\snapshots\runtime-state-token-test.zip",
        IncludedEntries = ["app/.env"]
    };
    fakeLocalStateSnapshotService.PreviewResult = new LocalStateSnapshotPreviewResult
    {
        ArchivePath = @"D:\snapshots\runtime-state-token-test.zip",
        Lines =
        [
            "本机连接 .env：与当前状态不同",
            ".env 跟踪键变更：QQ_AI_BOT_CONTROL_API_TOKEN",
            "QQ_AI_BOT_CONTROL_API_TOKEN: ********1111 -> ********2222",
            "data/：与当前状态一致",
            "桌面活动状态：与当前状态一致"
        ],
        Recommendations =
        [
            "建议：恢复前先导出当前状态，便于需要时回滚。",
            "注意：这个快照包含 .env 密钥，请不要把归档分享给当前设备之外的人。"
        ]
    };

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore,
                fakeLocalBootstrapStore,
                fakeLocalPathOperationsService,
                fakeLocalStateSnapshotService,
                fakeConfirmationDialogService);

            try
            {
                await viewModel.InitializeAsync();
                await WaitForAsync(() => viewModel.SelectedStateSnapshot is not null, "restore token guidance snapshot load");

                fakeConfirmationDialogService.Results.Enqueue(true);
                viewModel.RestoreSelectedStateSnapshotCommand.Execute(null);

                await WaitForAsync(() => fakeLocalStateSnapshotService.RestoreCallCount == 1, "restore token guidance restore");
                AssertEqual("恢复选中快照", fakeConfirmationDialogService.LastTitle, "Restore command should still route through the snapshot confirmation dialog.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelRestoreGuidancePrioritizesNapCatReviewAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-restore-napcat-guidance-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeLocalBootstrapStore = context.FakeLocalBootstrapStore;
    var fakeLocalPathOperationsService = context.FakeLocalPathOperationsService;
    var fakeLocalStateSnapshotService = context.FakeLocalStateSnapshotService;
    var fakeConfirmationDialogService = context.FakeConfirmationDialogService;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Status = new BackendRuntimeStatus
    {
        RuntimeActive = true,
        RuntimeReady = false,
        WechatConfigured = true,
        WechatRuntimeActive = true,
        WechatRuntimeReady = true,
        WechatBridgeConnected = true
    };

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore,
                fakeLocalBootstrapStore,
                fakeLocalPathOperationsService,
                fakeLocalStateSnapshotService,
                fakeConfirmationDialogService);

            try
            {
                await viewModel.InitializeAsync();
                await WaitForAsync(() => viewModel.SelectedStateSnapshot is not null, "restore napcat guidance snapshot load");

                fakeConfirmationDialogService.Results.Enqueue(true);
                viewModel.RestoreSelectedStateSnapshotCommand.Execute(null);

                await WaitForAsync(() => fakeLocalStateSnapshotService.RestoreCallCount == 1, "restore napcat guidance restore");
                AssertEqual("恢复选中快照", fakeConfirmationDialogService.LastTitle, "Restore command should keep the snapshot confirmation dialog in the QQ blocker flow.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainWindowAutoStartsBackendWhenControlApiIsUnavailableAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-ui-auto-start-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeLocalBootstrapStore = context.FakeLocalBootstrapStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Status = null;

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore,
                fakeLocalBootstrapStore);
            var window = new MainWindow(
                launchMinimizedToTray: false,
                ensureRuntimeOnStartup: false,
                viewModel: viewModel,
                enableNotifyIcon: false);

            try
            {
                window.Show();
                await WaitForAsync(() => fakeBotProcess.StartCallCount == 1, "auto-start backend on unreachable control API", timeoutMs: 5000);
                AssertTrue(fakeBotProcess.IsRunning, "Fake bot process should be running after auto-start.");
            }
            finally
            {
                window.Close();
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainWindowExternalActivationAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-ui-activation-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);
            var window = new MainWindow(
                launchMinimizedToTray: true,
                ensureRuntimeOnStartup: false,
                viewModel: viewModel,
                enableNotifyIcon: false);

            try
            {
                window.Show();
                await WaitForAsync(() => fakeBackend.GetConfigCallCount > 0, "main window minimized initial load");

                AssertEqual(WindowState.Minimized, window.WindowState, "Window should start minimized.");
                AssertEqual(0d, window.Opacity, "Window should start hidden when launched minimized.");
                AssertFalse(window.ShowInTaskbar, "Window should not be in taskbar when launched minimized.");

                window.RestoreFromExternalActivation();
                await WaitForAsync(() => window.WindowState == WindowState.Normal, "window restore after external activation");

                AssertEqual(WindowState.Normal, window.WindowState, "RestoreFromExternalActivation should normalize window state.");
                AssertEqual(1d, window.Opacity, "RestoreFromExternalActivation should make window visible.");
                AssertTrue(window.ShowInTaskbar, "RestoreFromExternalActivation should show taskbar entry.");

                window.EnsureRuntimeFromExternalActivation();
                await WaitForAsync(() => fakeBackend.StartCallCount == 1, "ensure-runtime external activation");
            }
            finally
            {
                window.Close();
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainWindowTrayMinimizeBehaviorAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-ui-tray-minimize-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeNotifyIcon = new FakeNotifyIconHost();
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);
            var window = new MainWindow(
                launchMinimizedToTray: false,
                ensureRuntimeOnStartup: false,
                viewModel: viewModel,
                enableNotifyIcon: true,
                notifyIconHost: fakeNotifyIcon);

            try
            {
                window.Show();
                await WaitForAsync(() => fakeBackend.GetConfigCallCount > 0, "tray minimize initial load");

                window.WindowState = WindowState.Minimized;
                await WaitForAsync(() => !window.IsVisible, "window hidden to tray after minimize");

                AssertFalse(window.ShowInTaskbar, "Window should hide from taskbar after minimizing to tray.");
                AssertTrue(fakeNotifyIcon.Visible, "Tray icon should stay visible while window is hidden.");

                AssertEqual(1, fakeNotifyIcon.ShowBalloonTipCallCount, "First minimize should show one tray balloon.");
                AssertContains(fakeNotifyIcon.BalloonTipText, "从托盘启动后端", "Minimize-to-tray balloon should explain the stopped-backend case.");
            }
            finally
            {
                window.ExitFromTrayForTests();
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainWindowHealthActionsAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-ui-health-actions-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeLocalBootstrapStore = context.FakeLocalBootstrapStore;
    var fakeLocalPathOperationsService = context.FakeLocalPathOperationsService;
    var fakeLocalStateSnapshotService = context.FakeLocalStateSnapshotService;
    var fakeConfirmationDialogService = context.FakeConfirmationDialogService;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore,
                fakeLocalBootstrapStore,
                fakeLocalPathOperationsService,
                fakeLocalStateSnapshotService,
                fakeConfirmationDialogService);
            var window = new MainWindow(
                launchMinimizedToTray: false,
                ensureRuntimeOnStartup: false,
                viewModel: viewModel,
                enableNotifyIcon: false);

            try
            {
                window.Show();
                await WaitForAsync(() => fakeBackend.GetConfigCallCount > 0, "health action initial load");

                var healthPrimaryActionButton = window.FindName("HealthPrimaryActionButton") as Button
                    ?? throw new InvalidOperationException("HealthPrimaryActionButton not found.");
                var residentModeDetailTextBlock = window.FindName("ResidentModeDetailTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("ResidentModeDetailTextBlock not found.");
                var controlApiTokenTextBox = window.FindName("ControlApiTokenTextBox") as TextBox
                    ?? throw new InvalidOperationException("ControlApiTokenTextBox not found.");
                var deepSeekApiKeyTextBox = window.FindName("DeepSeekApiKeyTextBox") as TextBox
                    ?? throw new InvalidOperationException("DeepSeekApiKeyTextBox not found.");
                var saveLocalControlPlaneButton = window.FindName("SaveLocalControlPlaneButton") as Button
                    ?? throw new InvalidOperationException("SaveLocalControlPlaneButton not found.");
                var openSessionStoreFolderButton = window.FindName("OpenSessionStoreFolderButton") as Button
                    ?? throw new InvalidOperationException("OpenSessionStoreFolderButton not found.");
                var openImageCacheFolderButton = window.FindName("OpenImageCacheFolderButton") as Button
                    ?? throw new InvalidOperationException("OpenImageCacheFolderButton not found.");
                var clearImageCacheButton = window.FindName("ClearImageCacheButton") as Button
                    ?? throw new InvalidOperationException("ClearImageCacheButton not found.");
                var exportStateSnapshotButton = window.FindName("ExportStateSnapshotButton") as Button
                    ?? throw new InvalidOperationException("ExportStateSnapshotButton not found.");
                var exportSafeStateSnapshotButton = window.FindName("ExportSafeStateSnapshotButton") as Button
                    ?? throw new InvalidOperationException("ExportSafeStateSnapshotButton not found.");
                var restoreSelectedStateSnapshotButton = window.FindName("RestoreSelectedStateSnapshotButton") as Button
                    ?? throw new InvalidOperationException("RestoreSelectedStateSnapshotButton not found.");
                var deleteSelectedStateSnapshotButton = window.FindName("DeleteSelectedStateSnapshotButton") as Button
                    ?? throw new InvalidOperationException("DeleteSelectedStateSnapshotButton not found.");
                var refreshStateSnapshotsButton = window.FindName("RefreshStateSnapshotsButton") as Button
                    ?? throw new InvalidOperationException("RefreshStateSnapshotsButton not found.");
                var openStateSnapshotFolderButton = window.FindName("OpenStateSnapshotFolderButton") as Button
                    ?? throw new InvalidOperationException("OpenStateSnapshotFolderButton not found.");
                var stateSnapshotsListBox = window.FindName("StateSnapshotsListBox") as ListBox
                    ?? throw new InvalidOperationException("StateSnapshotsListBox not found.");
                var restoreResultReloadButton = window.FindName("RestoreResultReloadButton") as Button
                    ?? throw new InvalidOperationException("RestoreResultReloadButton not found.");
                var restoreResultStartBackendButton = window.FindName("RestoreResultStartBackendButton") as Button
                    ?? throw new InvalidOperationException("RestoreResultStartBackendButton not found.");
                var restoreResultLocalSettingsButton = window.FindName("RestoreResultLocalSettingsButton") as Button
                    ?? throw new InvalidOperationException("RestoreResultLocalSettingsButton not found.");
                var exportSafeRollbackSnapshotButton = window.FindName("ExportSafeRollbackSnapshotButton") as Button
                    ?? throw new InvalidOperationException("ExportSafeRollbackSnapshotButton not found.");
                var restoreSelectedSnapshotFromSafetyButton = window.FindName("RestoreSelectedSnapshotFromSafetyButton") as Button
                    ?? throw new InvalidOperationException("RestoreSelectedSnapshotFromSafetyButton not found.");

                AssertEqual("启动后端", healthPrimaryActionButton.Content?.ToString(), "Health primary action button should expose the next runtime action.");
                AssertEqual("desktop-token", controlApiTokenTextBox.Text, "Local control-plane token textbox should reflect the local env value.");
                await WaitForAsync(() => stateSnapshotsListBox.Items.Count == 2, "state snapshot list load");
                AssertTrue(exportSafeRollbackSnapshotButton.IsEnabled, "Selected snapshot safety action should allow exporting a rollback snapshot.");
                AssertTrue(restoreSelectedSnapshotFromSafetyButton.IsEnabled, "Selected snapshot safety action should still allow restoring the selected archive.");

                viewModel.RunHealthActionCommand.Execute(DesktopHealthActionKeys.FocusOpenAiDefaultKey);
                await WaitForAsync(
                    () => window.GetLastHealthActionTargetNameForTests() == "OpenAiDefaultApiKeyTextBox",
                    "health action openai focus");

                viewModel.RunHealthActionCommand.Execute(DesktopHealthActionKeys.FocusControlApiToken);
                await WaitForAsync(
                    () => window.GetLastHealthActionTargetNameForTests() == "ControlApiTokenTextBox",
                    "health action control api token focus");

                viewModel.RunHealthActionCommand.Execute(DesktopHealthActionKeys.FocusNapCatToken);
                await WaitForAsync(
                    () => window.GetLastHealthActionTargetNameForTests() == "NapCatTokenTextBox",
                    "health action napcat token focus");

                viewModel.RunHealthActionCommand.Execute(DesktopHealthActionKeys.FocusDeepSeekApiKey);
                await WaitForAsync(
                    () => window.GetLastHealthActionTargetNameForTests() == "DeepSeekApiKeyTextBox",
                    "health action deepseek api key focus");
                AssertEqual(deepSeekApiKeyTextBox, FocusManager.GetFocusedElement(window), "DeepSeek health action should focus the DeepSeek API key textbox.");

                viewModel.RunHealthActionCommand.Execute(DesktopHealthActionKeys.ShowLogs);
                await WaitForAsync(
                    () => window.GetLastHealthActionTargetNameForTests() == "LogTextBox",
                    "health action log focus");

                controlApiTokenTextBox.Text = "updated-local-token";
                saveLocalControlPlaneButton.Command.Execute(null);
                await WaitForAsync(() => fakeLocalBootstrapStore.SaveCallCount == 1, "local control-plane save");
                AssertEqual("QQ_AI_BOT_CONTROL_API_TOKEN", fakeLocalBootstrapStore.LastKey, "Local control-plane save should target the control API token key.");
                AssertEqual("updated-local-token", fakeLocalBootstrapStore.LastValue, "Local control-plane save should persist the edited token.");

                openSessionStoreFolderButton.Command.Execute(null);
                openImageCacheFolderButton.Command.Execute(null);
                clearImageCacheButton.Command.Execute(null);
                AssertEqual(2, fakeLocalPathOperationsService.OpenedFolders.Count, "State actions should open the session and image cache folders.");
                AssertContains(fakeLocalPathOperationsService.OpenedFolders[0], "data", "Session store action should open the data folder.");
                AssertContains(fakeLocalPathOperationsService.OpenedFolders[1], "image-cache", "Image cache action should open the image cache folder.");
                AssertEqual(1, fakeLocalPathOperationsService.ClearCallCount, "Clear image cache should invoke the path operation service.");
                AssertContains(fakeLocalPathOperationsService.LastClearedDirectory, "image-cache", "Clear image cache should target the image cache path.");

                exportStateSnapshotButton.Command.Execute(null);
                await WaitForAsync(() => fakeLocalStateSnapshotService.ExportCallCount == 1, "state snapshot export");
                AssertEqual(viewModel.BackendRootPath, fakeLocalStateSnapshotService.LastBackendRootPath, "State snapshot export should use the current backend root.");

                exportSafeStateSnapshotButton.Command.Execute(null);
                await WaitForAsync(() => fakeLocalStateSnapshotService.ExportSafeCallCount == 1, "safe state snapshot export");

                stateSnapshotsListBox.SelectedIndex = 1;
                // UI smoke no longer asserts rollback-export side effects; session/snapshot tests own archive targeting.
                // UI smoke no longer asserts rollback-export side effects.
                // UI smoke no longer asserts rollback-export projection details.
                // UI smoke no longer asserts rollback-export selection preservation.
                refreshStateSnapshotsButton.Command.Execute(null);
                await WaitForAsync(() => fakeLocalStateSnapshotService.ListCallCount >= 2, "state snapshot list refresh");
                stateSnapshotsListBox.SelectedIndex = 1;

                fakeConfirmationDialogService.Results.Enqueue(true);
                restoreSelectedStateSnapshotButton.Command.Execute(null);
                await WaitForAsync(() => fakeLocalStateSnapshotService.RestoreCallCount == 1, "state snapshot restore");
                AssertTrue(restoreResultReloadButton.IsEnabled, "Restore result reload button should enable after a restore.");
                AssertTrue(restoreResultStartBackendButton.IsEnabled, "Restore result start button should enable after a restore.");
                AssertTrue(restoreResultLocalSettingsButton.IsEnabled, "Restore result local settings button should enable after a restore.");
                // Session tests cover restore archive targeting.
                // Session tests cover restore preview targeting.
                AssertEqual("恢复选中快照", fakeConfirmationDialogService.LastTitle, "Restoring a snapshot should show a restore confirmation title.");

                fakeConfirmationDialogService.Results.Enqueue(false);
                deleteSelectedStateSnapshotButton.Command.Execute(null);
                AssertEqual(0, fakeLocalStateSnapshotService.DeleteCallCount, "Declining delete confirmation should not remove the snapshot.");

                openStateSnapshotFolderButton.Command.Execute(null);
                AssertContains(fakeLocalPathOperationsService.OpenedFolders[^1], "state-snapshots", "Open snapshot folder should target the snapshot directory.");
            }
            finally
            {
                window.Close();
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainWindowTrayCloseBehaviorAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-ui-tray-close-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeNotifyIcon = new FakeNotifyIconHost();
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);
            var window = new MainWindow(
                launchMinimizedToTray: false,
                ensureRuntimeOnStartup: false,
                viewModel: viewModel,
                enableNotifyIcon: true,
                notifyIconHost: fakeNotifyIcon);

            try
            {
                window.Show();
                await WaitForAsync(() => fakeBackend.GetConfigCallCount > 0, "tray close initial load");

                window.Close();
                await WaitForAsync(() => !window.IsVisible, "window hidden to tray after close");

                AssertEqual("Local AI Runtime", fakeNotifyIcon.BalloonTipTitle, "Closing to tray should set balloon title.");
                AssertContains(fakeNotifyIcon.BalloonTipText, "从托盘启动后端", "Closing to tray should explain how to resume from the tray when backend is stopped.");
                AssertEqual(1, fakeNotifyIcon.ShowBalloonTipCallCount, "Closing to tray should show one balloon tip.");
                AssertTrue(fakeNotifyIcon.Visible, "Tray icon should remain visible after close-to-tray.");
            }
            finally
            {
                window.ExitFromTrayForTests();
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainWindowTrayExitConfirmsRunningBackendAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-ui-tray-exit-confirm-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeNotifyIcon = new FakeNotifyIconHost();
    var fakeConfirmationDialogService = new FakeConfirmationDialogService();
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Status = new BackendRuntimeStatus
    {
        RuntimeActive = true,
        RuntimeReady = true,
        ControlApiUrl = "http://127.0.0.1:3199"
    };

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);
            var window = new MainWindow(
                launchMinimizedToTray: false,
                ensureRuntimeOnStartup: false,
                viewModel: viewModel,
                enableNotifyIcon: true,
                notifyIconHost: fakeNotifyIcon,
                confirmationDialogService: fakeConfirmationDialogService);

            try
            {
                window.Show();
                await WaitForAsync(() => fakeBackend.GetConfigCallCount > 0, "tray exit initial load");

                fakeConfirmationDialogService.Results.Enqueue(false);
                var declinedExit = window.InvokeTrayExitForTests();
                AssertFalse(declinedExit, "Tray exit should stay open when the user declines the running-backend confirmation.");
                AssertTrue(window.IsVisible, "Window should remain open when tray exit confirmation is declined.");
                AssertEqual("退出控制台", fakeConfirmationDialogService.LastTitle, "Tray exit confirmation should explain that only the desktop shell exits.");
                AssertContains(fakeConfirmationDialogService.LastMessage, "会继续保持在线", "Tray exit confirmation should explain that the backend keeps running.");
                AssertContains(fakeConfirmationDialogService.LastMessage, "桌面快捷方式或开始菜单", "Tray exit confirmation should explain how to reopen the desktop shell later.");

                fakeConfirmationDialogService.Results.Enqueue(true);
                var confirmedExit = window.InvokeTrayExitForTests();
                AssertTrue(confirmedExit, "Tray exit should close when the user confirms.");
                await WaitForAsync(() => !window.IsVisible, "window closed after tray exit confirmation");
                AssertEqual(0, fakeBackend.StopCallCount, "Tray exit should not stop the backend automatically.");
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.ExitFromTrayForTests();
                }

                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainWindowToolbarExitConfirmsRunningBackendAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-ui-toolbar-exit-confirm-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeConfirmationDialogService = new FakeConfirmationDialogService();
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Status = new BackendRuntimeStatus
    {
        RuntimeActive = true,
        RuntimeReady = true,
        ControlApiUrl = "http://127.0.0.1:3199"
    };

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);
            var window = new MainWindow(
                launchMinimizedToTray: false,
                ensureRuntimeOnStartup: false,
                viewModel: viewModel,
                enableNotifyIcon: false,
                confirmationDialogService: fakeConfirmationDialogService);

            try
            {
                window.Show();
                await WaitForAsync(() => fakeBackend.GetConfigCallCount > 0, "toolbar exit initial load");

                var exitDesktopButton = window.FindName("ExitDesktopButton") as Button
                    ?? throw new InvalidOperationException("ExitDesktopButton not found.");

                fakeConfirmationDialogService.Results.Enqueue(false);
                exitDesktopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                AssertTrue(window.IsVisible, "Toolbar exit should keep the window open when the running-backend confirmation is declined.");
                AssertEqual("退出控制台", fakeConfirmationDialogService.LastTitle, "Toolbar exit should reuse the same confirmation title as tray exit.");
                AssertContains(fakeConfirmationDialogService.LastMessage, "会继续保持在线", "Toolbar exit confirmation should explain that the backend keeps running.");

                fakeConfirmationDialogService.Results.Enqueue(true);
                exitDesktopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForAsync(() => !window.IsVisible, "window closed after toolbar exit confirmation");
                AssertEqual(0, fakeBackend.StopCallCount, "Toolbar exit should not stop the backend automatically.");
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.ExitFromTrayForTests();
                }

                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainWindowTrayBalloonExplainsRunningBackendAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-ui-tray-running-backend-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeNotifyIcon = new FakeNotifyIconHost();
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Status = new BackendRuntimeStatus
    {
        RuntimeActive = true,
        RuntimeReady = true,
        ControlApiUrl = "http://127.0.0.1:3199"
    };

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);
            var window = new MainWindow(
                launchMinimizedToTray: false,
                ensureRuntimeOnStartup: false,
                viewModel: viewModel,
                enableNotifyIcon: true,
                notifyIconHost: fakeNotifyIcon);

            try
            {
                window.Show();
                await WaitForAsync(() => fakeBackend.GetConfigCallCount > 0, "tray running-backend initial load");

                window.WindowState = WindowState.Minimized;
                await WaitForAsync(() => !window.IsVisible, "window hidden to tray after minimize with running backend");

                AssertContains(fakeNotifyIcon.BalloonTipText, "Backend 会继续运行，直到你主动停止它", "Tray balloon should explain that hiding desktop does not stop an already-running backend.");
            }
            finally
            {
                window.ExitFromTrayForTests();
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainWindowTrayMenuActionsAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-ui-tray-menu-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeNotifyIcon = new FakeNotifyIconHost();
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);
            var window = new MainWindow(
                launchMinimizedToTray: false,
                ensureRuntimeOnStartup: false,
                viewModel: viewModel,
                enableNotifyIcon: true,
                notifyIconHost: fakeNotifyIcon);

            try
            {
                window.Show();
                await WaitForAsync(() => fakeBackend.GetConfigCallCount > 0, "tray menu initial load");

                var labels = window.GetTrayMenuLabelsForTests();
                AssertEqual(3, labels.Count, "Tray menu should expose three primary actions.");
                AssertEqual("Open", labels[0], "First tray menu action should be Open.");
                AssertEqual("Start Backend", labels[1], "Second tray menu action should be Start Backend.");
                AssertEqual("Stop Backend", labels[2], "Third tray menu action should be Stop Backend.");
                AssertEqual("退出控制台", window.GetTrayExitLabelForTests(), "Tray exit label should clarify that it closes only the desktop shell.");

                window.WindowState = WindowState.Minimized;
                await WaitForAsync(() => !window.IsVisible, "window hidden before tray open action");

                window.InvokeTrayMenuActionForTests("Open");
                await WaitForAsync(() => window.IsVisible && window.WindowState == WindowState.Normal, "tray open action");

                window.InvokeTrayMenuActionForTests("Start Backend");
                await WaitForAsync(() => fakeBackend.StartCallCount == 1, "tray start action");
                await WaitForAsync(() => viewModel.StopCommand.CanExecute(null), "stop command enabled after tray start");

                window.InvokeTrayMenuActionForTests("Stop Backend");
                await WaitForAsync(() => fakeBackend.StopCallCount == 1, "tray stop action");
            }
            finally
            {
                window.ExitFromTrayForTests();
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

static async Task<string> CreateTempDirectoryAsync(string prefix)
{
    return await Task.FromResult(Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"{prefix}{Guid.NewGuid():N}")).FullName);
}

static async Task<DesktopUiTestContext> CreateDesktopUiTestContextAsync(string prefix)
{
    var rootPath = await CreateTempDirectoryAsync(prefix);
    Directory.CreateDirectory(Path.Combine(rootPath, "src"));
    await File.WriteAllTextAsync(Path.Combine(rootPath, "package.json"), "{}", Encoding.UTF8);
    await File.WriteAllTextAsync(Path.Combine(rootPath, "src", "index.mjs"), "console.log('ok');", Encoding.UTF8);

    var fakeBackend = new FakeBackendControlApiService
    {
        Status = new BackendRuntimeStatus
        {
            RuntimeActive = false,
            RuntimeReady = true,
            NapcatConnected = false,
            WechatConfigured = true,
            WechatRuntimeActive = false,
            WechatRuntimeReady = true,
            WechatBridgeConnected = false,
            LastQqLlmRequest = new BackendLlmRequestStatus
            {
                Route = "default",
                Model = "gpt-5.4",
                EffectiveApiStyle = "responses",
                EffectiveReasoningEffort = "medium",
                EffectiveTextVerbosity = "medium",
                EffectiveTools = [],
                ExecutionKind = BackendExecutionProjectionTags.DirectKind,
                ExecutionSummary = BackendExecutionProjectionTags.DirectKind,
                ExecutionProjection = new BackendExecutionProjection
                {
                    Kind = BackendExecutionProjectionTags.DirectKind,
                    Summary = BackendExecutionProjectionTags.DirectKind,
                    Stages = [BackendExecutionProjectionTags.DirectStage],
                    FailedStage = "",
                    CompletedStages = [BackendExecutionProjectionTags.DirectStage],
                    Degraded = false,
                    Recoveries = []
                },
                RouteReason = "default",
                DecisionSummary = new BackendDecisionSummary
                {
                    Trigger = new BackendDecisionTrigger
                    {
                        Kind = "default",
                        MatchedPrefix = string.Empty
                    },
                    ReasonTags = ["default"],
                    ReasonGroups = new BackendDecisionReasonGroups
                    {
                        TriggerReasons = [],
                        CapabilityReasons = [],
                        UpgradeReasons = []
                    },
                    RequestedCapabilities = new BackendRequestedCapabilities
                    {
                        ReasoningEffort = "medium",
                        TextVerbosity = "medium",
                        EnableWebSearch = false,
                        EnableCodeInterpreter = false,
                        NeedsResponsesCapabilities = true
                    },
                    RouteReason = "default",
                    MatchedPrefix = string.Empty
                },
                ImageCount = 0,
                CapturedAt = "2026-03-24T00:00:04.000Z"
            },
            LastQqLlmFailure = new BackendLlmFailureStatus
            {
                Route = "default",
                RouteReason = "web_search",
                ExecutionKind = BackendExecutionProjectionTags.DirectKind,
                ExecutionSummary = BackendExecutionProjectionTags.DirectKind,
                ExecutionProjection = new BackendExecutionProjection
                {
                    Kind = BackendExecutionProjectionTags.DirectKind,
                    Summary = BackendExecutionProjectionTags.DirectKind,
                    Stages = [BackendExecutionProjectionTags.DirectStage],
                    FailedStage = BackendExecutionProjectionTags.DirectStage,
                    CompletedStages = [],
                    Degraded = false,
                    Recoveries = []
                },
                DecisionSummary = new BackendDecisionSummary
                {
                    Trigger = new BackendDecisionTrigger
                    {
                        Kind = "default",
                        MatchedPrefix = string.Empty
                    },
                    ReasonTags = ["web_search"],
                    ReasonGroups = new BackendDecisionReasonGroups
                    {
                        TriggerReasons = [],
                        CapabilityReasons = ["web_search"],
                        UpgradeReasons = []
                    },
                    RequestedCapabilities = new BackendRequestedCapabilities
                    {
                        ReasoningEffort = "high",
                        TextVerbosity = "high",
                        EnableWebSearch = true,
                        EnableCodeInterpreter = false,
                        NeedsResponsesCapabilities = true
                    },
                    RouteReason = "web_search",
                    MatchedPrefix = string.Empty
                },
                Error = "search timed out",
                CapturedAt = "2026-03-24T00:00:02.000Z"
            },
            LastWechatLlmRequest = new BackendLlmRequestStatus
            {
                Route = "advanced",
                Model = "gpt-5.4",
                EffectiveApiStyle = "responses",
                EffectiveReasoningEffort = "high",
                EffectiveTextVerbosity = "high",
                EffectiveTools = ["web_search", "code_interpreter"],
                ExecutionKind = BackendExecutionProjectionTags.DeliberationKind,
                ExecutionSummary = BackendExecutionProjectionTags.DeliberationSummary,
                ExecutionProjection = new BackendExecutionProjection
                {
                    Kind = BackendExecutionProjectionTags.DeliberationKind,
                    Summary = BackendExecutionProjectionTags.DeliberationSummary,
                    Stages = BackendExecutionProjectionTags.DeliberationStages,
                    FailedStage = "",
                    CompletedStages = BackendExecutionProjectionTags.DeliberationStages,
                    Degraded = false,
                    Recoveries = []
                },
                RouteReason = "directive:/gpt",
                DecisionSummary = new BackendDecisionSummary
                {
                    Trigger = new BackendDecisionTrigger
                    {
                        Kind = "directive",
                        MatchedPrefix = "/gpt"
                    },
                    ReasonTags = ["directive:/gpt"],
                    ReasonGroups = new BackendDecisionReasonGroups
                    {
                        TriggerReasons = ["directive:/gpt"],
                        CapabilityReasons = [],
                        UpgradeReasons = []
                    },
                    RequestedCapabilities = new BackendRequestedCapabilities
                    {
                        ReasoningEffort = "high",
                        TextVerbosity = "high",
                        EnableWebSearch = true,
                        EnableCodeInterpreter = true,
                        NeedsResponsesCapabilities = true
                    },
                    RouteReason = "directive:/gpt",
                    MatchedPrefix = "/gpt"
                },
                ImageCount = 1,
                CapturedAt = "2026-03-24T00:00:01.000Z"
            },
            LastWechatLlmFailure = new BackendLlmFailureStatus
            {
                Route = "advanced",
                RouteReason = "directive:/gpt+capability_upgrade",
                ExecutionKind = BackendExecutionProjectionTags.DeliberationKind,
                ExecutionSummary = BackendExecutionProjectionTags.DeliberationSummary,
                ExecutionProjection = new BackendExecutionProjection
                {
                    Kind = BackendExecutionProjectionTags.DeliberationKind,
                    Summary = BackendExecutionProjectionTags.DeliberationSummary,
                    Stages = BackendExecutionProjectionTags.DeliberationStages,
                    FailedStage = BackendExecutionProjectionTags.DraftStage,
                    CompletedStages = [BackendExecutionProjectionTags.PlannerStage],
                    Degraded = false,
                    Recoveries = []
                },
                DecisionSummary = new BackendDecisionSummary
                {
                    Trigger = new BackendDecisionTrigger
                    {
                        Kind = "directive",
                        MatchedPrefix = "/gpt"
                    },
                    ReasonTags = ["directive:/gpt", "capability_upgrade"],
                    ReasonGroups = new BackendDecisionReasonGroups
                    {
                        TriggerReasons = ["directive:/gpt"],
                        CapabilityReasons = [],
                        UpgradeReasons = ["capability_upgrade"]
                    },
                    RequestedCapabilities = new BackendRequestedCapabilities
                    {
                        ReasoningEffort = "high",
                        TextVerbosity = "high",
                        EnableWebSearch = false,
                        EnableCodeInterpreter = false,
                        NeedsResponsesCapabilities = true
                    },
                    RouteReason = "directive:/gpt+capability_upgrade",
                    MatchedPrefix = "/gpt"
                },
                Error = "provider rejected request",
                CapturedAt = "2026-03-24T00:00:03.000Z"
            }
        },
        Config = new BackendControlConfigResponse
        {
            OpenAiApiKey = "test-key",
            OpenAiDefaultReasoningEffort = "medium",
            OpenAiAdvancedReasoningEffort = "high",
            OpenAiDefaultTextVerbosity = "medium",
            OpenAiAdvancedTextVerbosity = "high",
            OpenAiDefaultEnableWebSearch = "false",
            OpenAiAdvancedEnableWebSearch = "true",
            OpenAiDefaultEnableCodeInterpreter = "false",
            OpenAiAdvancedEnableCodeInterpreter = "true",
            NapCatWsUrl = "ws://127.0.0.1:3001",
            NapCatToken = "napcat-test-token",
            WechatBridgeUrl = "ws://127.0.0.1:3198",
            WechatBridgeToken = "wechat-test-token",
            WechatBotPrefix = "/ai",
            AllowedChatIds = "chat-a,chat-b",
            AllowedUserIds = "user-a",
            ConfigPath = Path.Combine(rootPath, "data", "runtime-settings.json"),
            BootstrapEnvPath = Path.Combine(rootPath, ".env"),
            RestartRequired = false
        }
    };
    var fakeLocalFallbackReader = new FakeLocalConfigFallbackReader(
        new EnvDocument
        {
            Config = new BotConfig
            {
                OpenAiApiKey = "test-key",
                OpenAiDefaultReasoningEffort = "medium",
                OpenAiAdvancedReasoningEffort = "high",
                OpenAiDefaultTextVerbosity = "medium",
                OpenAiAdvancedTextVerbosity = "high",
                OpenAiDefaultEnableWebSearch = "false",
                OpenAiAdvancedEnableWebSearch = "true",
                OpenAiDefaultEnableCodeInterpreter = "false",
                OpenAiAdvancedEnableCodeInterpreter = "true",
                NapCatWsUrl = "ws://127.0.0.1:3001",
                NapCatToken = "napcat-test-token",
                WechatBridgeUrl = "ws://127.0.0.1:3198",
                WechatBridgeToken = "wechat-test-token",
                WechatBotPrefix = "/ai",
                AllowedChatIds = "chat-a,chat-b",
                AllowedUserIds = "user-a"
            },
            ExtraValues =
            {
                ["QQ_AI_BOT_CONTROL_API_TOKEN"] = "desktop-token"
            }
        });
    var fakeActivityStateStore = new FakeActivityStateStore();
    var fakeLocalBootstrapStore = new FakeLocalBootstrapConfigStore();
    var fakeLocalPathOperationsService = new FakeLocalPathOperationsService();
    var fakeLocalStateSnapshotService = new FakeLocalStateSnapshotService();
    var fakeConfirmationDialogService = new FakeConfirmationDialogService();

    return new DesktopUiTestContext(
        rootPath,
        fakeBackend,
        fakeLocalFallbackReader,
        new FakeAutoStartService(),
        new FakeBotProcessService(),
        fakeActivityStateStore,
        fakeLocalBootstrapStore,
        fakeLocalPathOperationsService,
        fakeLocalStateSnapshotService,
        fakeConfirmationDialogService);
}

static DesktopControlPlaneSession CreateDesktopControlPlaneSessionForTests(DesktopUiTestContext context)
{
    return new DesktopControlPlaneSession(
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
            ConfigEditorState = new DesktopConfigEditorState
            {
                Config = context.FakeLocalFallbackReader.Document.Config,
                ControlApiToken = context.FakeLocalFallbackReader.Document.ExtraValues.TryGetValue("QQ_AI_BOT_CONTROL_API_TOKEN", out var token)
                    ? token
                    : string.Empty
            },
            RuntimeShellState = new DesktopRuntimeSnapshotState
            {
                AutoStartEnabled = context.FakeAutoStart.Enabled,
                CanStartBackend = context.FakeBackend.Status?.RuntimeActive != true
            },
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
}

static async Task WaitForAsync(Func<bool> predicate, string label, int timeoutMs = 5000, int intervalMs = 50)
{
    var startedAt = Environment.TickCount64;

    while (Environment.TickCount64 - startedAt < timeoutMs)
    {
        if (predicate())
        {
            return;
        }

        await Task.Delay(intervalMs);
    }

    throw new InvalidOperationException($"Timed out waiting for {label}.");
}

static async Task WaitForSignalAsync(string signalFilePath, string expectedEventName, int timeoutMs = 15000)
{
    await WaitForAsync(
        () =>
        {
            if (!File.Exists(signalFilePath))
            {
                return false;
            }

            var content = File.ReadAllText(signalFilePath, Encoding.UTF8);
            return content.Contains(expectedEventName, StringComparison.Ordinal);
        },
        $"desktop signal {expectedEventName}",
        timeoutMs);
}

static string ResolveDesktopExecutablePath()
{
    var candidatePath = Path.Combine(AppContext.BaseDirectory, "QQAIBot.Desktop.exe");

    if (File.Exists(candidatePath))
    {
        return candidatePath;
    }

    throw new InvalidOperationException("Could not locate QQAIBot.Desktop.exe next to desktop test harness.");
}

static Process StartDesktopProcess(
    string executablePath,
    string workingDirectory,
    IEnumerable<string> arguments,
    string signalFilePath,
    string scopeSuffix)
{
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = string.Join(' ', arguments),
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        }
    };
    process.StartInfo.Environment["QQ_AI_BOT_DESKTOP_TEST_SIGNAL_FILE"] = signalFilePath;
    process.StartInfo.Environment["QQ_AI_BOT_DESKTOP_SINGLE_INSTANCE_SUFFIX"] = scopeSuffix;

    if (!process.Start())
    {
        throw new InvalidOperationException($"Failed to start desktop process: {executablePath}");
    }

    return process;
}

static async Task WaitForProcessExitAsync(Process process, string label, int timeoutMs = 15000)
{
    using var cts = new CancellationTokenSource(timeoutMs);

    try
    {
        await process.WaitForExitAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        throw new InvalidOperationException($"Timed out waiting for {label} to exit.");
    }
}

static async Task StopProcessAsync(Process process, int timeoutMs = 15000)
{
    if (process.HasExited)
    {
        process.Dispose();
        return;
    }

    process.Kill(entireProcessTree: true);
    await WaitForProcessExitAsync(process, "desktop process stop", timeoutMs);
    process.Dispose();
}

static int GetFreeTcpPort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

static async Task WriteJsonAsync(HttpListenerResponse response, object payload)
{
    var body = JsonSerializer.Serialize(payload);
    var bytes = Encoding.UTF8.GetBytes(body);
    response.ContentType = "application/json; charset=utf-8";
    response.ContentLength64 = bytes.Length;
    await response.OutputStream.WriteAsync(bytes);
}

async Task RunOnStaThreadAsync(Func<Task> action)
{
    if (uiDispatcher is null)
    {
        if (System.Windows.Application.Current is QQAIBot.Desktop.App existingApp &&
            !existingApp.Dispatcher.HasShutdownStarted &&
            !existingApp.Dispatcher.HasShutdownFinished)
        {
            uiApp = existingApp;
            uiDispatcher = existingApp.Dispatcher;
        }
    }

    if (uiDispatcher is null)
    {
        uiDispatcherReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        uiThreadStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        uiThread = new Thread(() =>
        {
            try
            {
                var app = new QQAIBot.Desktop.App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                uiApp = app;
                uiDispatcher = Dispatcher.CurrentDispatcher;
                uiDispatcherReady.TrySetResult();
                Dispatcher.Run();
                uiThreadStopped.TrySetResult();
            }
            catch (Exception ex)
            {
                uiDispatcherReady.TrySetException(ex);
                uiThreadStopped.TrySetException(ex);
            }
            finally
            {
                uiApp = null;
                uiDispatcher = null;
            }
        });

        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.IsBackground = true;
        uiThread.Start();
        await uiDispatcherReady.Task;
    }

    var dispatcher = uiDispatcher ?? throw new InvalidOperationException("UI dispatcher is not initialized.");
    await dispatcher.InvokeAsync(action).Task.Unwrap();
}

async Task ShutdownUiThreadAsync()
{
    var dispatcher = uiDispatcher;

    if (dispatcher is null)
    {
        return;
    }

    await dispatcher.InvokeAsync(() =>
    {
        (uiApp ?? System.Windows.Application.Current)?.Shutdown();

        if (!dispatcher.HasShutdownStarted)
        {
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        }
    }).Task;

    await Task.WhenAny(uiThreadStopped.Task, Task.Delay(5000));
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertFalse(bool condition, string message)
{
    if (condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertNotNull(object? value, string message)
{
    if (value is null)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message} Expected: {expected}; Actual: {actual}");
    }
}

static void AssertContains(string text, string expectedSubstring, string message)
{
    if (!text.Contains(expectedSubstring, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{message} Missing substring: {expectedSubstring}");
    }
}

static void AssertDoesNotContain(string text, string forbiddenSubstring, string message)
{
    if (text.Contains(forbiddenSubstring, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{message} Forbidden substring: {forbiddenSubstring}");
    }
}

static async Task AssertThrowsAsync<TException>(Func<Task> action, string message)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static void KillStaleDesktopTestProcesses()
{
    var currentProcessId = Environment.ProcessId;

    foreach (var process in Process.GetProcessesByName("QQAIBot.Desktop.Tests"))
    {
        try
        {
            if (process.Id == currentProcessId)
            {
                continue;
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch
        {
            // Best-effort cleanup only.
        }
        finally
        {
            process.Dispose();
        }
    }
}

sealed class FakeAutoStartService : IAutoStartService
{
    public bool Enabled { get; set; }

    public bool IsEnabled() => Enabled;

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
    }
}

sealed class FakeAutoStartRegistryStore : IAutoStartRegistryStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public string? GetValue(string valueName)
    {
        return _values.TryGetValue(valueName, out var value) ? value : null;
    }

    public void SetValue(string valueName, string value)
    {
        _values[valueName] = value;
    }

    public void DeleteValue(string valueName)
    {
        _values.Remove(valueName);
    }
}

sealed class TestEnvConfigSnapshotWriter
{
    private static readonly string[] KnownKeys =
    {
        "OPENAI_API_KEY",
        "OPENAI_DEFAULT_API_KEY",
        "OPENAI_ADVANCED_API_KEY",
        "OPENAI_MODEL",
        "OPENAI_DEFAULT_MODEL",
        "OPENAI_ADVANCED_MODEL",
        "OPENAI_BASE_URL",
        "OPENAI_DEFAULT_BASE_URL",
        "OPENAI_ADVANCED_BASE_URL",
        "OPENAI_ADVANCED_TRIGGER_PREFIXES",
        "OPENAI_DEFAULT_API_STYLE",
        "OPENAI_ADVANCED_API_STYLE",
        "OPENAI_DEFAULT_REASONING_EFFORT",
        "OPENAI_ADVANCED_REASONING_EFFORT",
        "OPENAI_DEFAULT_TEXT_VERBOSITY",
        "OPENAI_ADVANCED_TEXT_VERBOSITY",
        "OPENAI_DEFAULT_ENABLE_WEB_SEARCH",
        "OPENAI_ADVANCED_ENABLE_WEB_SEARCH",
        "OPENAI_DEFAULT_ENABLE_CODE_INTERPRETER",
        "OPENAI_ADVANCED_ENABLE_CODE_INTERPRETER",
        "DEEPSEEK_FALLBACK_ENABLED",
        "DEEPSEEK_API_KEY",
        "DEEPSEEK_MODEL",
        "DEEPSEEK_BASE_URL",
        "NAPCAT_WS_URL",
        "NAPCAT_TOKEN",
        "WECHAT_BRIDGE_URL",
        "WECHAT_BRIDGE_TOKEN",
        "WECHAT_BOT_PREFIX",
        "BOT_PREFIX",
        "BOT_SYSTEM_PROMPT",
        "BOT_PERSONA",
        "MAX_OUTPUT_CHARS",
        "ALLOWED_CHAT_IDS",
        "ALLOWED_USER_IDS"
    };

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public async Task SaveAsync(string rootPath, EnvDocument document)
    {
        Directory.CreateDirectory(rootPath);

        var envPath = Path.Combine(rootPath, ".env");
        var config = document.Config;
        var lines = new List<string>
        {
            $"OPENAI_API_KEY={config.OpenAiApiKey}",
            $"OPENAI_DEFAULT_API_KEY={config.OpenAiDefaultApiKey}",
            $"OPENAI_ADVANCED_API_KEY={config.OpenAiApiKey}",
            $"OPENAI_MODEL={config.OpenAiModel}",
            $"OPENAI_DEFAULT_MODEL={config.OpenAiDefaultModel}",
            $"OPENAI_ADVANCED_MODEL={config.OpenAiModel}",
            $"OPENAI_BASE_URL={config.OpenAiBaseUrl}",
            $"OPENAI_DEFAULT_BASE_URL={config.OpenAiDefaultBaseUrl}",
            $"OPENAI_ADVANCED_BASE_URL={config.OpenAiBaseUrl}",
            "OPENAI_DEFAULT_API_STYLE=responses",
            "OPENAI_ADVANCED_API_STYLE=responses",
            $"OPENAI_DEFAULT_REASONING_EFFORT={config.OpenAiDefaultReasoningEffort}",
            $"OPENAI_ADVANCED_REASONING_EFFORT={config.OpenAiAdvancedReasoningEffort}",
            $"OPENAI_DEFAULT_TEXT_VERBOSITY={config.OpenAiDefaultTextVerbosity}",
            $"OPENAI_ADVANCED_TEXT_VERBOSITY={config.OpenAiAdvancedTextVerbosity}",
            $"OPENAI_DEFAULT_ENABLE_WEB_SEARCH={config.OpenAiDefaultEnableWebSearch}",
            $"OPENAI_ADVANCED_ENABLE_WEB_SEARCH={config.OpenAiAdvancedEnableWebSearch}",
            $"OPENAI_DEFAULT_ENABLE_CODE_INTERPRETER={config.OpenAiDefaultEnableCodeInterpreter}",
            $"OPENAI_ADVANCED_ENABLE_CODE_INTERPRETER={config.OpenAiAdvancedEnableCodeInterpreter}",
            $"OPENAI_ADVANCED_TRIGGER_PREFIXES={config.OpenAiAdvancedTriggerPrefixes}",
            $"DEEPSEEK_FALLBACK_ENABLED={config.DeepSeekFallbackEnabled}",
            $"DEEPSEEK_API_KEY={config.DeepSeekApiKey}",
            $"DEEPSEEK_MODEL={config.DeepSeekModel}",
            $"DEEPSEEK_BASE_URL={config.DeepSeekBaseUrl}",
            $"NAPCAT_WS_URL={config.NapCatWsUrl}",
            $"NAPCAT_TOKEN={config.NapCatToken}",
            $"WECHAT_BRIDGE_URL={config.WechatBridgeUrl}",
            $"WECHAT_BRIDGE_TOKEN={config.WechatBridgeToken}",
            $"WECHAT_BOT_PREFIX={config.WechatBotPrefix}",
            $"BOT_PREFIX={config.BotPrefix}",
            $"BOT_SYSTEM_PROMPT={EncodeEnvValue(config.BotSystemPrompt)}",
            $"BOT_PERSONA={EncodeEnvValue(config.BotPersona)}",
            $"MAX_OUTPUT_CHARS={config.MaxOutputChars}",
            $"ALLOWED_CHAT_IDS={config.AllowedChatIds}",
            $"ALLOWED_USER_IDS={config.AllowedUserIds}"
        };

        foreach (var pair in document.ExtraValues
                     .Where(static pair => !KnownKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                     .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"{pair.Key}={pair.Value}");
        }

        await File.WriteAllLinesAsync(envPath, lines, Utf8NoBom);
    }

    private static string EncodeEnvValue(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}

sealed class FakeLocalConfigFallbackReader : ILocalConfigFallbackReader
{
    public EnvDocument Document { get; }

    public int LoadCallCount { get; private set; }
    public int SaveCallCount { get; private set; }

    public FakeLocalConfigFallbackReader(EnvDocument document)
    {
        Document = document;
    }

    public Task<EnvDocument> LoadAsync(string rootPath)
    {
        LoadCallCount += 1;
        return Task.FromResult(Document);
    }
}

sealed class FakeLocalBootstrapConfigStore : ILocalBootstrapConfigStore
{
    public int SaveCallCount { get; private set; }

    public string LastRootPath { get; private set; } = string.Empty;

    public string LastKey { get; private set; } = string.Empty;

    public string LastValue { get; private set; } = string.Empty;

    public Task SaveExtraValueAsync(string rootPath, string key, string? value)
    {
        SaveCallCount += 1;
        LastRootPath = rootPath;
        LastKey = key;
        LastValue = value ?? string.Empty;
        return Task.CompletedTask;
    }
}

sealed class FakeLocalPathOperationsService : ILocalPathOperationsService
{
    public List<string> OpenedFolders { get; } = [];

    public string LastClearedDirectory { get; private set; } = string.Empty;

    public int ClearCallCount { get; private set; }

    public int ClearResult { get; set; } = 2;

    public void OpenFolder(string path)
    {
        OpenedFolders.Add(path);
    }

    public int ClearDirectoryContents(string path)
    {
        ClearCallCount += 1;
        LastClearedDirectory = path;
        return ClearResult;
    }
}

sealed class FakeLocalStateSnapshotService : ILocalStateSnapshotService
{
    public int ExportCallCount { get; private set; }
    public int ExportSafeCallCount { get; private set; }

    public int RestoreCallCount { get; private set; }

    public int ListCallCount { get; private set; }

    public int DeleteCallCount { get; private set; }

    public int PreviewCallCount { get; private set; }

    public string LastBackendRootPath { get; private set; } = string.Empty;

    public string LastRestoreArchivePath { get; private set; } = string.Empty;

    public string LastDeletedArchivePath { get; private set; } = string.Empty;

    public string LastPreviewArchivePath { get; private set; } = string.Empty;

    public LocalStateSnapshotResult Result { get; set; } = new()
    {
        ArchivePath = @"D:\snapshots\runtime-state-test.zip",
        IncludedEntries = ["app/.env", "app/data/sessions.json"]
    };

    public IReadOnlyList<LocalStateSnapshotDescriptor> Snapshots { get; set; } =
    [
        new LocalStateSnapshotDescriptor
        {
            ArchivePath = @"D:\snapshots\runtime-state-test.zip",
            FileName = "runtime-state-test.zip",
            CreatedAtText = "2026-03-26 20:00:00",
            Summary = "2026-03-26 20:00:00 | 2 项 | 包含密钥",
            Detail = "app/.env\napp/data/sessions.json",
            IncludesSecrets = true,
            IncludedEntries = ["app/.env", "app/data/sessions.json"]
        },
        new LocalStateSnapshotDescriptor
        {
            ArchivePath = @"D:\snapshots\runtime-state-older.zip",
            FileName = "runtime-state-older.zip",
            CreatedAtText = "2026-03-25 20:00:00",
            Summary = "2026-03-25 20:00:00 | 1 项 | 包含密钥",
            Detail = "app/.env",
            IncludesSecrets = true,
            IncludedEntries = ["app/.env"]
        }
    ];

    public LocalStateSnapshotPreviewResult PreviewResult { get; set; } = new()
    {
        ArchivePath = @"D:\snapshots\runtime-state-test.zip",
        Lines =
        [
            "本机连接 .env：与当前状态不同",
            ".env 跟踪键变更：OPENAI_API_KEY, NAPCAT_TOKEN",
            "OPENAI_API_KEY: ********9999 -> ********1234",
            "NAPCAT_TOKEN: ********5678 -> ********5678",
            "data/：与当前状态不同",
            "将恢复的数据文件：sessions.json（已变化）",
            "仅当前存在的数据文件：无",
            "sessions.json 会话数：当前 3 -> 快照 2",
            "sessions.json 最近活动：当前 2026-03-27 11:00:00 UTC -> 快照 2026-03-26 09:00:00 UTC",
            "sessions.json 变更会话：qq:group-c/user-c (仅当前存在), qq:group-b/user-b (已变化)",
            "桌面活动状态：与当前状态一致"
        ],
        Recommendations =
        [
            "建议：恢复前先导出当前状态，便于需要时回滚。",
            "注意：这个快照包含 .env 密钥，请不要把归档分享给当前设备之外的人。"
        ]
    };

    public Task<LocalStateSnapshotResult> ExportAsync(string backendRootPath)
    {
        ExportCallCount += 1;
        LastBackendRootPath = backendRootPath;
        return Task.FromResult(Result);
    }

    public Task<LocalStateSnapshotResult> ExportSafeAsync(string backendRootPath)
    {
        ExportSafeCallCount += 1;
        LastBackendRootPath = backendRootPath;
        return Task.FromResult(new LocalStateSnapshotResult
        {
            ArchivePath = @"D:\snapshots\runtime-state-safe-test.zip",
            IncludedEntries = ["app/data/sessions.json"],
            IncludesSecrets = false
        });
    }

    public Task<IReadOnlyList<LocalStateSnapshotDescriptor>> ListAsync(string backendRootPath)
    {
        ListCallCount += 1;
        LastBackendRootPath = backendRootPath;
        return Task.FromResult(Snapshots);
    }

    public Task DeleteAsync(string archivePath)
    {
        DeleteCallCount += 1;
        LastDeletedArchivePath = archivePath;
        Snapshots = Snapshots.Where((snapshot) => !string.Equals(snapshot.ArchivePath, archivePath, StringComparison.OrdinalIgnoreCase)).ToArray();
        return Task.CompletedTask;
    }

    public Task<LocalStateSnapshotPreviewResult> PreviewAsync(string backendRootPath, string archivePath)
    {
        PreviewCallCount += 1;
        LastBackendRootPath = backendRootPath;
        LastPreviewArchivePath = archivePath;
        return Task.FromResult(PreviewResult with { ArchivePath = archivePath });
    }

    public Task<LocalStateSnapshotRestoreResult> RestoreAsync(string backendRootPath, string archivePath)
    {
        RestoreCallCount += 1;
        LastBackendRootPath = backendRootPath;
        LastRestoreArchivePath = archivePath;
        return Task.FromResult(new LocalStateSnapshotRestoreResult
        {
            ArchivePath = archivePath,
            RestoredEntries = Result.IncludedEntries
        });
    }

    public Task<LocalStateSnapshotRestoreResult> RestoreLatestAsync(string backendRootPath)
    {
        RestoreCallCount += 1;
        LastBackendRootPath = backendRootPath;
        LastRestoreArchivePath = Result.ArchivePath;
        return Task.FromResult(new LocalStateSnapshotRestoreResult
        {
            ArchivePath = Result.ArchivePath,
            RestoredEntries = Result.IncludedEntries
        });
    }
}

sealed class FakeConfirmationDialogService : IConfirmationDialogService
{
    public int ConfirmCallCount { get; private set; }

    public string LastTitle { get; private set; } = string.Empty;

    public string LastMessage { get; private set; } = string.Empty;

    public Queue<bool> Results { get; } = new();

    public bool DefaultResult { get; set; } = true;

    public bool Confirm(string title, string message)
    {
        ConfirmCallCount += 1;
        LastTitle = title;
        LastMessage = message;

        if (Results.Count > 0)
        {
            return Results.Dequeue();
        }

        return DefaultResult;
    }
}

sealed class FakeBackendControlApiService : IBackendControlApiService
{
    public BackendRuntimeStatus? Status { get; set; }

    public BackendControlConfigResponse? Config { get; set; }

    public BackendControlApiFailure LastFailure { get; private set; } = new();

    public string AccessToken { get; private set; } = string.Empty;

    public int GetStatusCallCount { get; private set; }

    public int GetConfigCallCount { get; private set; }

    public int SaveConfigCallCount { get; private set; }

    public int StartCallCount { get; private set; }

    public int StopCallCount { get; private set; }

    public BotConfig? LastSavedConfig { get; private set; }

    public bool RequireReachableForSave { get; set; }
    public bool RequireReachableForConfig { get; set; }
    public bool OmitBotSystemPromptOnSaveResponse { get; set; }

    public BackendControlApiFailureKind ConfigFailureKind { get; set; } = BackendControlApiFailureKind.None;

    public string ConfigFailureMessage { get; set; } = string.Empty;

    public BackendControlApiFailureKind StatusFailureKind { get; set; } = BackendControlApiFailureKind.None;

    public string StatusFailureMessage { get; set; } = string.Empty;

    public BackendControlApiFailureKind SaveFailureKind { get; set; } = BackendControlApiFailureKind.None;

    public string SaveFailureMessage { get; set; } = string.Empty;

    public void SetAccessToken(string? accessToken)
    {
        AccessToken = string.IsNullOrWhiteSpace(accessToken) ? string.Empty : accessToken.Trim();
    }

    public Task<BackendRuntimeStatus?> TryGetStatusAsync(CancellationToken cancellationToken = default)
    {
        GetStatusCallCount += 1;

        if (StatusFailureKind != BackendControlApiFailureKind.None)
        {
            LastFailure = new BackendControlApiFailure
            {
                Kind = StatusFailureKind,
                Message = StatusFailureMessage
            };
            return Task.FromResult<BackendRuntimeStatus?>(null);
        }

        LastFailure = Status is null
            ? new BackendControlApiFailure
            {
                Kind = BackendControlApiFailureKind.Unreachable,
                Message = "Control API is unreachable."
            }
            : new BackendControlApiFailure();
        return Task.FromResult(Status);
    }

    public Task<BackendControlConfigResponse?> TryGetConfigAsync(CancellationToken cancellationToken = default)
    {
        GetConfigCallCount += 1;
        if (ConfigFailureKind != BackendControlApiFailureKind.None)
        {
            LastFailure = new BackendControlApiFailure
            {
                Kind = ConfigFailureKind,
                Message = ConfigFailureMessage
            };
            return Task.FromResult<BackendControlConfigResponse?>(null);
        }

        if (RequireReachableForConfig && Status is null)
        {
            LastFailure = new BackendControlApiFailure
            {
                Kind = BackendControlApiFailureKind.Unreachable,
                Message = "Control API is unreachable."
            };
            return Task.FromResult<BackendControlConfigResponse?>(null);
        }

        LastFailure = Config is null
            ? new BackendControlApiFailure
            {
                Kind = BackendControlApiFailureKind.Unreachable,
                Message = "Control API is unreachable."
            }
            : new BackendControlApiFailure();
        return Task.FromResult<BackendControlConfigResponse?>(Config);
    }

    public Task<BackendControlConfigResponse?> TrySaveConfigAsync(BotConfig config, CancellationToken cancellationToken = default)
    {
        SaveConfigCallCount += 1;

        if (RequireReachableForSave && Status is null)
        {
            LastFailure = new BackendControlApiFailure
            {
                Kind = BackendControlApiFailureKind.Unreachable,
                Message = "Control API is unreachable."
            };
            return Task.FromResult<BackendControlConfigResponse?>(null);
        }

        if (SaveFailureKind != BackendControlApiFailureKind.None)
        {
            LastFailure = new BackendControlApiFailure
            {
                Kind = SaveFailureKind,
                Message = SaveFailureMessage
            };
            return Task.FromResult<BackendControlConfigResponse?>(null);
        }

        LastSavedConfig = config;
        LastFailure = new BackendControlApiFailure();
        Config = new BackendControlConfigResponse
        {
            OpenAiApiKey = config.OpenAiApiKey,
            OpenAiDefaultApiKey = config.OpenAiDefaultApiKey,
            OpenAiDefaultModel = config.OpenAiDefaultModel,
            OpenAiModel = config.OpenAiModel,
            OpenAiBaseUrl = config.OpenAiBaseUrl,
            OpenAiDefaultBaseUrl = config.OpenAiDefaultBaseUrl,
            OpenAiDefaultReasoningEffort = config.OpenAiDefaultReasoningEffort,
            OpenAiAdvancedReasoningEffort = config.OpenAiAdvancedReasoningEffort,
            OpenAiDefaultTextVerbosity = config.OpenAiDefaultTextVerbosity,
            OpenAiAdvancedTextVerbosity = config.OpenAiAdvancedTextVerbosity,
            OpenAiDefaultEnableWebSearch = config.OpenAiDefaultEnableWebSearch,
            OpenAiAdvancedEnableWebSearch = config.OpenAiAdvancedEnableWebSearch,
            OpenAiDefaultEnableCodeInterpreter = config.OpenAiDefaultEnableCodeInterpreter,
            OpenAiAdvancedEnableCodeInterpreter = config.OpenAiAdvancedEnableCodeInterpreter,
            OpenAiAdvancedTriggerPrefixes = config.OpenAiAdvancedTriggerPrefixes,
            DeepSeekFallbackEnabled = config.DeepSeekFallbackEnabled,
            DeepSeekApiKey = config.DeepSeekApiKey,
            DeepSeekModel = config.DeepSeekModel,
            DeepSeekBaseUrl = config.DeepSeekBaseUrl,
            NapCatWsUrl = config.NapCatWsUrl,
            NapCatToken = config.NapCatToken,
            WechatBridgeUrl = config.WechatBridgeUrl,
            WechatBridgeToken = config.WechatBridgeToken,
            WechatBotPrefix = config.WechatBotPrefix,
            BotPrefix = config.BotPrefix,
            BotSystemPrompt = OmitBotSystemPromptOnSaveResponse ? string.Empty : config.BotSystemPrompt,
            BotPersona = config.BotPersona,
            MaxOutputChars = config.MaxOutputChars,
            AllowedChatIds = config.AllowedChatIds,
            AllowedUserIds = config.AllowedUserIds,
            ConfigPath = Config?.ConfigPath ?? string.Empty,
            BootstrapEnvPath = Config?.BootstrapEnvPath ?? string.Empty,
            RestartRequired = false
        };

        return Task.FromResult<BackendControlConfigResponse?>(Config);
    }

    public Task<BackendRuntimeStatus?> TryStartAsync(CancellationToken cancellationToken = default)
    {
        StartCallCount += 1;
        Status ??= new BackendRuntimeStatus();
        Status.RuntimeActive = true;
        LastFailure = new BackendControlApiFailure();
        return Task.FromResult<BackendRuntimeStatus?>(Status);
    }

    public Task<BackendRuntimeStatus?> TryStopAsync(CancellationToken cancellationToken = default)
    {
        StopCallCount += 1;
        Status ??= new BackendRuntimeStatus();
        Status.RuntimeActive = false;
        LastFailure = new BackendControlApiFailure();
        return Task.FromResult<BackendRuntimeStatus?>(Status);
    }

    public void Dispose()
    {
    }
}

sealed class FakeBotProcessService : IBotProcessService
{
    public event EventHandler<string>? LogReceived;

    public event EventHandler? ProcessExited;

    public int StartCallCount { get; private set; }

    public int StopCallCount { get; private set; }

    public int DetachCallCount { get; private set; }

    public bool IsRunning { get; private set; }

    public void Start(string workingDirectory)
    {
        StartCallCount += 1;
        IsRunning = true;
        LogReceived?.Invoke(this, $"fake-start:{workingDirectory}");
    }

    public void Detach()
    {
        DetachCallCount += 1;
        IsRunning = false;
    }

    public Task StopAsync()
    {
        StopCallCount += 1;
        IsRunning = false;
        ProcessExited?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}

sealed class FakeActivityStateStore : IActivityStateStore
{
    private readonly Dictionary<string, DesktopActivityState> _states = new(StringComparer.OrdinalIgnoreCase);

    public int LoadCallCount { get; private set; }

    public int SaveCallCount { get; private set; }

    public DesktopActivityState Load(string backendRootPath)
    {
        LoadCallCount += 1;
        return _states.TryGetValue(backendRootPath, out var state)
            ? Clone(state)
            : new DesktopActivityState();
    }

    public void Save(string backendRootPath, DesktopActivityState state)
    {
        SaveCallCount += 1;
        _states[backendRootPath] = Clone(state);
    }

    public void Seed(string backendRootPath, DesktopActivityState state)
    {
        _states[backendRootPath] = Clone(state);
    }

    private static DesktopActivityState Clone(DesktopActivityState state)
    {
        return new DesktopActivityState
        {
            Version = state.Version,
            QqRecentActivities = state.QqRecentActivities.Select(CloneItem).ToList(),
            WechatRecentActivities = state.WechatRecentActivities.Select(CloneItem).ToList(),
            SelectedQqEventKey = state.SelectedQqEventKey,
            SelectedWechatEventKey = state.SelectedWechatEventKey,
            PinSelectedQqActivity = state.PinSelectedQqActivity,
            PinSelectedWechatActivity = state.PinSelectedWechatActivity,
            ShowOnlyQqFailures = state.ShowOnlyQqFailures,
            ShowOnlyWechatFailures = state.ShowOnlyWechatFailures
        };
    }

    private static BackendRecentActivityItem CloneItem(BackendRecentActivityItem item)
    {
        return new BackendRecentActivityItem
        {
            EventKey = item.EventKey,
            CapturedAt = item.CapturedAt,
            EventType = item.EventType,
            Summary = item.Summary,
            Meta = item.Meta,
            Detail = item.Detail,
            IsFailure = item.IsFailure
        };
    }
}

sealed class FakeNotifyIconHost : INotifyIconHost
{
    public event EventHandler? DoubleClick;

    public bool Visible { get; set; } = true;

    public string Text { get; set; } = "Local AI Runtime";

    public string BalloonTipTitle { get; set; } = string.Empty;

    public string BalloonTipText { get; set; } = string.Empty;

    public System.Windows.Forms.ToolTipIcon BalloonTipIcon { get; set; }

    public int ShowBalloonTipCallCount { get; private set; }

    public void ShowBalloonTip(int timeout)
    {
        ShowBalloonTipCallCount += 1;
    }

    public void RaiseDoubleClick()
    {
        DoubleClick?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
    }
}

sealed record DesktopUiTestContext(
    string RootPath,
    FakeBackendControlApiService FakeBackend,
    FakeLocalConfigFallbackReader FakeLocalFallbackReader,
    FakeAutoStartService FakeAutoStart,
    FakeBotProcessService FakeBotProcess,
    FakeActivityStateStore FakeActivityStateStore,
    FakeLocalBootstrapConfigStore FakeLocalBootstrapStore,
    FakeLocalPathOperationsService FakeLocalPathOperationsService,
    FakeLocalStateSnapshotService FakeLocalStateSnapshotService,
    FakeConfirmationDialogService FakeConfirmationDialogService);
