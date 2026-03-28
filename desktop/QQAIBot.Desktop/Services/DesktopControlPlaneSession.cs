using System.IO;
using System.Linq;

using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public sealed class DesktopControlPlaneSession
{
    private const int ControlApiRecoveryAttemptThreshold = 2;
    private const int ControlApiOutageNotificationThreshold = 3;
    private const string ControlApiTokenEnvKey = "QQ_AI_BOT_CONTROL_API_TOKEN";

    private readonly DesktopSessionDependencies _dependencies;
    private DesktopShellState _shellState;
    private bool _restoringActivityState;

    public DesktopControlPlaneSession(
        DesktopSessionDependencies dependencies,
        DesktopShellState initialState)
    {
        _dependencies = dependencies;
        _shellState = initialState;
        RecalculateDerivedState();
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
        _shellState = _shellState with
        {
            ConfigEditorState = _shellState.ConfigEditorState with
            {
                Config = BuildConfigCopy(config),
                ControlApiToken = controlApiToken,
                HasUnsavedChanges = hasUnsavedChanges,
                LastLoadedAtText = lastLoadedAtText,
                LastSavedAtText = lastSavedAtText
            },
            RuntimeShellState = _shellState.RuntimeShellState with
            {
                AutoStartEnabled = autoStartEnabled,
                CanStartBackend = canStartBackend
            },
            UiFeedbackState = _shellState.UiFeedbackState with
            {
                LogText = logText
            }
        };

        RecalculateDerivedState();
        return _shellState;
    }

    public DesktopShellState UpdateBackendRoot(string backendRootPath, bool backendRootDetected)
    {
        _shellState = _shellState with
        {
            LocalDocumentState = _shellState.LocalDocumentState with
            {
                BackendRootPath = backendRootPath ?? string.Empty,
                BackendRootDetected = backendRootDetected
            },
            SnapshotState = _shellState.SnapshotState with
            {
                StateSnapshots = [],
                SelectedStateSnapshot = null,
                SelectedStateSnapshotPreview = null,
                LastStateRestoreResult = null,
                LastStateRestorePreview = null
            }
        };

        LoadActivityStateCore();
        RecalculateDerivedState();
        return _shellState;
    }

    public async Task<DesktopCommandResult> InitializeAsync()
    {
        return await LoadConfigAsync();
    }

    public async Task<(BackendControlConfigResponse? ApiConfig, BackendRuntimeStatus? ApiStatus)> LoadAuthoritativeConfigAsync()
    {
        return await BackendControlPlaneFacade.LoadAuthoritativeConfigAsync(
            tryGetConfigAsync: (cancellationToken) => _dependencies.BackendControlApiService.TryGetConfigAsync(cancellationToken),
            getLastFailure: () => _dependencies.BackendControlApiService.LastFailure,
            tryGetStatusAsync: (cancellationToken) => _dependencies.BackendControlApiService.TryGetStatusAsync(cancellationToken),
            isImmediateFailure: IsImmediateControlApiFailure,
            tryRecoverControlApiAsync: () => TryRecoverControlApiAsyncInternal("load-config"));
    }

    public async Task<DesktopCommandResult> LoadConfigAsync()
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
                logMessages: ["Cannot load config because backend root is invalid."]);
        }

        try
        {
            _shellState = _shellState with
            {
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = "Loading config..."
                }
            };

            var localEnvDocument = await LoadLocalEnvDocumentAsync();
            var loadResult = await LoadAuthoritativeConfigAsync();
            var apiConfig = loadResult.ApiConfig;
            var apiStatus = loadResult.ApiStatus;
            var nextDocument = apiConfig is not null
                ? new EnvDocument
                {
                    Config = BuildConfigCopy(apiConfig)
                }
                : localEnvDocument;

            CopyLocalExtraValues(localEnvDocument, nextDocument);
            ApplyControlApiAccessToken(nextDocument);

            _shellState = _shellState with
            {
                LocalDocumentState = _shellState.LocalDocumentState with
                {
                    ConfigDocument = new DesktopConfigDocumentState
                    {
                        Document = nextDocument
                    }
                },
                ConfigEditorState = _shellState.ConfigEditorState with
                {
                    Config = BuildConfigCopy(nextDocument.Config),
                    ControlApiToken = ResolveControlApiToken(nextDocument),
                    HasUnsavedChanges = false,
                    LastLoadedAtText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                }
            };

            ApplyBackendRuntimeStatusCore(apiStatus, apiStatus is not null);
            await RefreshStateSnapshotsAsyncInternal(selectArchivePath: null);
            _shellState = _shellState with
            {
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = apiConfig?.RestartRequired == true
                        ? "Config loaded (restart required)"
                        : "Config loaded"
                }
            };
            RecalculateDerivedState();

            return BuildResult(
                succeeded: true,
                statusText: _shellState.UiFeedbackState.StatusText,
                logMessages:
                [
                    apiConfig is not null
                        ? $"Loaded config via control API: {apiConfig.EnvPath}"
                        : $"Loaded config from file: {EnvFilePath()}"
                ]);
        }
        catch (Exception ex)
        {
            var userFacingError = DesktopOperationErrorFormatter.Build(
                operationLabel: "Load config",
                fallbackStatusText: "Load failed",
                technicalMessage: ex.Message,
                controlApiFailure: _dependencies.BackendControlApiService.LastFailure,
                envPath: EnvFilePath(),
                canStartBackend: _shellState.RuntimeShellState.CanStartBackend);

            _shellState = _shellState with
            {
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = userFacingError.StatusText
                }
            };

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
                logMessages: ["Cannot save local control-plane settings because backend root is invalid."]);
        }

        try
        {
            var normalizedToken = controlApiToken?.Trim() ?? string.Empty;
            await _dependencies.LocalBootstrapConfigStore.SaveExtraValueAsync(
                _shellState.LocalDocumentState.BackendRootPath,
                ControlApiTokenEnvKey,
                normalizedToken);

            var nextDocument = CloneEnvDocument(_shellState.LocalDocumentState.ConfigDocument.Document);
            if (string.IsNullOrWhiteSpace(normalizedToken))
            {
                nextDocument.ExtraValues.Remove(ControlApiTokenEnvKey);
            }
            else
            {
                nextDocument.ExtraValues[ControlApiTokenEnvKey] = normalizedToken;
            }

            ApplyControlApiAccessToken(nextDocument);
            _shellState = _shellState with
            {
                LocalDocumentState = _shellState.LocalDocumentState with
                {
                    ConfigDocument = new DesktopConfigDocumentState
                    {
                        Document = nextDocument
                    }
                },
                ConfigEditorState = _shellState.ConfigEditorState with
                {
                    ControlApiToken = normalizedToken
                },
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = "Local control-plane settings saved"
                }
            };
            RecalculateDerivedState();

            return BuildResult(
                succeeded: true,
                statusText: "Local control-plane settings saved",
                logMessages: [$"Saved local control-plane token to {EnvFilePath()}"]);
        }
        catch (Exception ex)
        {
            var userFacingError = DesktopOperationErrorFormatter.Build(
                operationLabel: "Save local control settings",
                fallbackStatusText: "Local control-plane save failed",
                technicalMessage: ex.Message,
                envPath: EnvFilePath(),
                localControlSettingsOperation: true,
                canStartBackend: _shellState.RuntimeShellState.CanStartBackend);

            _shellState = _shellState with
            {
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = userFacingError.StatusText
                }
            };

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
                logMessages: ["Cannot save config because backend root is invalid."]);
        }

        try
        {
            _shellState = _shellState with
            {
                ConfigEditorState = _shellState.ConfigEditorState with
                {
                    Config = BuildConfigCopy(config)
                },
                LocalDocumentState = _shellState.LocalDocumentState with
                {
                    ConfigDocument = _shellState.LocalDocumentState.ConfigDocument with
                    {
                        Document = CloneEnvDocument(_shellState.LocalDocumentState.ConfigDocument.Document, config)
                    }
                },
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = "Saving config..."
                }
            };

            var apiResult = await SaveConfigThroughControlApiAsync(config);
            var mergedConfig = MergeSavedConfig(config, apiResult);
            var nextDocument = CloneEnvDocument(_shellState.LocalDocumentState.ConfigDocument.Document, mergedConfig);

            _shellState = _shellState with
            {
                ConfigEditorState = _shellState.ConfigEditorState with
                {
                    Config = BuildConfigCopy(mergedConfig),
                    HasUnsavedChanges = false,
                    LastSavedAtText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                },
                LocalDocumentState = _shellState.LocalDocumentState with
                {
                    ConfigDocument = _shellState.LocalDocumentState.ConfigDocument with
                    {
                        Document = nextDocument
                    }
                },
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = apiResult.RestartRequired ? "Config saved (restart required)" : "Config saved"
                }
            };
            RecalculateDerivedState();

            return BuildResult(
                succeeded: true,
                statusText: _shellState.UiFeedbackState.StatusText,
                logMessages: [$"Saved config via control API: {apiResult.EnvPath}"]);
        }
        catch (Exception ex)
        {
            var userFacingError = DesktopOperationErrorFormatter.Build(
                operationLabel: "Save config",
                fallbackStatusText: "Save failed",
                technicalMessage: ex.Message,
                controlApiFailure: _dependencies.BackendControlApiService.LastFailure,
                envPath: EnvFilePath(),
                canStartBackend: _shellState.RuntimeShellState.CanStartBackend);

            _shellState = _shellState with
            {
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = userFacingError.StatusText
                }
            };

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
            _shellState = _shellState with
            {
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = "Starting backend..."
                }
            };

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

            _shellState = _shellState with
            {
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = outcome.StatusText
                }
            };

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
                envPath: EnvFilePath(),
                canStartBackend: _shellState.RuntimeShellState.CanStartBackend);

            _shellState = _shellState with
            {
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = userFacingError.StatusText
                }
            };

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
            _shellState = _shellState with
            {
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = "Stopping backend..."
                }
            };

            var outcome = await BackendControlPlaneFacade.StopBackendAsync(
                prepareAsync: async () => { await LoadLocalEnvDocumentAsync(suppressErrors: true); },
                tryStopAsync: (cancellationToken) => _dependencies.BackendControlApiService.TryStopAsync(cancellationToken),
                getLastFailure: () => _dependencies.BackendControlApiService.LastFailure,
                isImmediateFailure: IsImmediateControlApiFailure,
                isProcessRunning: () => _dependencies.BotProcessService.IsRunning,
                stopProcessAsync: () => _dependencies.BotProcessService.StopAsync());

            ApplyBackendRuntimeStatusCore(outcome.AppliedStatus, outcome.ControlApiReachable);
            _shellState = _shellState with
            {
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = outcome.StatusText
                }
            };

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
                envPath: EnvFilePath(),
                canStartBackend: _shellState.RuntimeShellState.CanStartBackend);

            _shellState = _shellState with
            {
                UiFeedbackState = _shellState.UiFeedbackState with
                {
                    StatusText = userFacingError.StatusText
                }
            };

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
            _shellState.RuntimeShellState.RuntimeSnapshot,
            _shellState.RuntimeShellState.ControlApiPollState,
            status,
            statusFailure,
            ControlApiRecoveryAttemptThreshold,
            ControlApiOutageNotificationThreshold);

        _shellState = _shellState with
        {
            RuntimeShellState = _shellState.RuntimeShellState with
            {
                ControlApiPollState = pollOutcome.NextPollState,
                RuntimeSnapshot = pollOutcome.NextRuntimeSnapshot
            }
        };

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
        var nextState = _shellState.RecentActivityState with
        {
            PinSelectedQqActivity = pinSelectedQqActivity ?? _shellState.RecentActivityState.PinSelectedQqActivity,
            PinSelectedWechatActivity = pinSelectedWechatActivity ?? _shellState.RecentActivityState.PinSelectedWechatActivity,
            ShowOnlyQqFailures = showOnlyQqFailures ?? _shellState.RecentActivityState.ShowOnlyQqFailures,
            ShowOnlyWechatFailures = showOnlyWechatFailures ?? _shellState.RecentActivityState.ShowOnlyWechatFailures,
            SelectedQqRecentActivity = updateQqSelection ? selectedQqRecentActivity : _shellState.RecentActivityState.SelectedQqRecentActivity,
            SelectedWechatRecentActivity = updateWechatSelection ? selectedWechatRecentActivity : _shellState.RecentActivityState.SelectedWechatRecentActivity
        };

        if (showOnlyQqFailures.HasValue)
        {
            nextState = nextState with
            {
                SelectedQqRecentActivity = BackendRecentActivityCoordinator.ResolveSelectionAfterFilterChange(
                    nextState.QqRecentActivities,
                    nextState.SelectedQqRecentActivity,
                    nextState.ShowOnlyQqFailures)
            };
        }

        if (showOnlyWechatFailures.HasValue)
        {
            nextState = nextState with
            {
                SelectedWechatRecentActivity = BackendRecentActivityCoordinator.ResolveSelectionAfterFilterChange(
                    nextState.WechatRecentActivities,
                    nextState.SelectedWechatRecentActivity,
                    nextState.ShowOnlyWechatFailures)
            };
        }

        _shellState = _shellState with
        {
            RecentActivityState = nextState
        };

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
        _shellState = _shellState with
        {
            SnapshotState = _shellState.SnapshotState with
            {
                SelectedStateSnapshot = snapshot,
                SelectedStateSnapshotPreview = null
            }
        };
        ApplySelectedStateSnapshotPresentationCore();
        return _shellState;
    }

    public async Task<DesktopCommandResult> RefreshSelectedStateSnapshotPreviewAsync()
    {
        var selectedSnapshot = _shellState.SnapshotState.SelectedStateSnapshot;

        if (selectedSnapshot is null || !IsBackendRootValid())
        {
            _shellState = _shellState with
            {
                SnapshotState = _shellState.SnapshotState with
                {
                    SelectedStateSnapshotPreview = null
                }
            };
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

            _shellState = _shellState with
            {
                SnapshotState = _shellState.SnapshotState with
                {
                    SelectedStateSnapshotPreview = preview
                }
            };
            ApplySelectedStateSnapshotPresentationCore();
        }
        catch (Exception ex)
        {
            if (string.Equals(
                    _shellState.SnapshotState.SelectedStateSnapshot?.ArchivePath,
                    selectedSnapshot.ArchivePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                _shellState = _shellState with
                {
                    SnapshotState = _shellState.SnapshotState with
                    {
                        SelectedStateSnapshotPreview = null
                    }
                };
                ApplySelectedStateSnapshotPresentationCore(
                    diffTextOverride: $"Diff preview unavailable: {ex.Message}",
                    adviceTextOverride: "Review the snapshot details carefully before restoring.");
            }
        }

        return BuildResult(true, _shellState.UiFeedbackState.StatusText);
    }

    public async Task<DesktopCommandResult> RestoreLatestStateSnapshotAsync()
    {
        var result = await _dependencies.LocalStateSnapshotService.RestoreLatestAsync(_shellState.LocalDocumentState.BackendRootPath);
        _shellState = _shellState with
        {
            SnapshotState = _shellState.SnapshotState with
            {
                LastStateRestoreText = result.ArchivePath,
                LastStateRestoreResult = result
            }
        };
        await LoadConfigAsync();
        ApplyRestorePresentationCore();
        return BuildResult(true, _shellState.UiFeedbackState.StatusText, [$"已从 {result.ArchivePath} 恢复本地状态快照，共恢复 {result.RestoredEntries.Count} 项。"]);
    }

    public async Task<DesktopCommandResult> RestoreSelectedStateSnapshotAsync(string archivePath)
    {
        var result = await _dependencies.LocalStateSnapshotService.RestoreAsync(
            _shellState.LocalDocumentState.BackendRootPath,
            archivePath);
        _shellState = _shellState with
        {
            SnapshotState = _shellState.SnapshotState with
            {
                LastStateRestoreText = result.ArchivePath,
                LastStateRestoreResult = result
            }
        };
        await LoadConfigAsync();
        ApplyRestorePresentationCore();
        return BuildResult(true, _shellState.UiFeedbackState.StatusText, [$"已从 {result.ArchivePath} 恢复选中状态快照，共恢复 {result.RestoredEntries.Count} 项。"]);
    }

    public async Task<DesktopCommandResult> DeleteSelectedStateSnapshotAsync(string archivePath)
    {
        await _dependencies.LocalStateSnapshotService.DeleteAsync(archivePath);
        await RefreshStateSnapshotsAsyncInternal(selectArchivePath: null);
        return BuildResult(true, _shellState.UiFeedbackState.StatusText);
    }

    private async Task<BackendControlConfigResponse> SaveConfigThroughControlApiAsync(BotConfig config)
    {
        return await BackendControlPlaneFacade.SaveConfigAsync(
            prepareAsync: async () => { await LoadLocalEnvDocumentAsync(suppressErrors: true); },
            config,
            trySaveConfigAsync: (submittedConfig, cancellationToken) => _dependencies.BackendControlApiService.TrySaveConfigAsync(submittedConfig, cancellationToken),
            getLastFailure: () => _dependencies.BackendControlApiService.LastFailure,
            tryGetStatusAsync: (cancellationToken) => _dependencies.BackendControlApiService.TryGetStatusAsync(cancellationToken),
            isImmediateFailure: IsImmediateControlApiFailure,
            tryRecoverControlApiAsync: () => TryRecoverControlApiAsyncInternal("save-config"));
    }

    private async Task<DesktopCommandResult> TryRecoverControlApiAsyncInternal(string reason)
    {
        if (_shellState.RuntimeShellState.ControlApiRecoveryInProgress || !IsBackendRootValid())
        {
            return BuildResult(true, _shellState.UiFeedbackState.StatusText);
        }

        _shellState = _shellState with
        {
            RuntimeShellState = _shellState.RuntimeShellState with
            {
                ControlApiRecoveryInProgress = true
            }
        };

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
            _shellState = _shellState with
            {
                RuntimeShellState = _shellState.RuntimeShellState with
                {
                    ControlApiRecoveryInProgress = false
                }
            };
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
            var document = await _dependencies.LocalConfigFallbackReader.LoadAsync(_shellState.LocalDocumentState.BackendRootPath);
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

        _shellState = _shellState with
        {
            RecentActivityState = _shellState.RecentActivityState with
            {
                QqRecentActivities = projection.QqActivity.Items.ToArray(),
                WechatRecentActivities = projection.WechatActivity.Items.ToArray(),
                LastQqRequestEventKey = projection.QqActivity.LastRequestEventKey,
                LastQqFailureEventKey = projection.QqActivity.LastFailureEventKey,
                SelectedQqRecentActivity = projection.QqActivity.SelectedItem,
                LastWechatRequestEventKey = projection.WechatActivity.LastRequestEventKey,
                LastWechatFailureEventKey = projection.WechatActivity.LastFailureEventKey,
                SelectedWechatRecentActivity = projection.WechatActivity.SelectedItem
            },
            RuntimeShellState = _shellState.RuntimeShellState with
            {
                IsProcessRunning = projection.SnapshotState.RuntimeActive == true,
                RuntimeSnapshot = projection.SnapshotState
            }
        };

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

        ApplyActivityStateCore(_dependencies.ActivityStateStore.Load(_shellState.LocalDocumentState.BackendRootPath));
    }

    private void PersistActivityStateIfPossible()
    {
        if (_restoringActivityState || !IsBackendRootValid())
        {
            return;
        }

        var state = _dependencies.ActivityStatePolicy.CreateSnapshot(
            _shellState.RecentActivityState.QqRecentActivities,
            _shellState.RecentActivityState.WechatRecentActivities,
            _shellState.RecentActivityState.SelectedQqRecentActivity,
            _shellState.RecentActivityState.SelectedWechatRecentActivity,
            _shellState.RecentActivityState.PinSelectedQqActivity,
            _shellState.RecentActivityState.PinSelectedWechatActivity,
            _shellState.RecentActivityState.ShowOnlyQqFailures,
            _shellState.RecentActivityState.ShowOnlyWechatFailures);

        _dependencies.ActivityStateStore.Save(_shellState.LocalDocumentState.BackendRootPath, state);
    }

    private void ApplyActivityStateCore(DesktopActivityState? state)
    {
        var projection = BackendRuntimeSnapshotCoordinator.ProjectActivityRestore(
            _dependencies.ActivityStatePolicy,
            state);

        _restoringActivityState = true;
        try
        {
            _shellState = _shellState with
            {
                RecentActivityState = _shellState.RecentActivityState with
                {
                    QqRecentActivities = projection.QqRecentActivities,
                    WechatRecentActivities = projection.WechatRecentActivities,
                    PinSelectedQqActivity = projection.PinSelectedQqActivity,
                    PinSelectedWechatActivity = projection.PinSelectedWechatActivity,
                    ShowOnlyQqFailures = projection.ShowOnlyQqFailures,
                    ShowOnlyWechatFailures = projection.ShowOnlyWechatFailures,
                    SelectedQqRecentActivity = projection.SelectedQqRecentActivity,
                    SelectedWechatRecentActivity = projection.SelectedWechatRecentActivity
                }
            };
        }
        finally
        {
            _restoringActivityState = false;
        }

        _shellState = _shellState with
        {
            RecentActivityState = _shellState.RecentActivityState with
            {
                SelectedQqRecentActivity = BackendRecentActivityCoordinator.ResolveSelectionAfterFilterChange(
                    _shellState.RecentActivityState.QqRecentActivities,
                    _shellState.RecentActivityState.SelectedQqRecentActivity,
                    _shellState.RecentActivityState.ShowOnlyQqFailures),
                SelectedWechatRecentActivity = BackendRecentActivityCoordinator.ResolveSelectionAfterFilterChange(
                    _shellState.RecentActivityState.WechatRecentActivities,
                    _shellState.RecentActivityState.SelectedWechatRecentActivity,
                    _shellState.RecentActivityState.ShowOnlyWechatFailures)
            }
        };

        RecalculateDerivedState();
    }

    private void ResetControlApiFailureState()
    {
        _shellState = _shellState with
        {
            RuntimeShellState = _shellState.RuntimeShellState with
            {
                ControlApiPollState = new BackendControlApiPollState(),
                ControlApiRecoveryInProgress = false
            }
        };
    }

    private async Task RefreshStateSnapshotsAsyncInternal(string? selectArchivePath)
    {
        if (!IsBackendRootValid())
        {
            _shellState = _shellState with
            {
                SnapshotState = _shellState.SnapshotState with
                {
                    StateSnapshots = [],
                    SelectedStateSnapshot = null
                }
            };
            ApplySelectedStateSnapshotPresentationCore();
            return;
        }

        var snapshots = await _dependencies.LocalStateSnapshotService.ListAsync(_shellState.LocalDocumentState.BackendRootPath);
        var selectedArchivePath = !string.IsNullOrWhiteSpace(selectArchivePath)
            ? selectArchivePath
            : _shellState.SnapshotState.SelectedStateSnapshot?.ArchivePath;

        var selectedSnapshot = snapshots.FirstOrDefault(
            (snapshot) => string.Equals(snapshot.ArchivePath, selectedArchivePath, StringComparison.OrdinalIgnoreCase))
            ?? snapshots.FirstOrDefault();

        _shellState = _shellState with
        {
            SnapshotState = _shellState.SnapshotState with
            {
                StateSnapshots = snapshots,
                SelectedStateSnapshot = selectedSnapshot,
                SelectedStateSnapshotPreview = null
            }
        };
        ApplySelectedStateSnapshotPresentationCore();
    }

    private void RecalculateDerivedState()
    {
        var runtimeSnapshot = _shellState.RuntimeShellState.RuntimeSnapshot;
        var latestTurnOverview = BackendLatestTurnOverviewBuilder.Build(runtimeSnapshot);
        var healthReport = DesktopHealthReportBuilder.Build(
            _shellState.ConfigEditorState.Config,
            runtimeSnapshot,
            _dependencies.BackendControlApiService.LastFailure,
            IsBackendRootValid(),
            _shellState.ConfigEditorState.HasUnsavedChanges,
            _shellState.RuntimeShellState.AutoStartEnabled);
        var guideFlow = DesktopGuideFlowBuilder.Build(
            new DesktopGuideFlowContext
            {
                IsBackendRootValid = IsBackendRootValid(),
                HasUnsavedChanges = _shellState.ConfigEditorState.HasUnsavedChanges,
                CanStartBackend = _shellState.RuntimeShellState.CanStartBackend,
                IsProcessRunning = _shellState.RuntimeShellState.IsProcessRunning,
                IsQqRuntimeReady = runtimeSnapshot.RuntimeReady == true,
                AutoStartEnabled = _shellState.RuntimeShellState.AutoStartEnabled,
                HealthLatestIssueText = healthReport.LatestIssue,
                HealthLatestIssueActionLabel = healthReport.LatestIssueActionLabel,
                HealthLatestIssueActionKey = healthReport.LatestIssueActionKey,
                HealthChecks = healthReport.Checks,
                QqRecentActivities = _shellState.RecentActivityState.QqRecentActivities,
                WechatRecentActivities = _shellState.RecentActivityState.WechatRecentActivities
            });

        _shellState = _shellState with
        {
            RuntimeShellState = _shellState.RuntimeShellState with
            {
                RuntimeSnapshot = runtimeSnapshot,
                LatestTurnOverview = latestTurnOverview,
                HealthReport = healthReport,
                GuideFlow = guideFlow
            }
        };
    }

    private void ApplySelectedStateSnapshotPresentationCore(
        string? diffTextOverride = null,
        string? adviceTextOverride = null)
    {
        var presentation = LocalStateSnapshotPresentationBuilder.BuildSelectionPresentation(
            _shellState.SnapshotState.SelectedStateSnapshot,
            _shellState.SnapshotState.SelectedStateSnapshotPreview,
            diffTextOverride,
            adviceTextOverride);

        _shellState = _shellState with
        {
            SnapshotState = _shellState.SnapshotState with
            {
                SelectedStateSnapshotImpactText = presentation.ImpactText,
                SelectedStateSnapshotDiffText = presentation.DiffText,
                SelectedStateSnapshotAdviceText = presentation.AdviceText,
                SelectedStateSnapshotSafetyHeadlineText = presentation.SafetyHeadlineText,
                SelectedStateSnapshotSafetyRecommendationText = presentation.SafetyRecommendationText,
                SelectedStateSnapshotRollbackHintText = presentation.RollbackHintText
            }
        };
    }

    private void ApplyRestorePresentationCore()
    {
        var presentation = LocalStateSnapshotPresentationBuilder.BuildRestorePresentation(
            _shellState.SnapshotState.LastStateRestoreResult,
            _shellState.SnapshotState.LastStateRestorePreview,
            BuildRestorePresentationContext());

        _shellState = _shellState with
        {
            SnapshotState = _shellState.SnapshotState with
            {
                LastStateRestoreSummaryText = presentation.SummaryText,
                LastStateRestoreIssueText = presentation.IssueText,
                LastStateRestoreTargetsText = presentation.TargetsText,
                LastStateRestoreSessionsText = presentation.SessionsText,
                LastStateRestoreLatestActivityText = presentation.LatestActivityText,
                LastStateRestoreAdviceText = presentation.AdviceText,
                LastStateRestoreControlPlaneText = presentation.ControlPlaneText,
                LastStateRestoreRuntimeText = presentation.RuntimeText,
                LastStateRestoreNextStepText = presentation.NextStepText,
                LastStateRestorePrimaryActionLabel = presentation.PrimaryAction.Label,
                LastStateRestorePrimaryActionKey = presentation.PrimaryAction.Key,
                LastStateRestoreSecondaryActionLabel = presentation.SecondaryAction.Label,
                LastStateRestoreSecondaryActionKey = presentation.SecondaryAction.Key,
                LastStateRestoreTertiaryActionLabel = presentation.TertiaryAction.Label,
                LastStateRestoreTertiaryActionKey = presentation.TertiaryAction.Key
            }
        };
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

    private bool IsBackendRootValid()
    {
        return PathDiscoveryService.IsBackendRoot(_shellState.LocalDocumentState.BackendRootPath);
    }

    private string EnvFilePath()
    {
        return Path.Combine(_shellState.LocalDocumentState.BackendRootPath, ".env");
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

    private static string ResolveControlApiToken(EnvDocument document)
    {
        return document.ExtraValues.TryGetValue(ControlApiTokenEnvKey, out var accessToken)
            ? accessToken
            : string.Empty;
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
            BackendControlApiFailureKind.Unknown;
    }

    private static BotConfig BuildConfigCopy(BotConfig config)
    {
        return new BotConfig
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
            NapCatWsUrl = config.NapCatWsUrl,
            NapCatToken = config.NapCatToken,
            WechatBridgeUrl = config.WechatBridgeUrl,
            WechatBridgeToken = config.WechatBridgeToken,
            WechatBotPrefix = config.WechatBotPrefix,
            BotPrefix = config.BotPrefix,
            BotSystemPrompt = config.BotSystemPrompt,
            BotPersona = config.BotPersona,
            MaxOutputChars = config.MaxOutputChars,
            AllowedChatIds = config.AllowedChatIds,
            AllowedUserIds = config.AllowedUserIds
        };
    }

    private static BotConfig MergeSavedConfig(BotConfig submittedConfig, BotConfig savedConfig)
    {
        var mergedConfig = BuildConfigCopy(savedConfig);

        if (string.IsNullOrWhiteSpace(savedConfig.BotSystemPrompt))
        {
            mergedConfig.BotSystemPrompt = submittedConfig.BotSystemPrompt;
        }

        return mergedConfig;
    }

    private static EnvDocument CloneEnvDocument(EnvDocument source, BotConfig? config = null)
    {
        var nextDocument = new EnvDocument
        {
            Config = BuildConfigCopy(config ?? source.Config)
        };
        CopyLocalExtraValues(source, nextDocument);
        return nextDocument;
    }

    private DesktopCommandResult BuildResult(
        bool succeeded,
        string statusText,
        IReadOnlyList<string>? logMessages = null,
        IReadOnlyList<TrayNotification>? notifications = null,
        DesktopUserFacingOperationError? error = null,
        string? suggestedHealthActionKey = null)
    {
        _shellState = _shellState with
        {
            UiFeedbackState = _shellState.UiFeedbackState with
            {
                StatusText = statusText
            }
        };

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
}
