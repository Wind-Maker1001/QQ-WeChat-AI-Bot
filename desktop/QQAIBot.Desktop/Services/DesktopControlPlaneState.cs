using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public sealed record DesktopConfigEditorState
{
    public BotConfig Config { get; init; } = new();

    public string ControlApiToken { get; init; } = string.Empty;

    public bool HasUnsavedChanges { get; init; }

    public string LastLoadedAtText { get; init; } = string.Empty;

    public string LastSavedAtText { get; init; } = string.Empty;
}

public sealed record DesktopRuntimeSourceState
{
    public BackendRuntimeSnapshotViewState RuntimeSnapshot { get; init; } = new();

    public BackendControlApiPollState ControlApiPollState { get; init; } = new();

    public bool IsProcessRunning { get; init; }

    public bool AutoStartEnabled { get; init; }

    public bool CanStartBackend { get; init; }

    public bool ControlApiRecoveryInProgress { get; init; }
}

public sealed record DesktopRuntimeSnapshotState
{
    public BackendRuntimeSnapshotViewState RuntimeSnapshot { get; init; } = new();

    public DesktopLatestTurnOverview LatestTurnOverview { get; init; } = new();

    public DesktopHealthReport HealthReport { get; init; } = new();

    public DesktopGuideFlow GuideFlow { get; init; } = new();

    public BackendControlApiPollState ControlApiPollState { get; init; } = new();

    public bool IsProcessRunning { get; init; }

    public bool AutoStartEnabled { get; init; }

    public bool CanStartBackend { get; init; }

    public bool ControlApiRecoveryInProgress { get; init; }
}

public sealed record DesktopRecentActivityState
{
    public IReadOnlyList<BackendRecentActivityItem> QqRecentActivities { get; init; } = [];

    public IReadOnlyList<BackendRecentActivityItem> WechatRecentActivities { get; init; } = [];

    public string LastQqRequestEventKey { get; init; } = string.Empty;

    public string LastQqFailureEventKey { get; init; } = string.Empty;

    public string LastWechatRequestEventKey { get; init; } = string.Empty;

    public string LastWechatFailureEventKey { get; init; } = string.Empty;

    public bool PinSelectedQqActivity { get; init; }

    public bool PinSelectedWechatActivity { get; init; }

    public bool ShowOnlyQqFailures { get; init; }

    public bool ShowOnlyWechatFailures { get; init; }

    public BackendRecentActivityItem? SelectedQqRecentActivity { get; init; }

    public BackendRecentActivityItem? SelectedWechatRecentActivity { get; init; }
}

public sealed record DesktopSnapshotSourceState
{
    public IReadOnlyList<LocalStateSnapshotDescriptor> StateSnapshots { get; init; } = [];

    public LocalStateSnapshotDescriptor? SelectedStateSnapshot { get; init; }

    public LocalStateSnapshotPreviewResult? SelectedStateSnapshotPreview { get; init; }

    public LocalStateSnapshotRestoreResult? LastStateRestoreResult { get; init; }

    public LocalStateSnapshotPreviewResult? LastStateRestorePreview { get; init; }

    public string LastStateSnapshotText { get; init; } = "尚未导出状态快照";

    public string LastStateRestoreText { get; init; } = "尚未恢复状态快照";
}

public sealed record DesktopSnapshotState
{
    public IReadOnlyList<LocalStateSnapshotDescriptor> StateSnapshots { get; init; } = [];

    public LocalStateSnapshotDescriptor? SelectedStateSnapshot { get; init; }

    public LocalStateSnapshotPreviewResult? SelectedStateSnapshotPreview { get; init; }

    public LocalStateSnapshotRestoreResult? LastStateRestoreResult { get; init; }

    public LocalStateSnapshotPreviewResult? LastStateRestorePreview { get; init; }

    public string LastStateSnapshotText { get; init; } = "尚未导出状态快照";

    public string LastStateRestoreText { get; init; } = "尚未恢复状态快照";

    public string LastStateRestoreSummaryText { get; init; } = string.Empty;

    public string LastStateRestoreIssueText { get; init; } = string.Empty;

    public string LastStateRestoreTargetsText { get; init; } = string.Empty;

    public string LastStateRestoreSessionsText { get; init; } = string.Empty;

    public string LastStateRestoreLatestActivityText { get; init; } = string.Empty;

    public string LastStateRestoreAdviceText { get; init; } = string.Empty;

    public string LastStateRestoreControlPlaneText { get; init; } = string.Empty;

    public string LastStateRestoreRuntimeText { get; init; } = string.Empty;

    public string LastStateRestoreNextStepText { get; init; } = string.Empty;

    public string LastStateRestorePrimaryActionLabel { get; init; } = string.Empty;

    public string LastStateRestorePrimaryActionKey { get; init; } = string.Empty;

    public string LastStateRestoreSecondaryActionLabel { get; init; } = string.Empty;

    public string LastStateRestoreSecondaryActionKey { get; init; } = string.Empty;

    public string LastStateRestoreTertiaryActionLabel { get; init; } = string.Empty;

