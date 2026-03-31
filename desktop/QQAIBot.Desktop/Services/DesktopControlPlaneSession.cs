using System.IO;
using System.Linq;

using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public sealed class DesktopControlPlaneSession
{
    private const int ControlApiRecoveryAttemptThreshold = 2;
    private const int ControlApiOutageNotificationThreshold = 3;
    private const string ControlApiTokenEnvKey = "QQ_AI_BOT_CONTROL_API_TOKEN";
    private static readonly DesktopActivityStateStoragePolicy ActivityStateStoragePolicy = new();

    private readonly DesktopSessionDependencies _dependencies;
    private DesktopShellSourceState _sourceState;
    private DesktopShellState _shellState;
    private bool _restoringActivityState;

    public DesktopControlPlaneSession(
        DesktopSessionDependencies dependencies,
        DesktopShellState initialState)
    {
        _dependencies = dependencies;
        _shellState = initialState;
        _sourceState = DesktopShellProjector.CreateSourceState(initialState);
        ProjectShellState();
    }

    public DesktopShellState BuildCurrentShellState() => _shellState;

    public DesktopShellState UpdateEditorState(
        BotConfig config,
        string controlApiToken,
        bool hasUnsavedChanges,
        string lastLoadedAtText,
        string lastSavedAtText,
        bool autoStartEnabled,
        bool canStartBackend,
        string logText)
    {
        _sourceState = DesktopConfigWorkflow.UpdateEditorState(
            _sourceState,
            config,
            controlApiToken,
            hasUnsavedChanges,
            lastLoadedAtText,
            lastSavedAtText,
            autoStartEnabled,
            canStartBackend,
            logText);

        RecalculateDerivedState();
        return _shellState;
    }

    public DesktopShellState UpdateBackendRoot(string backendRootPath, bool backendRootDetected)
    {
        _sourceState = DesktopActivityWorkflow.UpdateBackendRoot(
            _sourceState,
            backendRootPath,
            backendRootDetected);

        LoadActivityStateCore();
        ApplyLocalDocumentProjection();
        RecalculateDerivedState();
        return _shellState;
    }

    public async Task<DesktopCommandResult> InitializeAsync()
    {
        return await LoadConfigAsync();
    }

    public async Task<(BackendControlConfigResponse? ApiConfig, BackendRuntimeStatus? ApiStatus)> LoadAuthoritativeConfigAsync()
    {
        var loadResult = await BackendControlPlaneFacade.LoadAuthoritativeConfigAsync(
            tryGetConfigAsync: (cancellationToken) => _dependencies.BackendControlApiService.TryGetConfigAsync(cancellationToken),
            getLastFailure: () => _dependencies.BackendControlApiService.LastFailure,
            tryGetStatusAsync: (cancellationToken) => _dependencies.BackendControlApiService.TryGetStatusAsync(cancellationToken),
            isImmediateFailure: IsImmediateControlApiFailure,
            tryRecoverControlApiAsync: () => TryRecoverControlApiAsyncInternal("load-config"));

        if (loadResult.ApiConfig is not null)
        {
            EnsureCompatibleControlApiConfigResponse(loadResult.ApiConfig);
        }

        return loadResult;
    }

    public async Task<DesktopCommandResult> LoadConfigAsync()
    {
        if (!IsBackendRootValid())
        {
            SetSourceStatusText("Backend root is invalid");

            return BuildResult(
                succeeded: false,
                statusText: "Backend root is invalid",
                logMessages: ["Cannot load config because backend root is invalid."]);
        }

        try
        {
            SetSourceStatusText("Loading config...");

            var localEnvDocument = await LoadLocalEnvDocumentAsync();
            var loadResult = await LoadAuthoritativeConfigAsync();
            var apiConfig = loadResult.ApiConfig;
            var apiStatus = loadResult.ApiStatus;
            var nextDocument = apiConfig is not null
                ? new EnvDocument
                {
                    Config = DesktopConfigWorkflow.BuildConfigCopy(apiConfig)
                }
                : localEnvDocument;

            CopyLocalExtraValues(localEnvDocument, nextDocument);
            ApplyControlApiAccessToken(nextDocument);

            _sourceState = DesktopConfigWorkflow.ApplyLoadedDocument(
                _sourceState,
                nextDocument,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            ApplyLocalDocumentProjection();
            ApplyBackendRuntimeStatusCore(apiStatus, apiStatus is not null);
            await RefreshStateSnapshotsAsyncInternal(selectArchivePath: null);
            SetSourceStatusText(
                apiConfig?.RestartRequired == true
                    ? "Config loaded (restart required)"
                    : "Config loaded");
            RecalculateDerivedState();

            return BuildResult(
                succeeded: true,
                statusText: _shellState.UiFeedbackState.StatusText,
                logMessages:
                [
                    apiConfig is not null
                        ? $"Loaded runtime config via control API: {apiConfig.ConfigPath}"
                        : $"Loaded local fallback config: {ResolveLocalFallbackConfigPath()}"
                ]);
        }
        catch (Exception ex)
        {
            var userFacingError = DesktopOperationErrorFormatter.Build(
                operationLabel: "Load config",
                fallbackStatusText: "Load failed",
                technicalMessage: ex.Message,
                controlApiFailure: _dependencies.BackendControlApiService.LastFailure,
                configPath: RuntimeConfigPath(),
                bootstrapEnvPath: BootstrapEnvFilePath(),
                canStartBackend: _sourceState.RuntimeSourceState.CanStartBackend);
            SetSourceStatusText(userFacingError.StatusText);

            return BuildResult(
                succeeded: false,
                statusText: userFacingError.StatusText,
                logMessages: [$"Load config failed: {ex.Message}"],
                error: userFacingError);
        }
    }

    public async Task<DesktopCommandResult> SaveLocalControlPlaneAsync(string controlApiToken)
    {
        if (!IsBackendRootValid())
        {
            SetSourceStatusText("Backend root is invalid");

            return BuildResult(
                succeeded: false,
                statusText: "Backend root is invalid",
                logMessages: ["Cannot save local control-plane settings because backend root is invalid."]);
        }

        try
        {
            var normalizedToken = controlApiToken?.Trim() ?? string.Empty;
            await _dependencies.LocalBootstrapConfigStore.SaveExtraValueAsync(
                _shellState.LocalDocumentState.BackendRootPath,
                ControlApiTokenEnvKey,
                normalizedToken);

            var nextDocument = DesktopConfigWorkflow.CloneEnvDocument(_shellState.LocalDocumentState.ConfigDocument.Document);
            if (string.IsNullOrWhiteSpace(normalizedToken))
            {
                nextDocument.ExtraValues.Remove(ControlApiTokenEnvKey);
            }
            else
            {
                nextDocument.ExtraValues[ControlApiTokenEnvKey] = normalizedToken;
            }

            ApplyControlApiAccessToken(nextDocument);
            _sourceState = DesktopConfigWorkflow.ApplySavedLocalControlPlane(
                _sourceState,
                nextDocument,
                normalizedToken);
            SetSourceStatusText("Local control-plane settings saved");
            RecalculateDerivedState();

            return BuildResult(
                succeeded: true,
                statusText: "Local control-plane settings saved",
                logMessages: [$"Saved local control-plane token to bootstrap .env: {BootstrapEnvFilePath()}"]);
        }
        catch (Exception ex)
        {
            var userFacingError = DesktopOperationErrorFormatter.Build(
                operationLabel: "Save local control settings",
                fallbackStatusText: "Local control-plane save failed",
                technicalMessage: ex.Message,
                bootstrapEnvPath: BootstrapEnvFilePath(),
                localControlSettingsOperation: true,
                canStartBackend: _sourceState.RuntimeSourceState.CanStartBackend);
            SetSourceStatusText(userFacingError.StatusText);

            return BuildResult(
                succeeded: false,
                statusText: userFacingError.StatusText,
                logMessages: [$"Saving local control-plane settings failed: {ex.Message}"],
                error: userFacingError);
        }
    }

    public async Task<DesktopCommandResult> SaveConfigAsync(BotConfig config, bool showUiErrors)
    {
        if (!IsBackendRootValid())
        {
            SetSourceStatusText("Backend root is invalid");

            return BuildResult(
                succeeded: false,
                statusText: "Backend root is invalid",
                logMessages: ["Cannot save config because backend root is invalid."]);
        }

        try
        {
            _sourceState = DesktopConfigWorkflow.ApplySaveDraft(_sourceState, config);
            SetSourceStatusText("Saving config...");

            var apiResult = await SaveConfigThroughControlApiAsync(config);
            var mergedConfig = DesktopConfigWorkflow.MergeSavedConfig(config, apiResult);
            var nextDocument = DesktopConfigWorkflow.CloneEnvDocument(_sourceState.LocalDocumentSourceState.ConfigDocument.Document, mergedConfig);

            _sourceState = DesktopConfigWorkflow.ApplySavedConfig(
                _sourceState,
                mergedConfig,
                nextDocument,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            SetSourceStatusText(apiResult.RestartRequired ? "Config saved (restart required)" : "Config saved");
            RecalculateDerivedState();

            return BuildResult(
                succeeded: true,
                statusText: _shellState.UiFeedbackState.StatusText,
                logMessages: [$"Saved runtime config via control API: {apiResult.ConfigPath}"]);
        }
        catch (Exception ex)
        {
            var userFacingError = DesktopOperationErrorFormatter.Build(
                operationLabel: "Save config",
                fallbackStatusText: "Save failed",
                technicalMessage: ex.Message,
                controlApiFailure: _dependencies.BackendControlApiService.LastFailure,
                configPath: RuntimeConfigPath(),
                bootstrapEnvPath: BootstrapEnvFilePath(),
                canStartBackend: _sourceState.RuntimeSourceState.CanStartBackend);
            SetSourceStatusText(userFacingError.StatusText);

            return BuildResult(
                succeeded: false,
                statusText: userFacingError.StatusText,
                logMessages: [$"Save config failed: {ex.Message}"],
                error: showUiErrors ? userFacingError : userFacingError,
                suggestedHealthActionKey: userFacingError.SuggestedActionKey);
        }
    }

    public async Task<DesktopCommandResult> StartBackendAsync(BotConfig config)
    {
        var saveResult = await SaveConfigAsync(config, showUiErrors: true);
        if (!saveResult.Succeeded)
        {
            return saveResult;
        }

        try
        {
            SetSourceStatusText("Starting backend...");

            var outcome = await BackendControlPlaneFacade.StartBackendAsync(
                prepareAsync: async () => { await LoadLocalEnvDocumentAsync(suppressErrors: true); },
                tryGetStatusAsync: (cancellationToken) => _dependencies.BackendControlApiService.TryGetStatusAsync(cancellationToken),
                getLastFailure: () => _dependencies.BackendControlApiService.LastFailure,
                isImmediateFailure: IsImmediateControlApiFailure,
                startProcess: () => _dependencies.BotProcessService.Start(_shellState.LocalDocumentState.BackendRootPath),
                waitForStatusAsync: (cancellationToken) => BackendControlApiStatusWaiter.WaitForStatusAsync(
                    (innerCancellationToken) => _dependencies.BackendControlApiService.TryGetStatusAsync(innerCancellationToken),
                    () => _dependencies.BackendControlApiService.LastFailure,
                    cancellationToken: cancellationToken),
                tryStartAsync: (cancellationToken) => _dependencies.BackendControlApiService.TryStartAsync(cancellationToken));

            if (outcome.AppliedStatus is not null || !outcome.ControlApiReachable)
            {
                ApplyBackendRuntimeStatusCore(outcome.AppliedStatus, outcome.ControlApiReachable);
            }

            if (outcome.ShouldDetachProcess)
            {
                _dependencies.BotProcessService.Detach();
            }

            if (outcome.ShouldReloadConfig)
            {
                await LoadConfigAsync();
            }

            SetSourceStatusText(outcome.StatusText);

            return BuildResult(
                succeeded: true,
                statusText: outcome.StatusText,
                logMessages: outcome.LogMessages,
                notifications: outcome.Notifications);
        }
        catch (Exception ex)
        {
            var userFacingError = DesktopOperationErrorFormatter.Build(
                operationLabel: "Start backend",
                fallbackStatusText: "Start failed",
                technicalMessage: ex.Message,
                controlApiFailure: _dependencies.BackendControlApiService.LastFailure,
                configPath: RuntimeConfigPath(),
                bootstrapEnvPath: BootstrapEnvFilePath(),
                canStartBackend: _sourceState.RuntimeSourceState.CanStartBackend);
            SetSourceStatusText(userFacingError.StatusText);

            return BuildResult(
                succeeded: false,
                statusText: userFacingError.StatusText,
                logMessages: [$"Start backend failed: {ex.Message}"],
                error: userFacingError);
        }
    }

    public async Task<DesktopCommandResult> StopBackendAsync()
    {
        try
        {
            SetSourceStatusText("Stopping backend...");

            var outcome = await BackendControlPlaneFacade.StopBackendAsync(
                prepareAsync: async () => { await LoadLocalEnvDocumentAsync(suppressErrors: true); },
                tryStopAsync: (cancellationToken) => _dependencies.BackendControlApiService.TryStopAsync(cancellationToken),
                getLastFailure: () => _dependencies.BackendControlApiService.LastFailure,
                isImmediateFailure: IsImmediateControlApiFailure,
                isProcessRunning: () => _dependencies.BotProcessService.IsRunning,
                stopProcessAsync: () => _dependencies.BotProcessService.StopAsync());

            ApplyBackendRuntimeStatusCore(outcome.AppliedStatus, outcome.ControlApiReachable);
            SetSourceStatusText(outcome.StatusText);

            return BuildResult(
                succeeded: true,
                statusText: outcome.StatusText,
                logMessages: outcome.LogMessages,
                notifications: outcome.Notifications);
        }
        catch (Exception ex)
        {
            var userFacingError = DesktopOperationErrorFormatter.Build(
                operationLabel: "Stop backend",
                fallbackStatusText: "Stop failed",
                technicalMessage: ex.Message,
                controlApiFailure: _dependencies.BackendControlApiService.LastFailure,
                configPath: RuntimeConfigPath(),
                bootstrapEnvPath: BootstrapEnvFilePath(),
                canStartBackend: _sourceState.RuntimeSourceState.CanStartBackend);
            SetSourceStatusText(userFacingError.StatusText);

            return BuildResult(
                succeeded: false,
                statusText: userFacingError.StatusText,
                logMessages: [$"Stop backend failed: {ex.Message}"],
                error: userFacingError);
        }
    }

    public async Task<DesktopCommandResult> PollStatusAsync()
    {
        await LoadLocalEnvDocumentAsync(suppressErrors: true);
        var status = await _dependencies.BackendControlApiService.TryGetStatusAsync();
        var statusFailure = _dependencies.BackendControlApiService.LastFailure;
        var pollOutcome = BackendControlApiStatusPollCoordinator.Evaluate(
            _sourceState.RuntimeSourceState.RuntimeSnapshot,
            _sourceState.RuntimeSourceState.ControlApiPollState,
            status,
            statusFailure,
            ControlApiRecoveryAttemptThreshold,
            ControlApiOutageNotificationThreshold);

        _sourceState = DesktopRuntimeWorkflow.ApplyPollOutcome(
            _sourceState,
            pollOutcome.NextPollState,
            pollOutcome.NextRuntimeSnapshot);

        if (!pollOutcome.ShouldApplyRuntimeStatus)
        {
            RecalculateDerivedState();

            if (pollOutcome.ShouldAttemptRecovery)
            {
                var recoveryResult = await TryRecoverControlApiAsyncInternal("status-poll");
                return BuildResult(
                    succeeded: recoveryResult.Succeeded,
                    statusText: recoveryResult.StatusText,
                    logMessages: pollOutcome.LogMessages.Concat(recoveryResult.LogMessages).ToArray(),
                    notifications: pollOutcome.Notifications.Concat(recoveryResult.Notifications).ToArray(),
                    error: recoveryResult.Error,
                    suggestedHealthActionKey: recoveryResult.SuggestedHealthActionKey);
            }

            return BuildResult(
                succeeded: true,
                statusText: _shellState.UiFeedbackState.StatusText,
                logMessages: pollOutcome.LogMessages,
                notifications: pollOutcome.Notifications);
        }

        ApplyBackendRuntimeStatusCore(status, true);
        ResetControlApiFailureState();

        return BuildResult(
            succeeded: true,
            statusText: _shellState.UiFeedbackState.StatusText,
            logMessages: pollOutcome.LogMessages,
            notifications: pollOutcome.Notifications);
    }

    public DesktopShellState ApplyBackendRuntimeStatus(BackendRuntimeStatus? status, bool controlApiReachable)
    {
        ApplyBackendRuntimeStatusCore(status, controlApiReachable);
        return _shellState;
    }

    public DesktopShellState ReloadActivityState()
    {
        LoadActivityStateCore();
        return _shellState;
    }

    public DesktopShellState ClearQqActivityHistory()
    {
        _sourceState = DesktopActivityWorkflow.ClearQqActivityHistory(_sourceState);

        PersistActivityStateIfPossible();
        RecalculateDerivedState();
        return _shellState;
    }

    public DesktopShellState ClearWechatActivityHistory()
    {
        _sourceState = DesktopActivityWorkflow.ClearWechatActivityHistory(_sourceState);

        PersistActivityStateIfPossible();
        RecalculateDerivedState();
        return _shellState;
    }

    public DesktopCommandResult OpenSessionStoreFolder()
    {
        if (!IsBackendRootValid())
        {
            _shellState = _shellState with
            {
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = "Backend root is invalid"
                }
            };

            return BuildResult(
                succeeded: false,
                statusText: "Backend root is invalid",
                logMessages: ["Cannot open session store folder because backend root is invalid."]);
        }

        var sessionDirectory = Path.Combine(_shellState.LocalDocumentState.BackendRootPath, "data");
        _dependencies.LocalPathOperationsService.OpenFolder(sessionDirectory);
        return BuildResult(
            succeeded: true,
            statusText: "Opened session store folder",
            logMessages: [$"Opened session store folder: {sessionDirectory}"]);
    }

    public DesktopCommandResult OpenImageCacheFolder()
    {
        if (!IsBackendRootValid())
        {
            _shellState = _shellState with
            {
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = "Backend root is invalid"
                }
            };

            return BuildResult(
                succeeded: false,
                statusText: "Backend root is invalid",
                logMessages: ["Cannot open image cache folder because backend root is invalid."]);
        }

        var imageCachePath = Path.Combine(_shellState.LocalDocumentState.BackendRootPath, "data", "image-cache");
        _dependencies.LocalPathOperationsService.OpenFolder(imageCachePath);
        return BuildResult(
            succeeded: true,
            statusText: "Opened image cache folder",
            logMessages: [$"Opened image cache folder: {imageCachePath}"]);
    }

    public DesktopCommandResult ClearImageCache()
    {
        if (!IsBackendRootValid())
        {
            _shellState = _shellState with
            {
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = "Backend root is invalid"
                }
            };

            return BuildResult(
                succeeded: false,
                statusText: "Backend root is invalid",
                logMessages: ["Cannot clear image cache because backend root is invalid."]);
        }

        try
        {
            var imageCachePath = Path.Combine(_shellState.LocalDocumentState.BackendRootPath, "data", "image-cache");
            var removedEntries = _dependencies.LocalPathOperationsService.ClearDirectoryContents(imageCachePath);
            var statusText = removedEntries > 0 ? "Image cache cleared" : "Image cache was already empty";
            var logMessage = removedEntries > 0
                ? $"Cleared image cache {imageCachePath}, removing {removedEntries} entries."
                : $"Image cache was already empty: {imageCachePath}";

            ApplyLocalDocumentProjection();
            return BuildResult(
                succeeded: true,
                statusText: statusText,
                logMessages: [logMessage]);
        }
        catch (Exception ex)
        {
            return BuildResult(
                succeeded: false,
                statusText: "Image cache clear failed",
                logMessages: [$"Image cache clear failed: {ex.Message}"],
                error: new DesktopUserFacingOperationError
                {
                    StatusText = "Image cache clear failed",
                    DialogTitle = "Image cache clear failed",
                    DialogMessage = $"Image cache clear failed:{Environment.NewLine}{ex.Message}"
                });
        }
    }

    public DesktopCommandResult OpenStateSnapshotFolder()
    {
        if (!IsBackendRootValid())
        {
            _shellState = _shellState with
            {
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = "Backend root is invalid"
                }
            };

            return BuildResult(
                succeeded: false,
                statusText: "Backend root is invalid",
                logMessages: ["Cannot open state snapshot folder because backend root is invalid."]);
        }

        var snapshotFolderPath = Path.Combine(_shellState.LocalDocumentState.BackendRootPath, "artifacts", "state-snapshots");
        _dependencies.LocalPathOperationsService.OpenFolder(snapshotFolderPath);
        return BuildResult(
            succeeded: true,
            statusText: "Opened state snapshot folder",
            logMessages: [$"Opened state snapshot folder: {snapshotFolderPath}"]);
    }

    public async Task<DesktopCommandResult> ExportStateSnapshotAsync()
    {
        if (!IsBackendRootValid())
        {
            _shellState = _shellState with
            {
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = "Backend root is invalid"
                }
            };

            return BuildResult(
                succeeded: false,
                statusText: "Backend root is invalid",
                logMessages: ["Cannot export state snapshot because backend root is invalid."]);
        }

        try
        {
            SetSourceStatusText("Exporting state snapshot...");

            var result = await _dependencies.LocalStateSnapshotService.ExportAsync(_shellState.LocalDocumentState.BackendRootPath);
            await RefreshStateSnapshotsAsyncInternal(result.ArchivePath);
            _sourceState = DesktopSnapshotWorkflow.ApplySnapshotExportResult(_sourceState, result.ArchivePath);

            return BuildResult(
                succeeded: true,
                statusText: "State snapshot exported",
                logMessages: [$"Exported local state snapshot to {result.ArchivePath}, including {result.IncludedEntries.Count} entries."]);
        }
        catch (Exception ex)
        {
            return BuildResult(
                succeeded: false,
                statusText: "State snapshot export failed",
                logMessages: [$"State snapshot export failed: {ex.Message}"],
                error: new DesktopUserFacingOperationError
                {
                    StatusText = "State snapshot export failed",
                    DialogTitle = "State snapshot export failed",
                    DialogMessage = $"State snapshot export failed:{Environment.NewLine}{ex.Message}"
                });
        }
    }

    public async Task<DesktopCommandResult> ExportSafeStateSnapshotAsync()
    {
        if (!IsBackendRootValid())
        {
            SetSourceStatusText("Backend root is invalid");

            return BuildResult(
                succeeded: false,
                statusText: "Backend root is invalid",
                logMessages: ["Cannot export safe state snapshot because backend root is invalid."]);
        }

        try
        {
            SetSourceStatusText("Exporting safe state snapshot...");

            var result = await _dependencies.LocalStateSnapshotService.ExportSafeAsync(_shellState.LocalDocumentState.BackendRootPath);
            await RefreshStateSnapshotsAsyncInternal(result.ArchivePath);
            _sourceState = DesktopSnapshotWorkflow.ApplySnapshotExportResult(_sourceState, result.ArchivePath);

            return BuildResult(
                succeeded: true,
                statusText: "Safe state snapshot exported",
                logMessages: [$"Exported safe state snapshot to {result.ArchivePath}, including {result.IncludedEntries.Count} entries and excluding .env secrets."]);
        }
        catch (Exception ex)
        {
            return BuildResult(
                succeeded: false,
                statusText: "Safe state snapshot export failed",
                logMessages: [$"Safe state snapshot export failed: {ex.Message}"],
                error: new DesktopUserFacingOperationError
                {
                    StatusText = "Safe state snapshot export failed",
                    DialogTitle = "Safe state snapshot export failed",
                    DialogMessage = $"Safe state snapshot export failed:{Environment.NewLine}{ex.Message}"
                });
        }
    }

    public async Task<DesktopCommandResult> ExportSafeRollbackSnapshotAsync(string restoreTargetArchivePath)
    {
        if (!IsBackendRootValid())
        {
            SetSourceStatusText("Backend root is invalid");

            return BuildResult(
                succeeded: false,
                statusText: "Backend root is invalid",
                logMessages: ["Cannot export safe rollback snapshot because backend root is invalid."]);
        }

        try
        {
            var selectedSnapshot = _shellState.SnapshotState.SelectedStateSnapshot;
            var restoreTargetFileName = selectedSnapshot?.FileName ?? Path.GetFileName(restoreTargetArchivePath);

            SetSourceStatusText("Exporting safe rollback snapshot...");

            var result = await _dependencies.LocalStateSnapshotService.ExportSafeAsync(_shellState.LocalDocumentState.BackendRootPath);
            await RefreshStateSnapshotsAsyncInternal(restoreTargetArchivePath);
            _sourceState = DesktopSnapshotWorkflow.ApplySnapshotExportResult(_sourceState, result.ArchivePath);

            return BuildResult(
                succeeded: true,
                statusText: "Safe rollback snapshot exported",
                logMessages: [$"Exported safe rollback snapshot to {result.ArchivePath} before restoring {restoreTargetFileName}, while keeping the original restore target selected."]);
        }
        catch (Exception ex)
        {
            return BuildResult(
                succeeded: false,
                statusText: "Safe rollback snapshot export failed",
                logMessages: [$"Safe rollback snapshot export failed: {ex.Message}"],
                error: new DesktopUserFacingOperationError
                {
                    StatusText = "Safe rollback snapshot export failed",
                    DialogTitle = "Safe rollback snapshot export failed",
                    DialogMessage = $"Safe rollback snapshot export failed:{Environment.NewLine}{ex.Message}"
                });
        }
    }

    public async Task<DesktopConfirmationPrompt> BuildRestoreLatestStateSnapshotConfirmationAsync()
    {
        var latestSnapshot = (await _dependencies.LocalStateSnapshotService.ListAsync(
            _shellState.LocalDocumentState.BackendRootPath)).FirstOrDefault()
            ?? throw new InvalidOperationException("当前没有可恢复的状态快照。");
        var preview = await _dependencies.LocalStateSnapshotService.PreviewAsync(
            _shellState.LocalDocumentState.BackendRootPath,
            latestSnapshot.ArchivePath);

        return new DesktopConfirmationPrompt
        {
            Title = "恢复最新快照",
            Message = BuildRestoreConfirmationMessage(latestSnapshot, preview),
            ArchivePath = latestSnapshot.ArchivePath
        };
    }

    public async Task<DesktopConfirmationPrompt> BuildRestoreSelectedStateSnapshotConfirmationAsync(string archivePath)
    {
        var snapshot = _shellState.SnapshotState.StateSnapshots.FirstOrDefault(
                (candidate) => string.Equals(candidate.ArchivePath, archivePath, StringComparison.OrdinalIgnoreCase))
            ?? _shellState.SnapshotState.SelectedStateSnapshot
            ?? throw new InvalidOperationException("当前没有选中的状态快照。");
        var preview = await _dependencies.LocalStateSnapshotService.PreviewAsync(
            _shellState.LocalDocumentState.BackendRootPath,
            snapshot.ArchivePath);

        return new DesktopConfirmationPrompt
        {
            Title = "恢复选中快照",
            Message = BuildRestoreConfirmationMessage(snapshot, preview),
            ArchivePath = snapshot.ArchivePath
        };
    }

    public DesktopConfirmationPrompt BuildDeleteSelectedStateSnapshotConfirmation(string archivePath)
    {
        var snapshot = _shellState.SnapshotState.StateSnapshots.FirstOrDefault(
                (candidate) => string.Equals(candidate.ArchivePath, archivePath, StringComparison.OrdinalIgnoreCase))
            ?? _shellState.SnapshotState.SelectedStateSnapshot
            ?? throw new InvalidOperationException("当前没有选中的状态快照。");

        return new DesktopConfirmationPrompt
        {
            Title = "删除选中快照",
            Message = BuildDeleteConfirmationMessage(snapshot),
            ArchivePath = snapshot.ArchivePath
        };
    }

    public DesktopShellState ApplyActivityState(DesktopActivityState? state)
    {
        ApplyActivityStateCore(state);
        return _shellState;
    }

    public async Task<DesktopCommandResult> TryRecoverControlApiAsync(string reason)
    {
        return await TryRecoverControlApiAsyncInternal(reason);
    }

    public DesktopShellState ApplyActivityStateSelection(
        bool? pinSelectedQqActivity = null,
        bool? pinSelectedWechatActivity = null,
        bool? showOnlyQqFailures = null,
        bool? showOnlyWechatFailures = null,
        BackendRecentActivityItem? selectedQqRecentActivity = null,
        BackendRecentActivityItem? selectedWechatRecentActivity = null,
        bool updateQqSelection = false,
        bool updateWechatSelection = false)
    {
        _sourceState = DesktopActivityWorkflow.ApplyActivitySelection(
            _sourceState,
            pinSelectedQqActivity,
            pinSelectedWechatActivity,
            showOnlyQqFailures,
            showOnlyWechatFailures,
            selectedQqRecentActivity,
            selectedWechatRecentActivity,
            updateQqSelection,
            updateWechatSelection);

        PersistActivityStateIfPossible();
        RecalculateDerivedState();
        return _shellState;
    }

    public async Task<DesktopCommandResult> RefreshStateSnapshotsAsync(string? selectArchivePath = null)
    {
        await RefreshStateSnapshotsAsyncInternal(selectArchivePath);
        return BuildResult(true, _shellState.UiFeedbackState.StatusText);
    }

    public DesktopShellState UpdateSelectedStateSnapshot(LocalStateSnapshotDescriptor? snapshot)
    {
        _sourceState = DesktopSnapshotWorkflow.UpdateSelectedSnapshot(_sourceState, snapshot);
        ApplySelectedStateSnapshotPresentationCore();
        return _shellState;
    }

    public async Task<DesktopCommandResult> RefreshSelectedStateSnapshotPreviewAsync()
    {
        var selectedSnapshot = _shellState.SnapshotState.SelectedStateSnapshot;

        if (selectedSnapshot is null || !IsBackendRootValid())
        {
            _sourceState = DesktopSnapshotWorkflow.ClearSelectedSnapshotPreview(_sourceState);
            ApplySelectedStateSnapshotPresentationCore();
            return BuildResult(true, _shellState.UiFeedbackState.StatusText);
        }

        ApplySelectedStateSnapshotPresentationCore(
            diffTextOverride: "Loading diff preview...",
            adviceTextOverride: "Loading restore advice...");

        try
        {
            var preview = await _dependencies.LocalStateSnapshotService.PreviewAsync(
                _shellState.LocalDocumentState.BackendRootPath,
                selectedSnapshot.ArchivePath);

            if (!string.Equals(
                    _shellState.SnapshotState.SelectedStateSnapshot?.ArchivePath,
                    selectedSnapshot.ArchivePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return BuildResult(true, _shellState.UiFeedbackState.StatusText);
            }

            _sourceState = DesktopSnapshotWorkflow.ApplySelectedSnapshotPreview(_sourceState, preview);
            ApplySelectedStateSnapshotPresentationCore();
        }
        catch (Exception ex)
        {
            if (string.Equals(
                    _shellState.SnapshotState.SelectedStateSnapshot?.ArchivePath,
                    selectedSnapshot.ArchivePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                _sourceState = DesktopSnapshotWorkflow.ClearSelectedSnapshotPreview(_sourceState);
                ApplySelectedStateSnapshotPresentationCore(
                    diffTextOverride: $"Diff preview unavailable: {ex.Message}",
                    adviceTextOverride: "Review the snapshot details carefully before restoring.");
            }
        }

        return BuildResult(true, _shellState.UiFeedbackState.StatusText);
    }

    public async Task<DesktopCommandResult> RestoreLatestStateSnapshotAsync()
    {
        var latestSnapshot = (await _dependencies.LocalStateSnapshotService.ListAsync(
            _shellState.LocalDocumentState.BackendRootPath)).FirstOrDefault();
        var preview = latestSnapshot is null
            ? null
            : await _dependencies.LocalStateSnapshotService.PreviewAsync(
                _shellState.LocalDocumentState.BackendRootPath,
                latestSnapshot.ArchivePath);
        var result = await _dependencies.LocalStateSnapshotService.RestoreLatestAsync(_shellState.LocalDocumentState.BackendRootPath);
        _sourceState = DesktopSnapshotWorkflow.ApplyRestoreResult(_sourceState, result.ArchivePath, result, preview);
        await LoadConfigAsync();
        ApplyRestorePresentationCore();
        return BuildResult(true, _shellState.UiFeedbackState.StatusText, [$"已从 {result.ArchivePath} 恢复本地状态快照，共恢复 {result.RestoredEntries.Count} 项。"]);
    }

    public async Task<DesktopCommandResult> RestoreSelectedStateSnapshotAsync(string archivePath)
    {
        var preview = _shellState.SnapshotState.SelectedStateSnapshotPreview;
        if (!string.Equals(preview?.ArchivePath, archivePath, StringComparison.OrdinalIgnoreCase))
        {
            preview = await _dependencies.LocalStateSnapshotService.PreviewAsync(
                _shellState.LocalDocumentState.BackendRootPath,
                archivePath);
        }

        var result = await _dependencies.LocalStateSnapshotService.RestoreAsync(
            _shellState.LocalDocumentState.BackendRootPath,
            archivePath);
        _sourceState = DesktopSnapshotWorkflow.ApplyRestoreResult(_sourceState, result.ArchivePath, result, preview);
        await LoadConfigAsync();
        ApplyRestorePresentationCore();
        return BuildResult(true, _shellState.UiFeedbackState.StatusText, [$"已从 {result.ArchivePath} 恢复选中状态快照，共恢复 {result.RestoredEntries.Count} 项。"]);
    }

    public async Task<DesktopCommandResult> DeleteSelectedStateSnapshotAsync(string archivePath)
    {
        await _dependencies.LocalStateSnapshotService.DeleteAsync(archivePath);
        await RefreshStateSnapshotsAsyncInternal(selectArchivePath: null);

        var previousRestoreText = _sourceState.SnapshotSourceState.LastStateRestoreText;
        _sourceState = DesktopSnapshotWorkflow.ClearRestoreResultIfMatchesArchive(_sourceState, archivePath);

        if (!string.Equals(previousRestoreText, _shellState.SnapshotState.LastStateRestoreText, StringComparison.Ordinal))
        {
            ApplyRestorePresentationCore();
        }

        return BuildResult(true, _shellState.UiFeedbackState.StatusText);
    }

    private async Task<BackendControlConfigResponse> SaveConfigThroughControlApiAsync(BotConfig config)
    {
        var apiResult = await BackendControlPlaneFacade.SaveConfigAsync(
            prepareAsync: async () => { await LoadLocalEnvDocumentAsync(suppressErrors: true); },
            config,
            trySaveConfigAsync: (submittedConfig, cancellationToken) => _dependencies.BackendControlApiService.TrySaveConfigAsync(submittedConfig, cancellationToken),
            getLastFailure: () => _dependencies.BackendControlApiService.LastFailure,
            tryGetStatusAsync: (cancellationToken) => _dependencies.BackendControlApiService.TryGetStatusAsync(cancellationToken),
            isImmediateFailure: IsImmediateControlApiFailure,
            tryRecoverControlApiAsync: () => TryRecoverControlApiAsyncInternal("save-config"));

        EnsureCompatibleControlApiConfigResponse(apiResult);
        return apiResult;
    }

    private async Task<DesktopCommandResult> TryRecoverControlApiAsyncInternal(string reason)
    {
        if (_sourceState.RuntimeSourceState.ControlApiRecoveryInProgress || !IsBackendRootValid())
        {
            return BuildResult(true, _shellState.UiFeedbackState.StatusText);
        }

        _sourceState = DesktopRuntimeWorkflow.SetRecoveryInProgress(_sourceState, true);

        try
        {
            var outcome = await BackendControlApiRecoveryCoordinator.TryRecoverAsync(
                reason,
                prepareAsync: async () => { await LoadLocalEnvDocumentAsync(suppressErrors: true); },
                tryGetStatusAsync: (cancellationToken) => _dependencies.BackendControlApiService.TryGetStatusAsync(cancellationToken),
                getLastFailure: () => _dependencies.BackendControlApiService.LastFailure,
                isImmediateFailure: IsImmediateControlApiFailure,
                isProcessRunning: () => _dependencies.BotProcessService.IsRunning,
                startProcess: () => _dependencies.BotProcessService.Start(_shellState.LocalDocumentState.BackendRootPath),
                tryStartAsync: (cancellationToken) => _dependencies.BackendControlApiService.TryStartAsync(cancellationToken),
                waitForStatusAsync: (cancellationToken) => BackendControlApiStatusWaiter.WaitForStatusAsync(
                    (innerCancellationToken) => _dependencies.BackendControlApiService.TryGetStatusAsync(innerCancellationToken),
                    () => _dependencies.BackendControlApiService.LastFailure,
                    cancellationToken: cancellationToken),
                detachProcess: () => _dependencies.BotProcessService.Detach());

            if (outcome.RecoveredStatus is not null)
            {
                ApplyBackendRuntimeStatusCore(outcome.RecoveredStatus, true);
                ResetControlApiFailureState();
            }

            return BuildResult(
                succeeded: true,
                statusText: _shellState.UiFeedbackState.StatusText,
                logMessages: outcome.LogMessages);
        }
        finally
        {
            _sourceState = DesktopRuntimeWorkflow.SetRecoveryInProgress(_sourceState, false);
        }
    }

    private async Task<EnvDocument> LoadLocalEnvDocumentAsync(bool suppressErrors = false)
    {
        if (!IsBackendRootValid())
        {
            _dependencies.BackendControlApiService.SetAccessToken(null);
            return new EnvDocument();
        }

        try
        {
            var document = await _dependencies.LocalConfigFallbackReader.LoadAsync(_sourceState.LocalDocumentSourceState.BackendRootPath);
            ApplyControlApiAccessToken(document);
            return document;
        }
        catch
        {
            _dependencies.BackendControlApiService.SetAccessToken(null);

            if (suppressErrors)
            {
                return new EnvDocument();
            }

            throw;
        }
    }

    private void ApplyBackendRuntimeStatusCore(BackendRuntimeStatus? status, bool controlApiReachable)
    {
        var projection = BackendRuntimeSnapshotCoordinator.ProjectRuntimeStatus(
            status,
            controlApiReachable,
            new BackendChannelActivityContext(
                _shellState.RecentActivityState.QqRecentActivities,
                _shellState.RecentActivityState.LastQqRequestEventKey,
                _shellState.RecentActivityState.LastQqFailureEventKey,
                _shellState.RecentActivityState.PinSelectedQqActivity,
                _shellState.RecentActivityState.SelectedQqRecentActivity),
            new BackendChannelActivityContext(
                _shellState.RecentActivityState.WechatRecentActivities,
                _shellState.RecentActivityState.LastWechatRequestEventKey,
                _shellState.RecentActivityState.LastWechatFailureEventKey,
                _shellState.RecentActivityState.PinSelectedWechatActivity,
                _shellState.RecentActivityState.SelectedWechatRecentActivity));

        _sourceState = DesktopRuntimeWorkflow.ApplyRuntimeProjection(_sourceState, projection);

        RecalculateDerivedState();

        if (_shellState.SnapshotState.LastStateRestoreResult is not null)
        {
            ApplyRestorePresentationCore();
        }

        PersistActivityStateIfPossible();
    }

    private void LoadActivityStateCore()
    {
        if (!IsBackendRootValid())
        {
            ApplyActivityStateCore(_dependencies.ActivityStatePolicy.CreateDefaultState());
            return;
        }

        ApplyActivityStateCore(_dependencies.ActivityStateStore.Load(_sourceState.LocalDocumentSourceState.BackendRootPath));
    }

    private void PersistActivityStateIfPossible()
    {
        if (_restoringActivityState || !IsBackendRootValid())
        {
            return;
        }

        var state = _dependencies.ActivityStatePolicy.CreateSnapshot(
            _sourceState.RecentActivityState.QqRecentActivities,
            _sourceState.RecentActivityState.WechatRecentActivities,
            _sourceState.RecentActivityState.SelectedQqRecentActivity,
            _sourceState.RecentActivityState.SelectedWechatRecentActivity,
            _sourceState.RecentActivityState.PinSelectedQqActivity,
            _sourceState.RecentActivityState.PinSelectedWechatActivity,
            _sourceState.RecentActivityState.ShowOnlyQqFailures,
            _sourceState.RecentActivityState.ShowOnlyWechatFailures);

        _dependencies.ActivityStateStore.Save(_sourceState.LocalDocumentSourceState.BackendRootPath, state);
    }

    private void ApplyActivityStateCore(DesktopActivityState? state)
    {
        var projection = BackendRuntimeSnapshotCoordinator.ProjectActivityRestore(
            _dependencies.ActivityStatePolicy,
            state);

        _restoringActivityState = true;
        try
        {
            _sourceState = DesktopActivityWorkflow.ApplyRestoredActivityState(_sourceState, projection);
        }
        finally
        {
            _restoringActivityState = false;
        }

        _sourceState = DesktopActivityWorkflow.ApplyActivitySelection(
            _sourceState,
            showOnlyQqFailures: _sourceState.RecentActivityState.ShowOnlyQqFailures,
            showOnlyWechatFailures: _sourceState.RecentActivityState.ShowOnlyWechatFailures);

        RecalculateDerivedState();
    }

    private void ResetControlApiFailureState()
    {
        _sourceState = DesktopRuntimeWorkflow.ResetControlApiFailureState(_sourceState);
    }

    private async Task RefreshStateSnapshotsAsyncInternal(string? selectArchivePath)
    {
        if (!IsBackendRootValid())
        {
            _sourceState = DesktopSnapshotWorkflow.ClearSnapshotSelection(_sourceState);
            ApplySelectedStateSnapshotPresentationCore();
            ApplyLocalDocumentProjection();
            return;
        }

        var snapshots = await _dependencies.LocalStateSnapshotService.ListAsync(_shellState.LocalDocumentState.BackendRootPath);
        var selectedArchivePath = !string.IsNullOrWhiteSpace(selectArchivePath)
            ? selectArchivePath
            : _shellState.SnapshotState.SelectedStateSnapshot?.ArchivePath;

        var selectedSnapshot = snapshots.FirstOrDefault(
            (snapshot) => string.Equals(snapshot.ArchivePath, selectedArchivePath, StringComparison.OrdinalIgnoreCase))
            ?? snapshots.FirstOrDefault();

        _sourceState = DesktopSnapshotWorkflow.ApplySnapshotList(_sourceState, snapshots, selectedSnapshot);
        ApplySelectedStateSnapshotPresentationCore();
        ApplyLocalDocumentProjection();
    }

    private void RecalculateDerivedState()
    {
        ProjectShellState();
    }

    private void ApplySelectedStateSnapshotPresentationCore(
        string? diffTextOverride = null,
        string? adviceTextOverride = null)
    {
        ProjectShellState(diffTextOverride, adviceTextOverride);
    }

    private void ApplyRestorePresentationCore()
    {
        ProjectShellState();
    }

    private LocalStateSnapshotRestoreRuntimeContext BuildRestorePresentationContext()
    {
        var runtimeSnapshot = _shellState.RuntimeShellState.RuntimeSnapshot;
        return new LocalStateSnapshotRestoreRuntimeContext
        {
            ControlApiFailure = _dependencies.BackendControlApiService.LastFailure,
            IsControlApiReachable = runtimeSnapshot.ControlApiReachable == true,
            CanStartBackend = _shellState.RuntimeShellState.CanStartBackend,
            IsQqRuntimeReady = runtimeSnapshot.RuntimeReady == true,
            IsWechatConfigured = runtimeSnapshot.WechatConfigured == true || !string.IsNullOrWhiteSpace(_shellState.ConfigEditorState.Config.WechatBridgeUrl),
            IsWechatRuntimeReady = runtimeSnapshot.WechatRuntimeReady == true
        };
    }

    private void ApplyLocalDocumentProjection()
    {
        var viewState = DesktopShellProjector.ProjectViewState(
            _sourceState,
            new DesktopShellProjectionContext
            {
                ControlApiFailure = _dependencies.BackendControlApiService.LastFailure,
                ActivityStateStoragePolicy = ActivityStateStoragePolicy
            });
        _shellState = _shellState with
        {
            LocalDocumentState = viewState.LocalDocumentState
        };
    }

    private void ProjectShellState(
        string? selectedSnapshotDiffTextOverride = null,
        string? selectedSnapshotAdviceTextOverride = null)
    {
        _shellState = DesktopShellProjector.Project(
            _sourceState,
            new DesktopShellProjectionContext
            {
                ControlApiFailure = _dependencies.BackendControlApiService.LastFailure,
                ActivityStateStoragePolicy = ActivityStateStoragePolicy,
                SelectedSnapshotDiffTextOverride = selectedSnapshotDiffTextOverride,
                SelectedSnapshotAdviceTextOverride = selectedSnapshotAdviceTextOverride
            });
    }

    private static string BuildImageCacheStateText(bool isBackendRootValid, string imageCachePath)
    {
        if (!isBackendRootValid)
        {
            return "后端目录有效后才能显示缓存路径";
        }

        if (!Directory.Exists(imageCachePath))
        {
            return "图片缓存为空";
        }

        var cachedFileCount = Directory.GetFiles(imageCachePath, "*", SearchOption.AllDirectories).Length;
        return cachedFileCount == 0
            ? "图片缓存为空"
            : $"{cachedFileCount} 个缓存图片文件";
    }

    private static string BuildControlApiTokenStateText(string controlApiToken, BackendControlApiFailure lastFailure)
    {
        if (string.IsNullOrWhiteSpace(controlApiToken))
        {
            return "本机令牌未设置";
        }

        return lastFailure.Kind == BackendControlApiFailureKind.Unauthorized
            ? "本机令牌已保存，但后端仍然拒绝它"
            : "本机令牌已配置";
    }

    private static string ResolveLocalExtraValue(EnvDocument? document, string key, string fallback)
    {
        if (document?.ExtraValues.TryGetValue(key, out var value) == true &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        return fallback;
    }

    private bool IsBackendRootValid()
    {
        return PathDiscoveryService.IsBackendRoot(_sourceState.LocalDocumentSourceState.BackendRootPath);
    }

    private string RuntimeConfigPath()
    {
        return Path.Combine(_sourceState.LocalDocumentSourceState.BackendRootPath, "data", "runtime-settings.json");
    }

    private string BootstrapEnvFilePath()
    {
        return Path.Combine(_sourceState.LocalDocumentSourceState.BackendRootPath, ".env");
    }

    private string ResolveLocalFallbackConfigPath()
    {
        var runtimeConfigPath = RuntimeConfigPath();
        return File.Exists(runtimeConfigPath) ? runtimeConfigPath : BootstrapEnvFilePath();
    }

    private void ApplyControlApiAccessToken(EnvDocument? document)
    {
        if (document?.ExtraValues.TryGetValue(ControlApiTokenEnvKey, out var accessToken) == true &&
            !string.IsNullOrWhiteSpace(accessToken))
        {
            _dependencies.BackendControlApiService.SetAccessToken(accessToken);
            return;
        }

        _dependencies.BackendControlApiService.SetAccessToken(null);
    }

    private static void CopyLocalExtraValues(EnvDocument? source, EnvDocument target)
    {
        target.ExtraValues.Clear();

        if (source is null)
        {
            return;
        }

        foreach (var pair in source.ExtraValues)
        {
            target.ExtraValues[pair.Key] = pair.Value;
        }
    }

    private static bool IsImmediateControlApiFailure(BackendControlApiFailure failure)
    {
        return failure.Kind is
            BackendControlApiFailureKind.Rejected or
            BackendControlApiFailureKind.Unauthorized or
            BackendControlApiFailureKind.Unknown or
            BackendControlApiFailureKind.Incompatible;
    }

    private static void EnsureCompatibleControlApiConfigResponse(BackendControlConfigResponse configResponse)
    {
        if (string.IsNullOrWhiteSpace(configResponse.ConfigPath))
        {
            throw new InvalidOperationException(BackendControlApiService.LegacyConfigContractMessage);
        }
    }

    private static string BuildRestoreConfirmationMessage(
        LocalStateSnapshotDescriptor snapshot,
        LocalStateSnapshotPreviewResult preview)
    {
        var presentation = LocalStateSnapshotPresentationBuilder.BuildSelectionPresentation(snapshot, preview);

        return string.Join(
            Environment.NewLine,
            [
                $"恢复快照：{snapshot.FileName}",
                snapshot.Summary,
                "会被覆盖的内容：",
                presentation.ImpactText,
                string.Empty,
                "恢复前建议：",
                presentation.SafetyHeadlineText,
                presentation.SafetyRecommendationText,
                presentation.RollbackHintText,
                string.Empty,
                "当前状态 vs 快照",
                presentation.DiffText,
                presentation.AdviceText,
                "是否继续？"
            ]);
    }

    private static string BuildDeleteConfirmationMessage(LocalStateSnapshotDescriptor snapshot)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"删除快照：{snapshot.FileName}",
                snapshot.Summary,
                "这会从本地快照目录中永久移除当前选中的归档。",
                "是否继续？"
            ]);
    }

    private DesktopCommandResult BuildResult(
        bool succeeded,
        string statusText,
        IReadOnlyList<string>? logMessages = null,
        IReadOnlyList<TrayNotification>? notifications = null,
        DesktopUserFacingOperationError? error = null,
        string? suggestedHealthActionKey = null)
    {
        SetSourceStatusText(statusText);
        ProjectShellState();

        return new DesktopCommandResult
        {
            Succeeded = succeeded,
            NextState = _shellState,
            StatusText = statusText,
            LogMessages = logMessages ?? [],
            Notifications = notifications ?? [],
            Error = error,
            SuggestedHealthActionKey = suggestedHealthActionKey ?? error?.SuggestedActionKey
        };
    }

    private void SetSourceStatusText(string statusText)
    {
        _sourceState = _sourceState with
        {
            UiFeedbackState = _sourceState.UiFeedbackState with
            {
                StatusText = statusText
            }
        };
    }
}
