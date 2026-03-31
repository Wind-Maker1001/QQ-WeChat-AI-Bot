using System.IO;

using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class DesktopShellProjector
{
    public static DesktopShellSourceState CreateSourceState(DesktopShellState shellState)
    {
        return new DesktopShellSourceState
        {
            ConfigEditorState = shellState.ConfigEditorState,
            RuntimeSourceState = new DesktopRuntimeSourceState
            {
                RuntimeSnapshot = shellState.RuntimeShellState.RuntimeSnapshot,
                ControlApiPollState = shellState.RuntimeShellState.ControlApiPollState,
                IsProcessRunning = shellState.RuntimeShellState.IsProcessRunning,
                AutoStartEnabled = shellState.RuntimeShellState.AutoStartEnabled,
                CanStartBackend = shellState.RuntimeShellState.CanStartBackend,
                ControlApiRecoveryInProgress = shellState.RuntimeShellState.ControlApiRecoveryInProgress
            },
            RecentActivityState = shellState.RecentActivityState,
            SnapshotSourceState = new DesktopSnapshotSourceState
            {
                StateSnapshots = shellState.SnapshotState.StateSnapshots,
                SelectedStateSnapshot = shellState.SnapshotState.SelectedStateSnapshot,
                SelectedStateSnapshotPreview = shellState.SnapshotState.SelectedStateSnapshotPreview,
                LastStateRestoreResult = shellState.SnapshotState.LastStateRestoreResult,
                LastStateRestorePreview = shellState.SnapshotState.LastStateRestorePreview,
                LastStateSnapshotText = shellState.SnapshotState.LastStateSnapshotText,
                LastStateRestoreText = shellState.SnapshotState.LastStateRestoreText
            },
            LocalDocumentSourceState = new DesktopLocalDocumentSourceState
            {
                BackendRootPath = shellState.LocalDocumentState.BackendRootPath,
                BackendRootDetected = shellState.LocalDocumentState.BackendRootDetected,
                ConfigDocument = shellState.LocalDocumentState.ConfigDocument
            },
            UiFeedbackState = shellState.UiFeedbackState
        };
    }

    public static DesktopShellViewState ProjectViewState(
        DesktopShellSourceState sourceState,
        DesktopShellProjectionContext context)
    {
        var localDocumentState = BuildLocalDocumentState(sourceState, context);
        var runtimeSnapshot = sourceState.RuntimeSourceState.RuntimeSnapshot;
        var latestTurnOverview = BackendLatestTurnOverviewBuilder.Build(runtimeSnapshot);
        var healthReport = DesktopHealthReportBuilder.Build(
            sourceState.ConfigEditorState.Config,
            runtimeSnapshot,
            context.ControlApiFailure,
            localDocumentState.IsBackendRootValid,
            sourceState.ConfigEditorState.HasUnsavedChanges,
            sourceState.RuntimeSourceState.AutoStartEnabled);
        var guideFlow = DesktopGuideFlowBuilder.Build(
            new DesktopGuideFlowContext
            {
                IsBackendRootValid = localDocumentState.IsBackendRootValid,
                HasUnsavedChanges = sourceState.ConfigEditorState.HasUnsavedChanges,
                CanStartBackend = sourceState.RuntimeSourceState.CanStartBackend,
                IsProcessRunning = sourceState.RuntimeSourceState.IsProcessRunning,
                IsQqRuntimeReady = runtimeSnapshot.RuntimeReady == true,
                AutoStartEnabled = sourceState.RuntimeSourceState.AutoStartEnabled,
                HealthLatestIssueText = healthReport.LatestIssue,
                HealthLatestIssueActionLabel = healthReport.LatestIssueActionLabel,
                HealthLatestIssueActionKey = healthReport.LatestIssueActionKey,
                HealthChecks = healthReport.Checks,
                QqRecentActivities = sourceState.RecentActivityState.QqRecentActivities,
                WechatRecentActivities = sourceState.RecentActivityState.WechatRecentActivities
            });

        return new DesktopShellViewState
        {
            RuntimeShellState = new DesktopRuntimeSnapshotState
            {
                RuntimeSnapshot = runtimeSnapshot,
                LatestTurnOverview = latestTurnOverview,
                HealthReport = healthReport,
                GuideFlow = guideFlow,
                ControlApiPollState = sourceState.RuntimeSourceState.ControlApiPollState,
                IsProcessRunning = sourceState.RuntimeSourceState.IsProcessRunning,
                AutoStartEnabled = sourceState.RuntimeSourceState.AutoStartEnabled,
                CanStartBackend = sourceState.RuntimeSourceState.CanStartBackend,
                ControlApiRecoveryInProgress = sourceState.RuntimeSourceState.ControlApiRecoveryInProgress
            },
            SnapshotState = BuildSnapshotState(sourceState, context, runtimeSnapshot),
            LocalDocumentState = localDocumentState
        };
    }

    public static DesktopShellState Project(
        DesktopShellSourceState sourceState,
        DesktopShellProjectionContext context)
    {
        var viewState = ProjectViewState(sourceState, context);

        return new DesktopShellState
        {
            ConfigEditorState = sourceState.ConfigEditorState,
            RuntimeShellState = viewState.RuntimeShellState,
            RecentActivityState = sourceState.RecentActivityState,
            SnapshotState = viewState.SnapshotState,
            LocalDocumentState = viewState.LocalDocumentState,
            UiFeedbackState = sourceState.UiFeedbackState
        };
    }

    private static DesktopSnapshotState BuildSnapshotState(
        DesktopShellSourceState sourceState,
        DesktopShellProjectionContext context,
        BackendRuntimeSnapshotViewState runtimeSnapshot)
    {
        var selectionPresentation = LocalStateSnapshotPresentationBuilder.BuildSelectionPresentation(
            sourceState.SnapshotSourceState.SelectedStateSnapshot,
            sourceState.SnapshotSourceState.SelectedStateSnapshotPreview,
            context.SelectedSnapshotDiffTextOverride,
            context.SelectedSnapshotAdviceTextOverride);
        var restorePresentation = LocalStateSnapshotPresentationBuilder.BuildRestorePresentation(
            sourceState.SnapshotSourceState.LastStateRestoreResult,
            sourceState.SnapshotSourceState.LastStateRestorePreview,
            new LocalStateSnapshotRestoreRuntimeContext
            {
                ControlApiFailure = context.ControlApiFailure,
                IsControlApiReachable = runtimeSnapshot.ControlApiReachable == true,
                CanStartBackend = sourceState.RuntimeSourceState.CanStartBackend,
                IsQqRuntimeReady = runtimeSnapshot.RuntimeReady == true,
                IsWechatConfigured = runtimeSnapshot.WechatConfigured == true || !string.IsNullOrWhiteSpace(sourceState.ConfigEditorState.Config.WechatBridgeUrl),
                IsWechatRuntimeReady = runtimeSnapshot.WechatRuntimeReady == true
            });

        return new DesktopSnapshotState
        {
            StateSnapshots = sourceState.SnapshotSourceState.StateSnapshots,
            SelectedStateSnapshot = sourceState.SnapshotSourceState.SelectedStateSnapshot,
            SelectedStateSnapshotPreview = sourceState.SnapshotSourceState.SelectedStateSnapshotPreview,
            LastStateRestoreResult = sourceState.SnapshotSourceState.LastStateRestoreResult,
            LastStateRestorePreview = sourceState.SnapshotSourceState.LastStateRestorePreview,
            LastStateSnapshotText = sourceState.SnapshotSourceState.LastStateSnapshotText,
            LastStateRestoreText = sourceState.SnapshotSourceState.LastStateRestoreText,
            LastStateRestoreSummaryText = restorePresentation.SummaryText,
            LastStateRestoreIssueText = restorePresentation.IssueText,
            LastStateRestoreTargetsText = restorePresentation.TargetsText,
            LastStateRestoreSessionsText = restorePresentation.SessionsText,
            LastStateRestoreLatestActivityText = restorePresentation.LatestActivityText,
            LastStateRestoreAdviceText = restorePresentation.AdviceText,
            LastStateRestoreControlPlaneText = restorePresentation.ControlPlaneText,
            LastStateRestoreRuntimeText = restorePresentation.RuntimeText,
            LastStateRestoreNextStepText = restorePresentation.NextStepText,
            LastStateRestorePrimaryActionLabel = restorePresentation.PrimaryAction.Label,
            LastStateRestorePrimaryActionKey = restorePresentation.PrimaryAction.Key,
            LastStateRestoreSecondaryActionLabel = restorePresentation.SecondaryAction.Label,
            LastStateRestoreSecondaryActionKey = restorePresentation.SecondaryAction.Key,
            LastStateRestoreTertiaryActionLabel = restorePresentation.TertiaryAction.Label,
            LastStateRestoreTertiaryActionKey = restorePresentation.TertiaryAction.Key,
            SelectedStateSnapshotImpactText = selectionPresentation.ImpactText,
            SelectedStateSnapshotDiffText = selectionPresentation.DiffText,
            SelectedStateSnapshotAdviceText = selectionPresentation.AdviceText,
            SelectedStateSnapshotSafetyHeadlineText = selectionPresentation.SafetyHeadlineText,
            SelectedStateSnapshotSafetyRecommendationText = selectionPresentation.SafetyRecommendationText,
            SelectedStateSnapshotRollbackHintText = selectionPresentation.RollbackHintText
        };
    }

    private static DesktopLocalDocumentState BuildLocalDocumentState(
        DesktopShellSourceState sourceState,
        DesktopShellProjectionContext context)
    {
        var backendRootPath = sourceState.LocalDocumentSourceState.BackendRootPath ?? string.Empty;
        var document = sourceState.LocalDocumentSourceState.ConfigDocument.Document;
        var isBackendRootValid = PathDiscoveryService.IsBackendRoot(backendRootPath);
        var runtimeConfigPath = Path.Combine(backendRootPath, "data", "runtime-settings.json");
        var bootstrapEnvPath = Path.Combine(backendRootPath, ".env");
        var sessionStorePath = Path.Combine(backendRootPath, "data", "sessions.json");
        var imageCachePath = Path.Combine(backendRootPath, "data", "image-cache");
        var activityStatePath = context.ActivityStateStoragePolicy.ResolveStateFilePath(backendRootPath);
        var stateSnapshotFolderPath = Path.Combine(backendRootPath, "artifacts", "state-snapshots");
        var controlApiHost = ResolveLocalExtraValue(document, "QQ_AI_BOT_CONTROL_API_HOST", "127.0.0.1");
        var controlApiPort = ResolveLocalExtraValue(document, "QQ_AI_BOT_CONTROL_API_PORT", "3199");

        return new DesktopLocalDocumentState
        {
            BackendRootPath = backendRootPath,
            BackendRootDetected = sourceState.LocalDocumentSourceState.BackendRootDetected,
            IsBackendRootValid = isBackendRootValid,
            RuntimeConfigPath = runtimeConfigPath,
            BootstrapEnvPath = bootstrapEnvPath,
            BackendRootStateText = isBackendRootValid
                ? (sourceState.LocalDocumentSourceState.BackendRootDetected
                    ? "已自动检测到 backend 根目录"
                    : "backend 根目录有效")
                : "backend 根目录无效",
            SessionStorePathText = sessionStorePath,
            SessionStoreStateText = isBackendRootValid
                ? (File.Exists(sessionStorePath)
                    ? "会话历史文件已存在"
                    : "首次保存会话后会创建历史文件")
                : "后端目录有效后才能显示会话路径",
            ImageCachePathText = imageCachePath,
            ImageCacheStateText = BuildImageCacheStateText(isBackendRootValid, imageCachePath),
            ActivityStatePathText = activityStatePath,
            StateSnapshotFolderPathText = stateSnapshotFolderPath,
            ControlApiEndpointText = $"http://{controlApiHost}:{controlApiPort}",
            ControlApiTokenStateText = BuildControlApiTokenStateText(
                sourceState.ConfigEditorState.ControlApiToken,
                context.ControlApiFailure),
            ConfigDocument = sourceState.LocalDocumentSourceState.ConfigDocument
        };
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
}