    public string LastStateRestoreTertiaryActionKey { get; init; } = string.Empty;

    public string SelectedStateSnapshotImpactText { get; init; } = string.Empty;

    public string SelectedStateSnapshotDiffText { get; init; } = string.Empty;

    public string SelectedStateSnapshotAdviceText { get; init; } = string.Empty;

    public string SelectedStateSnapshotSafetyHeadlineText { get; init; } = string.Empty;

    public string SelectedStateSnapshotSafetyRecommendationText { get; init; } = string.Empty;

    public string SelectedStateSnapshotRollbackHintText { get; init; } = string.Empty;
}

public sealed record DesktopConfigDocumentState
{
    public EnvDocument Document { get; init; } = new();
}

public sealed record DesktopLocalDocumentSourceState
{
    public string BackendRootPath { get; init; } = string.Empty;

    public bool BackendRootDetected { get; init; }

    public DesktopConfigDocumentState ConfigDocument { get; init; } = new();
}

public sealed record DesktopLocalDocumentState
{
    public string BackendRootPath { get; init; } = string.Empty;

    public bool BackendRootDetected { get; init; }

    public bool IsBackendRootValid { get; init; }

    public string RuntimeConfigPath { get; init; } = string.Empty;

    public string BootstrapEnvPath { get; init; } = string.Empty;

    public string BackendRootStateText { get; init; } = string.Empty;

    public string SessionStorePathText { get; init; } = string.Empty;

    public string SessionStoreStateText { get; init; } = string.Empty;

    public string ImageCachePathText { get; init; } = string.Empty;

    public string ImageCacheStateText { get; init; } = string.Empty;

    public string ActivityStatePathText { get; init; } = string.Empty;

    public string StateSnapshotFolderPathText { get; init; } = string.Empty;

    public string ControlApiEndpointText { get; init; } = string.Empty;

    public string ControlApiTokenStateText { get; init; } = string.Empty;

    public DesktopConfigDocumentState ConfigDocument { get; init; } = new();
}

public sealed record DesktopUiFeedbackState
{
    public string StatusText { get; init; } = string.Empty;

    public string LogText { get; init; } = string.Empty;
}

public sealed record DesktopShellSourceState
{
    public DesktopConfigEditorState ConfigEditorState { get; init; } = new();

    public DesktopRuntimeSourceState RuntimeSourceState { get; init; } = new();

    public DesktopRecentActivityState RecentActivityState { get; init; } = new();

    public DesktopSnapshotSourceState SnapshotSourceState { get; init; } = new();

    public DesktopLocalDocumentSourceState LocalDocumentSourceState { get; init; } = new();

    public DesktopUiFeedbackState UiFeedbackState { get; init; } = new();
}

public sealed record DesktopShellViewState
{
    public DesktopRuntimeSnapshotState RuntimeShellState { get; init; } = new();

    public DesktopSnapshotState SnapshotState { get; init; } = new();

    public DesktopLocalDocumentState LocalDocumentState { get; init; } = new();
}

public sealed record DesktopShellState
{
    public DesktopConfigEditorState ConfigEditorState { get; init; } = new();

    public DesktopRuntimeSnapshotState RuntimeShellState { get; init; } = new();

    public DesktopRecentActivityState RecentActivityState { get; init; } = new();

    public DesktopSnapshotState SnapshotState { get; init; } = new();

    public DesktopLocalDocumentState LocalDocumentState { get; init; } = new();

    public DesktopUiFeedbackState UiFeedbackState { get; init; } = new();
}

public sealed record DesktopCommandResult
{
    public bool Succeeded { get; init; }

    public DesktopShellState? NextState { get; init; }

    public string StatusText { get; init; } = string.Empty;

    public IReadOnlyList<string> LogMessages { get; init; } = [];

    public IReadOnlyList<TrayNotification> Notifications { get; init; } = [];

    public DesktopUserFacingOperationError? Error { get; init; }

    public string? SuggestedHealthActionKey { get; init; }
}

public sealed record DesktopConfirmationPrompt
{
    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string ArchivePath { get; init; } = string.Empty;
}

public sealed record DesktopShellProjectionContext
{
    public BackendControlApiFailure ControlApiFailure { get; init; } = new();

    public DesktopActivityStateStoragePolicy ActivityStateStoragePolicy { get; init; } = new();

    public string? SelectedSnapshotDiffTextOverride { get; init; }

    public string? SelectedSnapshotAdviceTextOverride { get; init; }
}

public sealed record DesktopSessionDependencies
{
    public required ILocalConfigFallbackReader LocalConfigFallbackReader { get; init; }

    public required ILocalBootstrapConfigStore LocalBootstrapConfigStore { get; init; }

    public required ILocalPathOperationsService LocalPathOperationsService { get; init; }

    public required ILocalStateSnapshotService LocalStateSnapshotService { get; init; }

    public required IBackendControlApiService BackendControlApiService { get; init; }

    public required IBotProcessService BotProcessService { get; init; }

    public required IActivityStateStore ActivityStateStore { get; init; }

    public required DesktopActivityStatePolicy ActivityStatePolicy { get; init; }
}
