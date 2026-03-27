using System.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

using QQAIBot.Desktop.Infrastructure;
using QQAIBot.Desktop.Models;
using QQAIBot.Desktop.Services;

namespace QQAIBot.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const int ControlApiRecoveryAttemptThreshold = 2;
    private const int ControlApiOutageNotificationThreshold = 3;
    private const string ControlApiHostEnvKey = "QQ_AI_BOT_CONTROL_API_HOST";
    private const string ControlApiPortEnvKey = "QQ_AI_BOT_CONTROL_API_PORT";
    private const string ControlApiTokenEnvKey = "QQ_AI_BOT_CONTROL_API_TOKEN";
    private static readonly string DefaultBotInstructionsTextValue = string.Join(
        Environment.NewLine,
        [
            "你是本地消息助手，会处理来自 QQ 和微信的消息。",
            "默认使用简体中文。",
            "回答直接、准确、简洁。",
            "不要说教。",
            "不要输出多余免责声明。",
            "不确定时明确说不确定。"
        ]);

    private readonly IAutoStartService _autoStartService;
    private readonly ILocalConfigFallbackReader _localConfigFallbackReader;
    private readonly ILocalBootstrapConfigStore _localBootstrapConfigStore;
    private readonly ILocalPathOperationsService _localPathOperationsService;
    private readonly ILocalStateSnapshotService _localStateSnapshotService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly IBackendControlApiService _backendControlApiService;
    private readonly IBotProcessService _botProcessService;
    private readonly IActivityStateStore _activityStateStore;
    private readonly DesktopActivityStatePolicy _activityStatePolicy;
    private readonly Dispatcher _uiDispatcher;
    private readonly Queue<string> _logLines = new();
    private readonly DispatcherTimer _logFlushTimer;
    private readonly DispatcherTimer _statusPollTimer;
    private readonly object _logSyncRoot = new();

    private readonly AsyncRelayCommand _reloadCommand;
    private readonly AsyncRelayCommand _saveCommand;
    private readonly AsyncRelayCommand _saveLocalControlPlaneCommand;
    private readonly AsyncRelayCommand _startCommand;
    private readonly AsyncRelayCommand _stopCommand;
    private readonly AsyncRelayCommand _toggleAutoStartCommand;
    private readonly RelayCommand _autoDetectCommand;
    private readonly RelayCommand _openBackendFolderCommand;
    private readonly RelayCommand _openSessionStoreFolderCommand;
    private readonly RelayCommand _openImageCacheFolderCommand;
    private readonly RelayCommand _clearImageCacheCommand;
    private readonly AsyncRelayCommand _exportStateSnapshotCommand;
    private readonly AsyncRelayCommand _exportSafeStateSnapshotCommand;
    private readonly AsyncRelayCommand _restoreLatestStateSnapshotCommand;
    private readonly AsyncRelayCommand _refreshStateSnapshotsCommand;
    private readonly AsyncRelayCommand _restoreSelectedStateSnapshotCommand;
    private readonly AsyncRelayCommand _deleteSelectedStateSnapshotCommand;
    private readonly RelayCommand _openStateSnapshotFolderCommand;
    private readonly RelayCommand _applyBaseUrlPresetCommand;
    private readonly RelayCommand _runHealthActionCommand;
    private readonly RelayCommand _clearLogsCommand;
    private readonly RelayCommand _clearQqActivityHistoryCommand;
    private readonly RelayCommand _clearWechatActivityHistoryCommand;

    private EnvDocument _envDocument = new();
    private bool _suspendDirtyTracking;
    private bool _backendRootDetected;
    private bool _autoStartEnabled;
    private bool _isProcessRunning;
    private bool _hasUnsavedChanges;
    private string _backendRootPath = string.Empty;
    private string _statusText = "Waiting to load";
    private string _openAiApiKey = string.Empty;
    private string _openAiDefaultApiKey = string.Empty;
    private string _openAiDefaultModel = "gpt-5.4";
    private string _openAiModel = "gpt-5.4";
    private string _openAiBaseUrl = string.Empty;
    private string _openAiDefaultBaseUrl = string.Empty;
    private string _openAiDefaultReasoningEffort = "medium";
    private string _openAiDefaultTextVerbosity = "medium";
    private string _openAiAdvancedTriggerPrefixes = "/5.4,/gpt,/vision,/高级,/多模态,/看图,/图片分析";
    private string _openAiAdvancedReasoningEffort = "high";
    private string _openAiAdvancedTextVerbosity = "high";
    private string _openAiDefaultEnableWebSearch = "false";
    private string _openAiDefaultEnableCodeInterpreter = "false";
    private string _openAiAdvancedEnableWebSearch = "true";
    private string _openAiAdvancedEnableCodeInterpreter = "true";
    private string _napCatWsUrl = "ws://127.0.0.1:3001";
    private string _napCatToken = string.Empty;
    private string _wechatBridgeUrl = string.Empty;
    private string _wechatBridgeToken = string.Empty;
    private string _wechatBotPrefix = "/ai";
    private string _botPrefix = "/ai";
    private string _botSystemPrompt = DefaultBotInstructionsTextValue;
    private string _botPersona = string.Empty;
    private string _maxOutputChars = "800";
    private string _allowedChatIds = string.Empty;
    private string _allowedUserIds = string.Empty;
    private string _lastLoadedAtText = "Not loaded";
    private string _lastSavedAtText = "Not saved";
    private string _logText = string.Empty;
    private bool _logDirty;
    private string _controlApiToken = string.Empty;
    private string _lastStateSnapshotText = "No state snapshot exported yet";
    private string _lastStateRestoreText = "No state snapshot restored yet";
    private string _lastStateRestoreSummaryText = "Restore result summary will appear here.";
    private string _lastStateRestoreIssueText = "Post-restore issue summary will appear here.";
    private string _lastStateRestoreTargetsText = "Restore targets will appear here.";
    private string _lastStateRestoreSessionsText = "Session summary will appear here.";
    private string _lastStateRestoreLatestActivityText = "Latest activity summary will appear here.";
    private string _lastStateRestoreAdviceText = "Post-restore advice will appear here.";
    private string _lastStateRestoreControlPlaneText = "Post-restore control plane check will appear here.";
    private string _lastStateRestoreRuntimeText = "Post-restore runtime check will appear here.";
    private string _lastStateRestoreNextStepText = "Post-restore next step will appear here.";
    private string _lastStateRestorePrimaryActionLabel = string.Empty;
    private string _lastStateRestorePrimaryActionKey = string.Empty;
    private string _lastStateRestoreSecondaryActionLabel = string.Empty;
    private string _lastStateRestoreSecondaryActionKey = string.Empty;
    private string _lastStateRestoreTertiaryActionLabel = string.Empty;
    private string _lastStateRestoreTertiaryActionKey = string.Empty;
    private string _selectedStateSnapshotDiffText = "Select a snapshot to preview differences.";
    private string _selectedStateSnapshotAdviceText = "Restore advice will appear here.";
    private LocalStateSnapshotDescriptor? _selectedStateSnapshot;
    private LocalStateSnapshotRestoreResult? _lastStateRestoreResult;
    private LocalStateSnapshotPreviewResult? _lastStateRestorePreview;
    private BackendRuntimeSnapshotViewState _runtimeSnapshot = new();
    private DesktopHealthReport _healthReport = new();
    private BackendControlApiPollState _controlApiPollState = new();
    private bool _controlApiRecoveryInProgress;
    private bool _restoringActivityState;
    private bool _disposed;
    private bool _pinSelectedQqActivity;
    private bool _pinSelectedWechatActivity;
    private bool _showOnlyQqFailures;
    private bool _showOnlyWechatFailures;
    private string _lastQqRequestEventKey = string.Empty;
    private string _lastQqFailureEventKey = string.Empty;
    private BackendRecentActivityItem? _selectedQqRecentActivity;
    private string _lastWechatRequestEventKey = string.Empty;
    private string _lastWechatFailureEventKey = string.Empty;
    private BackendRecentActivityItem? _selectedWechatRecentActivity;

    public event EventHandler<TrayNotification>? NotificationRequested;
    public event EventHandler<string>? HealthActionRequested;

    public MainViewModel(
        IAutoStartService? autoStartService = null,
        ILocalConfigFallbackReader? localConfigFallbackReader = null,
        IBackendControlApiService? backendControlApiService = null,
        IBotProcessService? botProcessService = null,
        IActivityStateStore? activityStateStore = null,
        ILocalBootstrapConfigStore? localBootstrapConfigStore = null,
        ILocalPathOperationsService? localPathOperationsService = null,
        ILocalStateSnapshotService? localStateSnapshotService = null,
        IConfirmationDialogService? confirmationDialogService = null)
    {
        _autoStartService = autoStartService ?? new AutoStartService();
        _localConfigFallbackReader = localConfigFallbackReader ?? new LocalEnvConfigFallbackReader();
        _localBootstrapConfigStore = localBootstrapConfigStore ?? new LocalEnvBootstrapConfigStore();
        _localPathOperationsService = localPathOperationsService ?? new LocalPathOperationsService();
        _localStateSnapshotService = localStateSnapshotService ?? new LocalStateSnapshotService();
        _confirmationDialogService = confirmationDialogService ?? new ConfirmationDialogService();
        _backendControlApiService = backendControlApiService ?? new BackendControlApiService();
        _botProcessService = botProcessService ?? new BotProcessService();
        _activityStateStore = activityStateStore ?? new LocalActivityStateStore();
        _activityStatePolicy = DesktopActivityStatePolicy.Default;
        _uiDispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _reloadCommand = new AsyncRelayCommand(LoadConfigAsync, CanLoadOrSave);
        _saveCommand = new AsyncRelayCommand(SaveConfigAsync, CanLoadOrSave);
        _saveLocalControlPlaneCommand = new AsyncRelayCommand(SaveLocalControlPlaneAsync, CanLoadOrSave);
        _startCommand = new AsyncRelayCommand(StartBackendAsync, CanStartBackend);
        _stopCommand = new AsyncRelayCommand(StopBackendAsync, CanStopBackend);
        _toggleAutoStartCommand = new AsyncRelayCommand(ToggleAutoStartAsync);
        _autoDetectCommand = new RelayCommand(_ => AutoDetectBackendRoot());
        _openBackendFolderCommand = new RelayCommand(
            _ => OpenBackendFolder(),
            _ => Directory.Exists(BackendRootPath)
        );
        _openSessionStoreFolderCommand = new RelayCommand(
            _ => OpenSessionStoreFolder(),
            _ => IsBackendRootValid);
        _openImageCacheFolderCommand = new RelayCommand(
            _ => OpenImageCacheFolder(),
            _ => IsBackendRootValid);
        _clearImageCacheCommand = new RelayCommand(
            _ => ClearImageCache(),
            _ => IsBackendRootValid);
        _exportStateSnapshotCommand = new AsyncRelayCommand(ExportStateSnapshotAsync, CanLoadOrSave);
        _exportSafeStateSnapshotCommand = new AsyncRelayCommand(ExportSafeStateSnapshotAsync, CanLoadOrSave);
        _restoreLatestStateSnapshotCommand = new AsyncRelayCommand(RestoreLatestStateSnapshotAsync, CanLoadOrSave);
        _refreshStateSnapshotsCommand = new AsyncRelayCommand(RefreshStateSnapshotsAsync, CanLoadOrSave);
        _restoreSelectedStateSnapshotCommand = new AsyncRelayCommand(
            RestoreSelectedStateSnapshotAsync,
            () => IsBackendRootValid && SelectedStateSnapshot is not null);
        _deleteSelectedStateSnapshotCommand = new AsyncRelayCommand(
            DeleteSelectedStateSnapshotAsync,
            () => IsBackendRootValid && SelectedStateSnapshot is not null);
        _openStateSnapshotFolderCommand = new RelayCommand(
            _ => OpenStateSnapshotFolder(),
            _ => IsBackendRootValid);
        _applyBaseUrlPresetCommand = new RelayCommand(ApplyBaseUrlPreset);
        _runHealthActionCommand = new RelayCommand(ExecuteHealthAction);
        _clearLogsCommand = new RelayCommand(_ => ClearLogs());
        _clearQqActivityHistoryCommand = new RelayCommand(
            _ => ClearActivityHistory(QqRecentActivities, () => SelectedQqRecentActivity = null, () => PinSelectedQqActivity = false),
            _ => QqRecentActivities.Count > 0);
        _clearWechatActivityHistoryCommand = new RelayCommand(
            _ => ClearActivityHistory(WechatRecentActivities, () => SelectedWechatRecentActivity = null, () => PinSelectedWechatActivity = false),
            _ => WechatRecentActivities.Count > 0);
        QqRecentActivitiesView = CollectionViewSource.GetDefaultView(QqRecentActivities);
        QqRecentActivitiesView.Filter = FilterQqRecentActivity;
        WechatRecentActivitiesView = CollectionViewSource.GetDefaultView(WechatRecentActivities);
        WechatRecentActivitiesView.Filter = FilterWechatRecentActivity;

        _botProcessService.LogReceived += OnProcessLogReceived;
        _botProcessService.ProcessExited += OnProcessExited;

        _logFlushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(160)
        };
        _logFlushTimer.Tick += OnLogFlushTimerTick;
        _logFlushTimer.Start();

        _statusPollTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(4)
        };
        _statusPollTimer.Tick += OnStatusPollTimerTick;
        _statusPollTimer.Start();

        RefreshAutoStartState();
        AutoDetectBackendRoot();
        RefreshHealthReport();
        AddLog("Desktop UI initialized.");
    }

    public ICommand ReloadCommand => _reloadCommand;
    public ICommand SaveCommand => _saveCommand;
    public ICommand SaveLocalControlPlaneCommand => _saveLocalControlPlaneCommand;
    public ICommand StartCommand => _startCommand;
    public ICommand StopCommand => _stopCommand;
    public ICommand ToggleAutoStartCommand => _toggleAutoStartCommand;
    public ICommand AutoDetectCommand => _autoDetectCommand;
    public ICommand OpenBackendFolderCommand => _openBackendFolderCommand;
    public ICommand OpenSessionStoreFolderCommand => _openSessionStoreFolderCommand;
    public ICommand OpenImageCacheFolderCommand => _openImageCacheFolderCommand;
    public ICommand ClearImageCacheCommand => _clearImageCacheCommand;
    public ICommand ExportStateSnapshotCommand => _exportStateSnapshotCommand;
    public ICommand ExportSafeStateSnapshotCommand => _exportSafeStateSnapshotCommand;
    public ICommand RestoreLatestStateSnapshotCommand => _restoreLatestStateSnapshotCommand;
    public ICommand RefreshStateSnapshotsCommand => _refreshStateSnapshotsCommand;
    public ICommand RestoreSelectedStateSnapshotCommand => _restoreSelectedStateSnapshotCommand;
    public ICommand DeleteSelectedStateSnapshotCommand => _deleteSelectedStateSnapshotCommand;
    public ICommand OpenStateSnapshotFolderCommand => _openStateSnapshotFolderCommand;
    public ICommand ApplyBaseUrlPresetCommand => _applyBaseUrlPresetCommand;
    public ICommand RunHealthActionCommand => _runHealthActionCommand;
    public ICommand ClearLogsCommand => _clearLogsCommand;
    public ICommand ClearQqActivityHistoryCommand => _clearQqActivityHistoryCommand;
    public ICommand ClearWechatActivityHistoryCommand => _clearWechatActivityHistoryCommand;

    public ObservableCollection<BackendRecentActivityItem> QqRecentActivities { get; } = [];

    public ObservableCollection<BackendRecentActivityItem> WechatRecentActivities { get; } = [];

    public ObservableCollection<DesktopHealthCheckItem> HealthChecks { get; } = [];

    public ObservableCollection<LocalStateSnapshotDescriptor> StateSnapshots { get; } = [];

    public ICollectionView QqRecentActivitiesView { get; }

    public ICollectionView WechatRecentActivitiesView { get; }

    public string BackendRootPath
    {
        get => _backendRootPath;
        set
        {
            if (SetProperty(ref _backendRootPath, value))
            {
                LastStateSnapshotText = "No state snapshot exported yet";
                ResetRestoreResultState();
                ReplaceStateSnapshots([]);
                SelectedStateSnapshot = null;
                OnPropertyChanged(nameof(EnvFilePath));
                OnPropertyChanged(nameof(IsBackendRootValid));
                OnPropertyChanged(nameof(BackendRootStateText));
                LoadActivityState();
                RefreshHealthReport();
                UpdateCommandStates();
            }
        }
    }

    public string EnvFilePath => Path.Combine(BackendRootPath, ".env");

    public bool IsBackendRootValid => PathDiscoveryService.IsBackendRoot(BackendRootPath);

    public string BackendRootStateText => IsBackendRootValid
        ? (_backendRootDetected ? "Backend root auto-detected" : "Backend root is valid")
        : "Backend root is invalid";

    public string OpenAiApiKey
    {
        get => _openAiApiKey;
        set => SetTrackedProperty(ref _openAiApiKey, value);
    }

    public string OpenAiDefaultApiKey
    {
        get => _openAiDefaultApiKey;
        set => SetTrackedProperty(ref _openAiDefaultApiKey, value);
    }

    public string OpenAiDefaultModel
    {
        get => _openAiDefaultModel;
        set => SetTrackedProperty(ref _openAiDefaultModel, value);
    }

    public string OpenAiModel
    {
        get => _openAiModel;
        set => SetTrackedProperty(ref _openAiModel, value);
    }

    public string OpenAiBaseUrl
    {
        get => _openAiBaseUrl;
        set => SetTrackedProperty(ref _openAiBaseUrl, value);
    }

    public string OpenAiDefaultBaseUrl
    {
        get => _openAiDefaultBaseUrl;
        set => SetTrackedProperty(ref _openAiDefaultBaseUrl, value);
    }

    public string OpenAiAdvancedTriggerPrefixes
    {
        get => _openAiAdvancedTriggerPrefixes;
        set => SetTrackedProperty(ref _openAiAdvancedTriggerPrefixes, value);
    }

    public string OpenAiDefaultReasoningEffort
    {
        get => _openAiDefaultReasoningEffort;
        set => SetTrackedProperty(ref _openAiDefaultReasoningEffort, value);
    }

    public string OpenAiAdvancedReasoningEffort
    {
        get => _openAiAdvancedReasoningEffort;
        set => SetTrackedProperty(ref _openAiAdvancedReasoningEffort, value);
    }

    public string OpenAiDefaultTextVerbosity
    {
        get => _openAiDefaultTextVerbosity;
        set => SetTrackedProperty(ref _openAiDefaultTextVerbosity, value);
    }

    public string OpenAiAdvancedTextVerbosity
    {
        get => _openAiAdvancedTextVerbosity;
        set => SetTrackedProperty(ref _openAiAdvancedTextVerbosity, value);
    }

    public string OpenAiDefaultEnableWebSearch
    {
        get => _openAiDefaultEnableWebSearch;
        set => SetTrackedBooleanStringProperty(
            ref _openAiDefaultEnableWebSearch,
            value,
            "IsOpenAiDefaultEnableWebSearchEnabled");
    }

    public string OpenAiAdvancedEnableWebSearch
    {
        get => _openAiAdvancedEnableWebSearch;
        set => SetTrackedBooleanStringProperty(
            ref _openAiAdvancedEnableWebSearch,
            value,
            "IsOpenAiAdvancedEnableWebSearchEnabled");
    }

    public string OpenAiDefaultEnableCodeInterpreter
    {
        get => _openAiDefaultEnableCodeInterpreter;
        set => SetTrackedBooleanStringProperty(
            ref _openAiDefaultEnableCodeInterpreter,
            value,
            "IsOpenAiDefaultEnableCodeInterpreterEnabled");
    }

    public string OpenAiAdvancedEnableCodeInterpreter
    {
        get => _openAiAdvancedEnableCodeInterpreter;
        set => SetTrackedBooleanStringProperty(
            ref _openAiAdvancedEnableCodeInterpreter,
            value,
            "IsOpenAiAdvancedEnableCodeInterpreterEnabled");
    }

    public bool IsOpenAiDefaultEnableWebSearchEnabled
    {
        get => ParseBooleanFlag(OpenAiDefaultEnableWebSearch);
        set => OpenAiDefaultEnableWebSearch = value ? "true" : "false";
    }

    public bool IsOpenAiAdvancedEnableWebSearchEnabled
    {
        get => ParseBooleanFlag(OpenAiAdvancedEnableWebSearch);
        set => OpenAiAdvancedEnableWebSearch = value ? "true" : "false";
    }

    public bool IsOpenAiDefaultEnableCodeInterpreterEnabled
    {
        get => ParseBooleanFlag(OpenAiDefaultEnableCodeInterpreter);
        set => OpenAiDefaultEnableCodeInterpreter = value ? "true" : "false";
    }

    public bool IsOpenAiAdvancedEnableCodeInterpreterEnabled
    {
        get => ParseBooleanFlag(OpenAiAdvancedEnableCodeInterpreter);
        set => OpenAiAdvancedEnableCodeInterpreter = value ? "true" : "false";
    }

    public string NapCatWsUrl
    {
        get => _napCatWsUrl;
        set => SetTrackedProperty(ref _napCatWsUrl, value);
    }

    public string NapCatToken
    {
        get => _napCatToken;
        set => SetTrackedProperty(ref _napCatToken, value);
    }

    public string WechatBridgeUrl
    {
        get => _wechatBridgeUrl;
        set => SetTrackedProperty(ref _wechatBridgeUrl, value);
    }

    public string WechatBridgeToken
    {
        get => _wechatBridgeToken;
        set => SetTrackedProperty(ref _wechatBridgeToken, value);
    }

    public string WechatBotPrefix
    {
        get => _wechatBotPrefix;
        set => SetTrackedProperty(ref _wechatBotPrefix, value);
    }

    public string BotPrefix
    {
        get => _botPrefix;
        set => SetTrackedProperty(ref _botPrefix, value);
    }

    public string BotSystemPrompt
    {
        get => _botSystemPrompt;
        set
        {
            if (SetProperty(ref _botSystemPrompt, value))
            {
                if (!_suspendDirtyTracking)
                {
                    HasUnsavedChanges = true;
                }

                OnPropertyChanged(nameof(EffectiveBotInstructionsText));
                RefreshHealthReport();
            }
        }
    }

    public string BotPersona
    {
        get => _botPersona;
        set
        {
            if (SetProperty(ref _botPersona, value))
            {
                if (!_suspendDirtyTracking)
                {
                    HasUnsavedChanges = true;
                }

                OnPropertyChanged(nameof(EffectiveBotInstructionsText));
                RefreshHealthReport();
            }
        }
    }

    public string DefaultBotInstructionsText => DefaultBotInstructionsTextValue;

    public string EffectiveBotInstructionsText => BuildEffectiveBotInstructions(BotSystemPrompt, BotPersona);

    public string MaxOutputChars
    {
        get => _maxOutputChars;
        set => SetTrackedProperty(ref _maxOutputChars, value);
    }

    public string AllowedChatIds
    {
        get => _allowedChatIds;
        set => SetTrackedProperty(ref _allowedChatIds, value);
    }

    public string AllowedUserIds
    {
        get => _allowedUserIds;
        set => SetTrackedProperty(ref _allowedUserIds, value);
    }

    public bool IsProcessRunning
    {
        get => _isProcessRunning;
        private set
        {
            if (SetProperty(ref _isProcessRunning, value))
            {
                OnPropertyChanged(nameof(ProcessStateText));
                OnPropertyChanged(nameof(ShellRuntimeBoundaryText));
                OnPropertyChanged(nameof(CloseToTrayBehaviorText));
                OnPropertyChanged(nameof(ExitDesktopBehaviorText));
                OnPropertyChanged(nameof(StopBackendBehaviorText));
                OnPropertyChanged(nameof(ReopenDesktopBehaviorText));
                OnPropertyChanged(nameof(IsOverallReadinessReady));
                OnPropertyChanged(nameof(IsOverallReadinessSetupComplete));
                OnPropertyChanged(nameof(OverallReadinessStateText));
                OnPropertyChanged(nameof(OverallReadinessSummaryText));
                OnPropertyChanged(nameof(OverallReadinessRecentActivityText));
                OnPropertyChanged(nameof(OverallReadinessActionLabel));
                OnPropertyChanged(nameof(OverallReadinessActionKey));
                OnPropertyChanged(nameof(FirstRunGuideSteps));
                OnPropertyChanged(nameof(FirstRunGuideProgressText));
                OnPropertyChanged(nameof(FirstRunGuideCurrentStepText));
                OnPropertyChanged(nameof(IsFirstRunGuideComplete));
                OnPropertyChanged(nameof(FirstRunGuideCompletionText));
                OnPropertyChanged(nameof(DailyUseGuideText));
                OnPropertyChanged(nameof(DailyUseGuideSteps));
                OnPropertyChanged(nameof(DailyUseGuideProgressText));
                OnPropertyChanged(nameof(DailyUseGuideCurrentStepText));
                OnPropertyChanged(nameof(IsDailyUseGuideComplete));
                OnPropertyChanged(nameof(DailyUseGuideCompletionText));
                OnPropertyChanged(nameof(DailyUseStepsText));
                UpdateCommandStates();
            }
        }
    }

    public string ProcessStateText => IsProcessRunning ? "Running" : "Stopped";

    public string WechatRuntimeStateText =>
        _runtimeSnapshot.WechatConfigured != true
            ? "Wechat disabled"
            : _runtimeSnapshot.WechatRuntimeActive == true
                ? "Wechat runtime active"
                : "Wechat runtime stopped";

    public string RuntimeReadyText =>
        _runtimeSnapshot.RuntimeReady == true ? "QQ channel ready" : "QQ channel not ready";

    public string WechatRuntimeReadyText =>
        _runtimeSnapshot.WechatConfigured != true
            ? "Wechat channel disabled"
            : _runtimeSnapshot.WechatRuntimeReady == true
                ? "Wechat channel ready"
                : "Wechat channel not ready";

    public string WechatBridgeStateText =>
        _runtimeSnapshot.WechatBridgeConnected == true ? "Wechat bridge connected" : "Wechat bridge disconnected";

    public string WechatWorkerProcessText =>
        _runtimeSnapshot.WechatWorkerProcessId is int workerPid ? $"Wechat worker PID {workerPid}" : "Wechat worker not running";

    public string HealthStateText => _healthReport.StateText;

    public string HealthSummaryText => _healthReport.Summary;

    public string HealthChecklistStatusText => _healthReport.ChecklistStatus;

    public string HealthReadyNowText => _healthReport.ReadyNowText;

    public string HealthPrimaryActionText => _healthReport.PrimaryAction;

    public string HealthPrimaryActionLabel => _healthReport.PrimaryActionLabel;

    public string HealthPrimaryActionKey => _healthReport.PrimaryActionKey;

    public string HealthRuntimeExplanationText => _healthReport.RuntimeExplanation;

    public string HealthLatestIssueText => _healthReport.LatestIssue;

    public string HealthLatestIssueActionLabel => _healthReport.LatestIssueActionLabel;

    public string HealthLatestIssueActionKey => _healthReport.LatestIssueActionKey;

    public string LatestQqLlmSummaryText =>
        BackendActivityProjectionFormatter.FormatRequestSummary(_runtimeSnapshot.LastQqLlmRequest, "No QQ requests captured yet");

    public string LatestQqLlmDetailText =>
        BackendLlmProjectionFormatter.FormatRequestDetail(_runtimeSnapshot.LastQqLlmRequest);

    public string LatestQqActivitySummaryText =>
        BackendActivityProjectionFormatter.FormatActivitySummary(_runtimeSnapshot.LastQqLlmRequest, _runtimeSnapshot.LastQqLlmFailure, "No QQ activity captured yet");

    public string LatestQqRecentActivityText =>
        BackendActivityProjectionFormatter.FormatRecentActivity(QqRecentActivities, "No recent QQ activity yet");

    public bool PinSelectedQqActivity
    {
        get => _pinSelectedQqActivity;
        set
        {
            if (SetProperty(ref _pinSelectedQqActivity, value))
            {
                PersistActivityStateIfPossible();
            }
        }
    }

    public bool ShowOnlyQqFailures
    {
        get => _showOnlyQqFailures;
        set
        {
            if (SetProperty(ref _showOnlyQqFailures, value))
            {
                QqRecentActivitiesView.Refresh();
                SelectedQqRecentActivity = BackendRecentActivityCoordinator.ResolveSelectionAfterFilterChange(
                    QqRecentActivities,
                    SelectedQqRecentActivity,
                    ShowOnlyQqFailures);
                PersistActivityStateIfPossible();
            }
        }
    }

    public BackendRecentActivityItem? SelectedQqRecentActivity
    {
        get => _selectedQqRecentActivity;
        set
        {
            if (SetProperty(ref _selectedQqRecentActivity, value))
            {
                OnPropertyChanged(nameof(SelectedQqRecentActivitySummaryText));
                OnPropertyChanged(nameof(SelectedQqRecentActivityMetaText));
                OnPropertyChanged(nameof(SelectedQqRecentActivityDetailText));
                PersistActivityStateIfPossible();
            }
        }
    }

    public string SelectedQqRecentActivitySummaryText =>
        _selectedQqRecentActivity?.Summary ?? "Select a QQ activity event";

    public string SelectedQqRecentActivityMetaText =>
        _selectedQqRecentActivity?.Meta ?? "No event selected";

    public string SelectedQqRecentActivityDetailText =>
        _selectedQqRecentActivity?.Detail ?? "Select a QQ activity event";

    public string LatestQqActivityStateText =>
        BackendActivityProjectionFormatter.FormatActivityState(_runtimeSnapshot.LastQqLlmRequest, _runtimeSnapshot.LastQqLlmFailure);

    public string LatestQqLatestSuccessText =>
        BackendActivityProjectionFormatter.FormatLatestSuccess(_runtimeSnapshot.LastQqLlmRequest);

    public string LatestQqLatestFailureText =>
        BackendActivityProjectionFormatter.FormatLatestFailure(_runtimeSnapshot.LastQqLlmFailure);

    public string LatestQqRecoveryText =>
        BackendActivityProjectionFormatter.FormatRecoveryState(_runtimeSnapshot.LastQqLlmRequest, _runtimeSnapshot.LastQqLlmFailure);

    public string LatestQqRequestTimelineText =>
        BackendActivityProjectionFormatter.FormatRequestTimeline(_runtimeSnapshot.LastQqLlmRequest);

    public string LatestQqDecisionTriggerText =>
        BackendLlmProjectionFormatter.FormatRequestDecisionTrigger(_runtimeSnapshot.LastQqLlmRequest);

    public string LatestQqDecisionCapabilityText =>
        BackendLlmProjectionFormatter.FormatRequestDecisionCapability(_runtimeSnapshot.LastQqLlmRequest);

    public string LatestQqDecisionUpgradeText =>
        BackendLlmProjectionFormatter.FormatRequestDecisionUpgrade(_runtimeSnapshot.LastQqLlmRequest);

    public string LatestQqRequestedCapabilitiesText =>
        BackendLlmProjectionFormatter.FormatRequestedCapabilities(_runtimeSnapshot.LastQqLlmRequest);

    public string LatestWechatLlmSummaryText =>
        BackendActivityProjectionFormatter.FormatRequestSummary(_runtimeSnapshot.LastWechatLlmRequest, "No Wechat requests captured yet");

    public string LatestWechatLlmDetailText =>
        BackendLlmProjectionFormatter.FormatRequestDetail(_runtimeSnapshot.LastWechatLlmRequest);

    public string LatestWechatActivitySummaryText =>
        BackendActivityProjectionFormatter.FormatActivitySummary(_runtimeSnapshot.LastWechatLlmRequest, _runtimeSnapshot.LastWechatLlmFailure, "No Wechat activity captured yet");

    public string LatestWechatRecentActivityText =>
        BackendActivityProjectionFormatter.FormatRecentActivity(WechatRecentActivities, "No recent Wechat activity yet");

    public bool PinSelectedWechatActivity
    {
        get => _pinSelectedWechatActivity;
        set
        {
            if (SetProperty(ref _pinSelectedWechatActivity, value))
            {
                PersistActivityStateIfPossible();
            }
        }
    }

    public bool ShowOnlyWechatFailures
    {
        get => _showOnlyWechatFailures;
        set
        {
            if (SetProperty(ref _showOnlyWechatFailures, value))
            {
                WechatRecentActivitiesView.Refresh();
                SelectedWechatRecentActivity = BackendRecentActivityCoordinator.ResolveSelectionAfterFilterChange(
                    WechatRecentActivities,
                    SelectedWechatRecentActivity,
                    ShowOnlyWechatFailures);
                PersistActivityStateIfPossible();
            }
        }
    }

    public BackendRecentActivityItem? SelectedWechatRecentActivity
    {
        get => _selectedWechatRecentActivity;
        set
        {
            if (SetProperty(ref _selectedWechatRecentActivity, value))
            {
                OnPropertyChanged(nameof(SelectedWechatRecentActivitySummaryText));
                OnPropertyChanged(nameof(SelectedWechatRecentActivityMetaText));
                OnPropertyChanged(nameof(SelectedWechatRecentActivityDetailText));
                PersistActivityStateIfPossible();
            }
        }
    }

    public string SelectedWechatRecentActivitySummaryText =>
        _selectedWechatRecentActivity?.Summary ?? "Select a Wechat activity event";

    public string SelectedWechatRecentActivityMetaText =>
        _selectedWechatRecentActivity?.Meta ?? "No event selected";

    public string SelectedWechatRecentActivityDetailText =>
        _selectedWechatRecentActivity?.Detail ?? "Select a Wechat activity event";

    public string LatestWechatActivityStateText =>
        BackendActivityProjectionFormatter.FormatActivityState(_runtimeSnapshot.LastWechatLlmRequest, _runtimeSnapshot.LastWechatLlmFailure);

    public string LatestWechatLatestSuccessText =>
        BackendActivityProjectionFormatter.FormatLatestSuccess(_runtimeSnapshot.LastWechatLlmRequest);

    public string LatestWechatLatestFailureText =>
        BackendActivityProjectionFormatter.FormatLatestFailure(_runtimeSnapshot.LastWechatLlmFailure);

    public string LatestWechatRecoveryText =>
        BackendActivityProjectionFormatter.FormatRecoveryState(_runtimeSnapshot.LastWechatLlmRequest, _runtimeSnapshot.LastWechatLlmFailure);

    public string LatestWechatRequestTimelineText =>
        BackendActivityProjectionFormatter.FormatRequestTimeline(_runtimeSnapshot.LastWechatLlmRequest);

    public string LatestWechatDecisionTriggerText =>
        BackendLlmProjectionFormatter.FormatRequestDecisionTrigger(_runtimeSnapshot.LastWechatLlmRequest);

    public string LatestWechatDecisionCapabilityText =>
        BackendLlmProjectionFormatter.FormatRequestDecisionCapability(_runtimeSnapshot.LastWechatLlmRequest);

    public string LatestWechatDecisionUpgradeText =>
        BackendLlmProjectionFormatter.FormatRequestDecisionUpgrade(_runtimeSnapshot.LastWechatLlmRequest);

    public string LatestWechatRequestedCapabilitiesText =>
        BackendLlmProjectionFormatter.FormatRequestedCapabilities(_runtimeSnapshot.LastWechatLlmRequest);

    public string LatestQqFailureSummaryText =>
        BackendActivityProjectionFormatter.FormatFailureSummary(_runtimeSnapshot.LastQqLlmFailure, "No QQ failures captured yet");

    public string LatestQqFailureTimelineText =>
        BackendActivityProjectionFormatter.FormatFailureTimeline(_runtimeSnapshot.LastQqLlmFailure);

    public string LatestQqFailureTriggerText =>
        BackendLlmProjectionFormatter.FormatFailureTrigger(_runtimeSnapshot.LastQqLlmFailure);

    public string LatestQqFailureCapabilityText =>
        BackendLlmProjectionFormatter.FormatFailureCapability(_runtimeSnapshot.LastQqLlmFailure);

    public string LatestQqFailureUpgradeText =>
        BackendLlmProjectionFormatter.FormatFailureUpgrade(_runtimeSnapshot.LastQqLlmFailure);

    public string LatestQqFailureErrorText =>
        BackendLlmProjectionFormatter.FormatFailureError(_runtimeSnapshot.LastQqLlmFailure);

    public string LatestWechatFailureSummaryText =>
        BackendActivityProjectionFormatter.FormatFailureSummary(_runtimeSnapshot.LastWechatLlmFailure, "No Wechat failures captured yet");

    public string LatestWechatFailureTimelineText =>
        BackendActivityProjectionFormatter.FormatFailureTimeline(_runtimeSnapshot.LastWechatLlmFailure);

    public string LatestWechatFailureTriggerText =>
        BackendLlmProjectionFormatter.FormatFailureTrigger(_runtimeSnapshot.LastWechatLlmFailure);

    public string LatestWechatFailureCapabilityText =>
        BackendLlmProjectionFormatter.FormatFailureCapability(_runtimeSnapshot.LastWechatLlmFailure);

    public string LatestWechatFailureUpgradeText =>
        BackendLlmProjectionFormatter.FormatFailureUpgrade(_runtimeSnapshot.LastWechatLlmFailure);

    public string LatestWechatFailureErrorText =>
        BackendLlmProjectionFormatter.FormatFailureError(_runtimeSnapshot.LastWechatLlmFailure);

    public bool IsControlApiReachable => _runtimeSnapshot.ControlApiReachable == true;

    public bool AutoStartEnabled
    {
        get => _autoStartEnabled;
        private set
        {
            if (SetProperty(ref _autoStartEnabled, value))
            {
                OnPropertyChanged(nameof(AutoStartStateText));
                OnPropertyChanged(nameof(AutoStartButtonText));
                OnPropertyChanged(nameof(ResidentModeDetailText));
                RefreshHealthReport();
            }
        }
    }

    public string AutoStartStateText => AutoStartEnabled ? "Starts with Windows" : "Manual launch";

    public string AutoStartButtonText => AutoStartEnabled ? "Disable startup" : "Enable startup";

    public string ResidentModeDetailText => AutoStartEnabled
        ? "Resident mode: starts at Windows sign-in, reopens minimized, and keeps the tray entry available for quick control. Closing the desktop window still hides it to the tray; use Stop backend if you want the runtime offline."
        : "Resident mode: launch manually for now. Minimize or close still hides this window to the tray, and Exit Desktop closes only this window. Enable startup if you want it back automatically after sign-in.";

    public string ShellRuntimeBoundaryText => IsProcessRunning
        ? "Close to tray: hides this desktop window and leaves the backend running." + Environment.NewLine +
          "Exit Desktop: closes only the desktop shell and tray icon. The runtime keeps running until you stop it." + Environment.NewLine +
          "Stop Backend: stops the local runtime and takes QQ/WeChat offline."
        : "Close to tray: hides this desktop window and keeps the tray entry available." + Environment.NewLine +
          "Exit Desktop: closes only the desktop shell. You can reopen it later without changing saved state." + Environment.NewLine +
          "Stop Backend: no effect while the runtime is already offline.";

    public string CloseToTrayBehaviorText => IsProcessRunning
        ? "Hides this desktop window and leaves the backend running."
        : "Hides this desktop window and keeps the tray entry available.";

    public string ExitDesktopBehaviorText => IsProcessRunning
        ? "Closes only the desktop shell and tray icon. The runtime keeps running until you stop it."
        : "Closes only the desktop shell. You can reopen it later without changing saved state.";

    public string StopBackendBehaviorText => IsProcessRunning
        ? "Stops the local runtime and takes QQ/WeChat offline."
        : "No effect while the runtime is already offline.";

    public string ReopenDesktopBehaviorText => IsProcessRunning
        ? "Use the desktop shortcut or Start menu to reopen Local AI Runtime. It restores this desktop window and reattaches to the same running runtime instead of starting a second desktop shell."
        : "Use the desktop shortcut or Start menu to reopen Local AI Runtime. It restores this desktop window instead of starting a second desktop shell. If the runtime is offline, reopen the shell and start it manually.";

    public bool IsOverallReadinessReady => IsDailyUseGuideComplete;

    public bool IsOverallReadinessSetupComplete => IsFirstRunGuideComplete;

    public string OverallReadinessStateText => IsDailyUseGuideComplete
        ? "Ready for daily use"
        : IsFirstRunGuideComplete
            ? "Setup complete"
            : "Setup in progress";

    public string OverallReadinessSummaryText => IsDailyUseGuideComplete
        ? "Runtime is online, resident-mode basics are configured, and no urgent issue action is waiting right now."
        : IsFirstRunGuideComplete
            ? "Required setup is complete. Finish the highlighted daily-use step below to make long-term use smoother."
            : "Finish the highlighted first-run step below. After that, this window can stay in the tray as your daily control plane.";

    public string OverallReadinessRecentActivityText => BuildOverallReadinessRecentActivityText();

    public string OverallReadinessActionLabel
    {
        get
        {
            if (IsOverallReadinessReady)
            {
                return HasAnyRecentActivity() ? "Review recent activity" : string.Empty;
            }

            var actionStep = !IsFirstRunGuideComplete
                ? FirstRunGuideSteps.FirstOrDefault(static step => step.IsCurrent)
                : DailyUseGuideSteps.FirstOrDefault(static step => step.IsCurrent);
            return actionStep?.ActionLabel ?? string.Empty;
        }
    }

    public string OverallReadinessActionKey
    {
        get
        {
            if (IsOverallReadinessReady)
            {
                return HasAnyRecentActivity() ? DesktopHealthActionKeys.FocusLatestActivity : string.Empty;
            }

            var actionStep = !IsFirstRunGuideComplete
                ? FirstRunGuideSteps.FirstOrDefault(static step => step.IsCurrent)
                : DailyUseGuideSteps.FirstOrDefault(static step => step.IsCurrent);
            return actionStep?.ActionKey ?? string.Empty;
        }
    }

    public string FirstRunGuideText
    {
        get
        {
            if (!IsBackendRootValid)
            {
                return "首次打开只看这 3 步。";
            }

            var blockingChecks = _healthReport.Checks
                .Where(static check => check.IsBlocking)
                .Select(static check => check.Title)
                .ToArray();

            if (blockingChecks.Length > 0)
            {
                return $"首次打开：先补齐 {string.Join("、", blockingChecks)}。";
            }

            if (StartCommand.CanExecute(null))
            {
                return "首次打开：必填项已经齐了。";
            }

            return "首次打开：现在可以直接进入常驻使用。";
        }
    }

    public IReadOnlyList<DesktopGuideStepItem> FirstRunGuideSteps => BuildFirstRunGuideSteps();

    public string FirstRunGuideProgressText => BuildGuideProgressText(FirstRunGuideSteps);

    public string FirstRunGuideCurrentStepText => BuildGuideCurrentStepText(FirstRunGuideSteps);

    public bool IsFirstRunGuideComplete => AreGuideStepsComplete(FirstRunGuideSteps);

    public string FirstRunGuideCompletionText => IsFirstRunGuideComplete
        ? "首次安装已完成，现在可以把 Local AI Runtime 当作日常常驻控制台使用。"
        : string.Empty;

    public string FirstRunStepsText
    {
        get
        {
            if (!IsBackendRootValid)
            {
                return "1. 选择本地 runtime 目录。" + Environment.NewLine +
                       "2. 点击重新加载，确认配置已读到这台机器。" + Environment.NewLine +
                       "3. 再补 API key 和通道配置。";
            }

            var blockingChecks = _healthReport.Checks
                .Where(static check => check.IsBlocking)
                .Select(static check => check.Title)
                .ToArray();

            if (blockingChecks.Length > 0)
            {
                return "1. 先补齐必填项：" + string.Join("、", blockingChecks) + "。" + Environment.NewLine +
                       "2. 保存配置，让当前窗口里的改动真正生效。" + Environment.NewLine +
                       "3. 启动后端，再在 System Check 里确认 QQ ready。";
            }

            if (StartCommand.CanExecute(null))
            {
                return "1. 保存当前配置，保持窗口显示的状态为最新。" + Environment.NewLine +
                       "2. 启动后端，让 control API、QQ runtime 和可选 WeChat runtime 上线。" + Environment.NewLine +
                       "3. 在 System Check 里确认 QQ ready，再开始日常使用。";
            }

            return "1. 必填项已经齐了，当前 runtime 可继续使用。" + Environment.NewLine +
                   "2. 之后最小化或关闭窗口都会进托盘。" + Environment.NewLine +
                   "3. 需要重新接回时，从 Local AI Runtime 快捷方式或开始菜单打开即可。";
        }
    }

    public string DailyUseGuideText => IsProcessRunning
        ? "日常常驻只记住这 3 件事。"
        : "日常常驻：先把入口和托盘规则记住。";

    public IReadOnlyList<DesktopGuideStepItem> DailyUseGuideSteps => BuildDailyUseGuideSteps();

    public string DailyUseGuideProgressText => BuildGuideProgressText(DailyUseGuideSteps);

    public string DailyUseGuideCurrentStepText => BuildGuideCurrentStepText(DailyUseGuideSteps);

    public bool IsDailyUseGuideComplete => AreGuideStepsComplete(DailyUseGuideSteps);

    public string DailyUseGuideCompletionText => IsDailyUseGuideComplete
        ? "已进入日常常驻模式。之后从 Local AI Runtime 重新接回即可。"
        : string.Empty;

    public string DailyUseStepsText => IsProcessRunning
        ? "1. 最小化或关闭窗口：只会进托盘，backend 继续运行。" + Environment.NewLine +
          "2. Exit Desktop：只退出桌面壳；Stop Backend 才会让 QQ / WeChat 下线。" + Environment.NewLine +
          "3. 之后从桌面快捷方式或开始菜单里的 Local AI Runtime 重新接回控制面。"
        : "1. 先从这个窗口启动后端，再开始常驻使用。" + Environment.NewLine +
          "2. 之后最小化或关闭窗口都会进托盘，不会清掉本地状态。" + Environment.NewLine +
          "3. 需要重新打开时，从桌面快捷方式或开始菜单里的 Local AI Runtime 重新接回。";

    private IReadOnlyList<DesktopGuideStepItem> BuildFirstRunGuideSteps()
    {
        var steps = new List<DesktopGuideStepItem>();
        var blockingChecks = _healthReport.Checks
            .Where(static check => check.IsBlocking)
            .ToArray();
        var firstBlockingCheck = blockingChecks.FirstOrDefault();
        var hasUnsavedBlockingWork = IsBackendRootValid && HasUnsavedChanges;

        steps.Add(
            new DesktopGuideStepItem
            {
                Title = "Attach runtime folder",
                Detail = IsBackendRootValid
                    ? "This window is already pointed at a usable local runtime folder."
                    : "Choose the installed local runtime folder that contains package.json and src\\index.mjs.",
                StatusText = IsBackendRootValid ? "Done" : "Next",
                IsComplete = IsBackendRootValid,
                ActionLabel = IsBackendRootValid ? string.Empty : "Choose folder",
                ActionKey = IsBackendRootValid ? string.Empty : DesktopHealthActionKeys.FocusBackendRoot
            });

        steps.Add(
            new DesktopGuideStepItem
            {
                Title = "Complete required setup",
                Detail = BuildFirstRunSetupStepDetail(blockingChecks, hasUnsavedBlockingWork),
                StatusText = blockingChecks.Length == 0 && !hasUnsavedBlockingWork ? "Done" : "Next",
                IsComplete = blockingChecks.Length == 0 && !hasUnsavedBlockingWork,
                ActionLabel = BuildFirstRunSetupStepActionLabel(firstBlockingCheck, hasUnsavedBlockingWork),
                ActionKey = BuildFirstRunSetupStepActionKey(firstBlockingCheck, hasUnsavedBlockingWork)
            });

        steps.Add(
            new DesktopGuideStepItem
            {
                Title = "Bring the runtime online",
                Detail = BuildFirstRunRuntimeStepDetail(blockingChecks.Length > 0 || hasUnsavedBlockingWork),
                StatusText = IsFirstRunRuntimeStepComplete() ? "Done" : "Next",
                IsComplete = IsFirstRunRuntimeStepComplete(),
                ActionLabel = BuildFirstRunRuntimeStepActionLabel(blockingChecks.Length > 0 || hasUnsavedBlockingWork),
                ActionKey = BuildFirstRunRuntimeStepActionKey(blockingChecks.Length > 0 || hasUnsavedBlockingWork)
            });

        return FinalizeGuideSteps(steps);
    }

    private string BuildFirstRunSetupStepDetail(
        IReadOnlyList<DesktopHealthCheckItem> blockingChecks,
        bool hasUnsavedBlockingWork)
    {
        if (!IsBackendRootValid)
        {
            return "After choosing the folder, fill in the API key and channel settings shown in System Check.";
        }

        if (hasUnsavedBlockingWork)
        {
            return "Save the edits currently shown in this window before trying to start the runtime.";
        }

        if (blockingChecks.Count == 0)
        {
            return "Required API key and channel settings already look complete.";
        }

        return $"Finish the required setup items first: {string.Join("、", blockingChecks.Select(static check => check.Title))}.";
    }

    private string BuildFirstRunSetupStepActionLabel(
        DesktopHealthCheckItem? firstBlockingCheck,
        bool hasUnsavedBlockingWork)
    {
        if (!IsBackendRootValid)
        {
            return string.Empty;
        }

        if (hasUnsavedBlockingWork)
        {
            return "Save config";
        }

        return firstBlockingCheck?.ActionLabel ?? string.Empty;
    }

    private string BuildFirstRunSetupStepActionKey(
        DesktopHealthCheckItem? firstBlockingCheck,
        bool hasUnsavedBlockingWork)
    {
        if (!IsBackendRootValid)
        {
            return string.Empty;
        }

        if (hasUnsavedBlockingWork)
        {
            return DesktopHealthActionKeys.SaveConfig;
        }

        return firstBlockingCheck?.ActionKey ?? string.Empty;
    }

    private string BuildFirstRunRuntimeStepDetail(bool isBlockedBySetup)
    {
        if (!IsBackendRootValid)
        {
            return "Runtime start becomes available after the folder and required setup above are complete.";
        }

        if (isBlockedBySetup)
        {
            return "Finish the setup step above first. Then start the backend from this window.";
        }

        if (StartCommand.CanExecute(null))
        {
            return "Start the backend to bring the control API and QQ runtime online.";
        }

        if (!string.Equals(RuntimeReadyText, "QQ channel ready", StringComparison.Ordinal))
        {
            return "Backend is already running. Confirm NapCat is connected, then check System Check again.";
        }

        return "QQ is ready. You can now use this as a daily desktop control plane.";
    }

    private string BuildFirstRunRuntimeStepActionLabel(bool isBlockedBySetup)
    {
        if (!IsBackendRootValid || isBlockedBySetup)
        {
            return string.Empty;
        }

        if (StartCommand.CanExecute(null))
        {
            return "Start backend";
        }

        if (!string.Equals(RuntimeReadyText, "QQ channel ready", StringComparison.Ordinal))
        {
            return "Check NapCat config";
        }

        return string.Empty;
    }

    private string BuildFirstRunRuntimeStepActionKey(bool isBlockedBySetup)
    {
        if (!IsBackendRootValid || isBlockedBySetup)
        {
            return string.Empty;
        }

        if (StartCommand.CanExecute(null))
        {
            return DesktopHealthActionKeys.StartBackend;
        }

        if (!string.Equals(RuntimeReadyText, "QQ channel ready", StringComparison.Ordinal))
        {
            return DesktopHealthActionKeys.FocusNapCatUrl;
        }

        return string.Empty;
    }

    private bool IsFirstRunRuntimeStepComplete()
    {
        return IsBackendRootValid &&
               !StartCommand.CanExecute(null) &&
               string.Equals(RuntimeReadyText, "QQ channel ready", StringComparison.Ordinal);
    }

    private IReadOnlyList<DesktopGuideStepItem> BuildDailyUseGuideSteps()
    {
        var steps = new List<DesktopGuideStepItem>();
        var latestIssueActionLabel = HealthLatestIssueActionLabel;
        var latestIssueActionKey = HealthLatestIssueActionKey;

        steps.Add(
            new DesktopGuideStepItem
            {
                Title = "Keep the runtime reachable",
                Detail = IsProcessRunning
                    ? "Backend is already online. You can leave this window in the tray and reconnect later."
                    : "Start the backend before switching into regular tray-based use.",
                StatusText = IsProcessRunning ? "Done" : "Next",
                IsComplete = IsProcessRunning,
                ActionLabel = IsProcessRunning ? string.Empty : "Start backend",
                ActionKey = IsProcessRunning ? string.Empty : DesktopHealthActionKeys.StartBackend
            });

        steps.Add(
            new DesktopGuideStepItem
            {
                Title = "Decide whether it should return after sign-in",
                Detail = AutoStartEnabled
                    ? "Resident mode startup is already enabled. Local AI Runtime will reopen minimized after Windows sign-in."
                    : "Enable startup if you want Local AI Runtime to return automatically after Windows sign-in.",
                StatusText = AutoStartEnabled ? "Done" : "Optional",
                IsComplete = AutoStartEnabled,
                ActionLabel = AutoStartEnabled ? string.Empty : "Enable startup",
                ActionKey = AutoStartEnabled ? string.Empty : DesktopHealthActionKeys.ToggleAutoStart
            });

        steps.Add(
            new DesktopGuideStepItem
            {
                Title = "Check the latest issue when something feels wrong",
                Detail = string.IsNullOrWhiteSpace(latestIssueActionLabel)
                    ? "No urgent issue action is waiting right now. Use the tray, recent activity, or System Check as needed."
                    : HealthLatestIssueText,
                StatusText = string.IsNullOrWhiteSpace(latestIssueActionLabel) ? "Ready" : "Review",
                IsComplete = string.IsNullOrWhiteSpace(latestIssueActionLabel),
                ActionLabel = latestIssueActionLabel,
                ActionKey = latestIssueActionKey
            });

        return FinalizeGuideSteps(steps);
    }

    private static IReadOnlyList<DesktopGuideStepItem> FinalizeGuideSteps(IReadOnlyList<DesktopGuideStepItem> steps)
    {
        var currentStepIndex = steps
            .Select((step, index) => new { Step = step, Index = index })
            .FirstOrDefault(static item => !item.Step.IsComplete)
            ?.Index;

        return steps
            .Select((step, index) => step with
            {
                StepNumber = (index + 1).ToString(),
                IsCurrent = currentStepIndex is int currentIndex && currentIndex == index
            })
            .ToArray();
    }

    private static string BuildGuideProgressText(IReadOnlyList<DesktopGuideStepItem> steps)
    {
        if (steps.Count == 0)
        {
            return "已完成 0/0";
        }

        var completedCount = steps.Count(static step => step.IsComplete);
        return $"已完成 {completedCount}/{steps.Count}";
    }

    private static string BuildGuideCurrentStepText(IReadOnlyList<DesktopGuideStepItem> steps)
    {
        if (steps.Count == 0)
        {
            return "当前步骤：无";
        }

        var currentStep = steps.FirstOrDefault(static step => step.IsCurrent);
        return currentStep is null
            ? "当前步骤：全部完成"
            : $"当前步骤：{currentStep.Title}";
    }

    private static bool AreGuideStepsComplete(IReadOnlyList<DesktopGuideStepItem> steps)
    {
        return steps.Count > 0 && steps.All(static step => step.IsComplete);
    }

    private bool HasAnyRecentActivity() => QqRecentActivities.Count > 0 || WechatRecentActivities.Count > 0;

    private string BuildOverallReadinessRecentActivityText()
    {
        var latestActivity = GetLatestRecentActivity();
        if (latestActivity is null)
        {
            return "Recent activity: no recent QQ / WeChat activity captured yet.";
        }

        var (channel, item) = latestActivity.Value;
        var eventType = string.IsNullOrWhiteSpace(item.EventType) ? "Activity" : item.EventType;
        var summary = string.IsNullOrWhiteSpace(item.Summary) ? "No summary" : item.Summary;
        var capturedAt = string.IsNullOrWhiteSpace(item.Meta) ? item.CapturedAt : item.Meta;
        var capturedAtText = string.IsNullOrWhiteSpace(capturedAt) ? "unknown time" : capturedAt;

        return $"Recent activity: {channel} {eventType} | {summary} | {capturedAtText}";
    }

    private (string Channel, BackendRecentActivityItem Item)? GetLatestRecentActivity()
    {
        var latestQqItem = QqRecentActivities
            .OrderByDescending(static item => ParseCapturedAt(item.CapturedAt))
            .FirstOrDefault();
        var latestWechatItem = WechatRecentActivities
            .OrderByDescending(static item => ParseCapturedAt(item.CapturedAt))
            .FirstOrDefault();

        if (latestQqItem is null && latestWechatItem is null)
        {
            return null;
        }

        if (latestWechatItem is null)
        {
            return ("QQ", latestQqItem!);
        }

        if (latestQqItem is null)
        {
            return ("WeChat", latestWechatItem);
        }

        return ParseCapturedAt(latestQqItem.CapturedAt) >= ParseCapturedAt(latestWechatItem.CapturedAt)
            ? ("QQ", latestQqItem)
            : ("WeChat", latestWechatItem);
    }

    private static DateTimeOffset ParseCapturedAt(string? capturedAt)
    {
        return DateTimeOffset.TryParse(capturedAt, out var parsedCapturedAt)
            ? parsedCapturedAt
            : DateTimeOffset.MinValue;
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (SetProperty(ref _hasUnsavedChanges, value))
            {
                OnPropertyChanged(nameof(ConfigStateText));
                RefreshHealthReport();
            }
        }
    }

    public string ConfigStateText => HasUnsavedChanges ? "Unsaved changes" : "Config synced";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string LastLoadedAtText
    {
        get => _lastLoadedAtText;
        private set => SetProperty(ref _lastLoadedAtText, value);
    }

    public string LastSavedAtText
    {
        get => _lastSavedAtText;
        private set => SetProperty(ref _lastSavedAtText, value);
    }

    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

    public string ControlApiToken
    {
        get => _controlApiToken;
        set
        {
            if (SetProperty(ref _controlApiToken, value))
            {
                RefreshHealthReport();
            }
        }
    }

    public string ControlApiTokenStateText =>
        string.IsNullOrWhiteSpace(ControlApiToken)
            ? "Local token not set"
            : _backendControlApiService.LastFailure.Kind == BackendControlApiFailureKind.Unauthorized
                ? "Local token saved, but the backend still rejects it"
                : "Local token configured";

    public string ControlApiEndpointText
    {
        get
        {
            var host = ResolveLocalExtraValue(ControlApiHostEnvKey, "127.0.0.1");
            var port = ResolveLocalExtraValue(ControlApiPortEnvKey, "3199");
            return $"http://{host}:{port}";
        }
    }

    public string SessionStorePathText => Path.Combine(BackendRootPath, "data", "sessions.json");

    public string ImageCachePathText => Path.Combine(BackendRootPath, "data", "image-cache");

    public string ActivityStatePathText => new DesktopActivityStateStoragePolicy().ResolveStateFilePath(BackendRootPath);

    public string StateSnapshotFolderPathText => Path.Combine(BackendRootPath, "artifacts", "state-snapshots");

    public string LastStateSnapshotText
    {
        get => _lastStateSnapshotText;
        private set => SetProperty(ref _lastStateSnapshotText, value);
    }

    public string LastStateRestoreText
    {
        get => _lastStateRestoreText;
        private set
        {
            if (SetProperty(ref _lastStateRestoreText, value))
            {
                OnPropertyChanged(nameof(HasStateRestoreResult));
            }
        }
    }

    public string LastStateRestoreSummaryText
    {
        get => _lastStateRestoreSummaryText;
        private set => SetProperty(ref _lastStateRestoreSummaryText, value);
    }

    public string LastStateRestoreIssueText
    {
        get => _lastStateRestoreIssueText;
        private set => SetProperty(ref _lastStateRestoreIssueText, value);
    }

    public string LastStateRestoreTargetsText
    {
        get => _lastStateRestoreTargetsText;
        private set => SetProperty(ref _lastStateRestoreTargetsText, value);
    }

    public string LastStateRestoreSessionsText
    {
        get => _lastStateRestoreSessionsText;
        private set => SetProperty(ref _lastStateRestoreSessionsText, value);
    }

    public string LastStateRestoreLatestActivityText
    {
        get => _lastStateRestoreLatestActivityText;
        private set => SetProperty(ref _lastStateRestoreLatestActivityText, value);
    }

    public string LastStateRestoreAdviceText
    {
        get => _lastStateRestoreAdviceText;
        private set => SetProperty(ref _lastStateRestoreAdviceText, value);
    }

    public string LastStateRestoreControlPlaneText
    {
        get => _lastStateRestoreControlPlaneText;
        private set => SetProperty(ref _lastStateRestoreControlPlaneText, value);
    }

    public string LastStateRestoreRuntimeText
    {
        get => _lastStateRestoreRuntimeText;
        private set => SetProperty(ref _lastStateRestoreRuntimeText, value);
    }

    public string LastStateRestoreNextStepText
    {
        get => _lastStateRestoreNextStepText;
        private set => SetProperty(ref _lastStateRestoreNextStepText, value);
    }

    public string LastStateRestorePrimaryActionLabel
    {
        get => _lastStateRestorePrimaryActionLabel;
        private set => SetProperty(ref _lastStateRestorePrimaryActionLabel, value);
    }

    public string LastStateRestorePrimaryActionKey
    {
        get => _lastStateRestorePrimaryActionKey;
        private set => SetProperty(ref _lastStateRestorePrimaryActionKey, value);
    }

    public string LastStateRestoreSecondaryActionLabel
    {
        get => _lastStateRestoreSecondaryActionLabel;
        private set => SetProperty(ref _lastStateRestoreSecondaryActionLabel, value);
    }

    public string LastStateRestoreSecondaryActionKey
    {
        get => _lastStateRestoreSecondaryActionKey;
        private set => SetProperty(ref _lastStateRestoreSecondaryActionKey, value);
    }

    public string LastStateRestoreTertiaryActionLabel
    {
        get => _lastStateRestoreTertiaryActionLabel;
        private set => SetProperty(ref _lastStateRestoreTertiaryActionLabel, value);
    }

    public string LastStateRestoreTertiaryActionKey
    {
        get => _lastStateRestoreTertiaryActionKey;
        private set => SetProperty(ref _lastStateRestoreTertiaryActionKey, value);
    }

    public bool HasStateRestoreResult =>
        !string.Equals(
            LastStateRestoreText,
            "No state snapshot restored yet",
            StringComparison.Ordinal);

    public LocalStateSnapshotDescriptor? SelectedStateSnapshot
    {
        get => _selectedStateSnapshot;
        set
        {
            if (SetProperty(ref _selectedStateSnapshot, value))
            {
                OnPropertyChanged(nameof(SelectedStateSnapshotSummaryText));
                OnPropertyChanged(nameof(SelectedStateSnapshotDetailText));
                OnPropertyChanged(nameof(SelectedStateSnapshotImpactText));
                _ = RefreshSelectedStateSnapshotPreviewAsync();
                UpdateCommandStates();
            }
        }
    }

    public string SelectedStateSnapshotSummaryText =>
        SelectedStateSnapshot?.Summary ?? "Select a snapshot to inspect or restore.";

    public string SelectedStateSnapshotDetailText =>
        SelectedStateSnapshot?.Detail ?? "Snapshot details will appear here.";

    public string SelectedStateSnapshotImpactText =>
        SelectedStateSnapshot is null
            ? "Restore will overwrite the state shown in this snapshot. Delete permanently removes the selected archive."
            : BuildSelectedStateSnapshotImpactText(SelectedStateSnapshot);

    public string SelectedStateSnapshotDiffText
    {
        get => _selectedStateSnapshotDiffText;
        private set => SetProperty(ref _selectedStateSnapshotDiffText, value);
    }

    public string SelectedStateSnapshotAdviceText
    {
        get => _selectedStateSnapshotAdviceText;
        private set => SetProperty(ref _selectedStateSnapshotAdviceText, value);
    }

    public string SnapshotRetentionHintText =>
        "Snapshots are kept until you delete them. Older snapshots may still include secrets from .env.";

    public string SessionStoreStateText =>
        !IsBackendRootValid
            ? "State path unavailable until backend root is valid"
            : File.Exists(SessionStorePathText)
                ? "Session history file is present"
                : "Session history file will be created after the first saved conversation";

    public string ImageCacheStateText
    {
        get
        {
            if (!IsBackendRootValid)
            {
                return "Cache path unavailable until backend root is valid";
            }

            if (!Directory.Exists(ImageCachePathText))
            {
                return "Image cache is empty";
            }

            var cachedFileCount = Directory.GetFiles(ImageCachePathText, "*", SearchOption.AllDirectories).Length;
            return cachedFileCount == 0
                ? "Image cache is empty"
                : $"{cachedFileCount} cached image file{(cachedFileCount == 1 ? string.Empty : "s")}";
        }
    }

    public async Task InitializeAsync()
    {
        await LoadConfigAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logFlushTimer.Stop();
        _statusPollTimer.Stop();
        _logFlushTimer.Tick -= OnLogFlushTimerTick;
        _statusPollTimer.Tick -= OnStatusPollTimerTick;
        _botProcessService.LogReceived -= OnProcessLogReceived;
        _botProcessService.ProcessExited -= OnProcessExited;

        _backendControlApiService.Dispose();
        _botProcessService.Dispose();
        await Task.CompletedTask;
    }

    private bool CanLoadOrSave()
    {
        return IsBackendRootValid;
    }

    private bool CanStartBackend()
    {
        return IsBackendRootValid && !IsProcessRunning;
    }

    private bool CanStopBackend()
    {
        return IsProcessRunning;
    }

    private void RefreshAutoStartState()
    {
        try
        {
            AutoStartEnabled = _autoStartService.IsEnabled();
        }
        catch (Exception ex)
        {
            AutoStartEnabled = false;
            AddLog($"Failed to read auto-start state: {ex.Message}");
        }
    }

    private async Task LoadConfigAsync()
    {
        if (!IsBackendRootValid)
        {
            DesktopControlPlaneFeedback.ApplyOutcome(
                statusText: "Backend root is invalid",
                logMessages: ["Cannot load config because backend root is invalid."],
                notifications: [],
                setStatusText: (statusText) => StatusText = statusText,
                addLog: AddLog,
                notify: null);
            return;
        }

        try
        {
            _suspendDirtyTracking = true;
            StatusText = "Loading config...";
            var localEnvDocument = await LoadLocalEnvDocumentAsync();
            var loadResult = await LoadConfigFromAuthoritativeSourceAsync();
            var apiConfig = loadResult.ApiConfig;
            var apiStatus = loadResult.ApiStatus;

            if (apiConfig is not null)
            {
                _envDocument = new EnvDocument
                {
                    Config = BuildConfigCopy(apiConfig)
                };
                CopyLocalExtraValues(localEnvDocument, _envDocument);
            }
            else
            {
                _envDocument = localEnvDocument;
            }

            ApplyConfigToView(_envDocument.Config);
            ApplyLocalControlPlaneSettings(_envDocument);
            ApplyBackendRuntimeStatus(apiStatus, apiStatus is not null);
            await RefreshStateSnapshotsAsync();

            LastLoadedAtText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            HasUnsavedChanges = false;
            DesktopControlPlaneFeedback.ApplyOutcome(
                statusText: apiConfig?.RestartRequired == true
                    ? "Config loaded (restart required)"
                    : "Config loaded",
                logMessages:
                [
                    apiConfig is not null
                        ? $"Loaded config via control API: {apiConfig.EnvPath}"
                        : $"Loaded config from file: {EnvFilePath}"
                ],
                notifications: [],
                setStatusText: (statusText) => StatusText = statusText,
                addLog: AddLog,
                notify: null);
        }
        catch (Exception ex)
        {
            var userFacingError = DesktopOperationErrorFormatter.Build(
                operationLabel: "Load config",
                fallbackStatusText: "Load failed",
                technicalMessage: ex.Message,
                controlApiFailure: _backendControlApiService.LastFailure,
                envPath: EnvFilePath);
            DesktopControlPlaneFeedback.ApplyError(
                statusText: userFacingError.StatusText,
                logMessage: $"Load config failed: {ex.Message}",
                showDialog: true,
                dialogTitle: userFacingError.DialogTitle,
                dialogMessage: userFacingError.DialogMessage,
                setStatusText: (statusText) => StatusText = statusText,
                addLog: AddLog,
                showErrorDialog: (title, message) => System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error));
            ApplySuggestedErrorAction(userFacingError);
        }
        finally
        {
            _suspendDirtyTracking = false;
            UpdateCommandStates();
        }
    }

    private async Task<(BackendControlConfigResponse? ApiConfig, BackendRuntimeStatus? ApiStatus)> LoadConfigFromAuthoritativeSourceAsync()
    {
        return await BackendControlPlaneFacade.LoadAuthoritativeConfigAsync(
            tryGetConfigAsync: (cancellationToken) => _backendControlApiService.TryGetConfigAsync(cancellationToken),
            getLastFailure: () => _backendControlApiService.LastFailure,
            tryGetStatusAsync: (cancellationToken) => _backendControlApiService.TryGetStatusAsync(cancellationToken),
            isImmediateFailure: IsImmediateControlApiFailure,
            tryRecoverControlApiAsync: () => TryRecoverControlApiAsync("load-config"));
    }

    private async Task SaveConfigAsync()
    {
        await SaveConfigAsync(showUiErrors: true);
    }

    private async Task SaveLocalControlPlaneAsync()
    {
        if (!IsBackendRootValid)
        {
            StatusText = "Backend root is invalid";
            AddLog("Cannot save local control-plane settings because backend root is invalid.");
            return;
        }

        try
        {
            StatusText = "Saving local control-plane settings...";
            await _localBootstrapConfigStore.SaveExtraValueAsync(
                BackendRootPath,
                ControlApiTokenEnvKey,
                ControlApiToken);
            _envDocument.ExtraValues[ControlApiTokenEnvKey] = ControlApiToken.Trim();

            if (string.IsNullOrWhiteSpace(ControlApiToken))
            {
                _envDocument.ExtraValues.Remove(ControlApiTokenEnvKey);
            }

            ApplyControlApiAccessToken(_envDocument);
            AddLog($"Saved local control-plane token to {EnvFilePath}");
            StatusText = "Local control-plane settings saved";
            await LoadConfigAsync();
        }
        catch (Exception ex)
        {
            var userFacingError = DesktopOperationErrorFormatter.Build(
                operationLabel: "Save local control settings",
                fallbackStatusText: "Local control-plane save failed",
                technicalMessage: ex.Message,
                envPath: EnvFilePath,
                localControlSettingsOperation: true);
            StatusText = userFacingError.StatusText;
            AddLog($"Saving local control-plane settings failed: {ex.Message}");
            System.Windows.MessageBox.Show(
                userFacingError.DialogMessage,
                userFacingError.DialogTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            ApplySuggestedErrorAction(userFacingError);
        }
        finally
        {
            UpdateCommandStates();
        }
    }

    private async Task<bool> SaveConfigAsync(bool showUiErrors)
    {
        if (!IsBackendRootValid)
        {
            DesktopControlPlaneFeedback.ApplyOutcome(
                statusText: "Backend root is invalid",
                logMessages: ["Cannot save config because backend root is invalid."],
                notifications: [],
                setStatusText: (statusText) => StatusText = statusText,
                addLog: AddLog,
                notify: null);
            return false;
        }

        try
        {
            StatusText = "Saving config...";
            var submittedConfig = BuildConfig();
            _envDocument.Config = submittedConfig;
            var apiResult = await SaveConfigThroughControlApiAsync(submittedConfig);
            _envDocument.Config = MergeSavedConfig(submittedConfig, apiResult);
            ApplyConfigToView(_envDocument.Config);
            DesktopControlPlaneFeedback.ApplyOutcome(
                statusText: apiResult.RestartRequired ? "Config saved (restart required)" : "Config saved",
                logMessages: [$"Saved config via control API: {apiResult.EnvPath}"],
                notifications: [],
                setStatusText: (statusText) => StatusText = statusText,
                addLog: AddLog,
                notify: null);

            LastSavedAtText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            HasUnsavedChanges = false;
            return true;
        }
        catch (Exception ex)
        {
            var userFacingError = DesktopOperationErrorFormatter.Build(
                operationLabel: "Save config",
                fallbackStatusText: "Save failed",
                technicalMessage: ex.Message,
                controlApiFailure: _backendControlApiService.LastFailure,
                envPath: EnvFilePath);
            DesktopControlPlaneFeedback.ApplyError(
                statusText: userFacingError.StatusText,
                logMessage: $"Save config failed: {ex.Message}",
                showDialog: showUiErrors,
                dialogTitle: userFacingError.DialogTitle,
                dialogMessage: userFacingError.DialogMessage,
                setStatusText: (statusText) => StatusText = statusText,
                addLog: AddLog,
                showErrorDialog: (title, message) => System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error));
            ApplySuggestedErrorAction(userFacingError);

            return false;
        }
        finally
        {
            UpdateCommandStates();
        }
    }

    private async Task StartBackendAsync()
    {
        try
        {
            if (!await SaveConfigAsync(showUiErrors: true))
            {
                return;
            }
            StatusText = "Starting backend...";
            var outcome = await BackendControlPlaneFacade.StartBackendAsync(
                prepareAsync: async () => { await LoadLocalEnvDocumentAsync(suppressErrors: true); },
                tryGetStatusAsync: (cancellationToken) => _backendControlApiService.TryGetStatusAsync(cancellationToken),
                getLastFailure: () => _backendControlApiService.LastFailure,
                isImmediateFailure: IsImmediateControlApiFailure,
                startProcess: () => _botProcessService.Start(BackendRootPath),
                waitForStatusAsync: (cancellationToken) => BackendControlApiStatusWaiter.WaitForStatusAsync(
                    (innerCancellationToken) => _backendControlApiService.TryGetStatusAsync(innerCancellationToken),
                    () => _backendControlApiService.LastFailure,
                    cancellationToken: cancellationToken),
                tryStartAsync: (cancellationToken) => _backendControlApiService.TryStartAsync(cancellationToken));

            if (outcome.AppliedStatus is not null || !outcome.ControlApiReachable)
            {
                ApplyBackendRuntimeStatus(outcome.AppliedStatus, outcome.ControlApiReachable);
            }

            DesktopControlPlaneFeedback.ApplyOutcome(
                statusText: outcome.StatusText,
                logMessages: outcome.LogMessages,
                notifications: outcome.Notifications,
                setStatusText: (statusText) => StatusText = statusText,
                addLog: AddLog,
                notify: (notification) => NotificationRequested?.Invoke(this, notification));

            if (outcome.ShouldDetachProcess)
            {
                _botProcessService.Detach();
            }

            if (outcome.ShouldReloadConfig)
            {
                await LoadConfigAsync();
            }
        }
        catch (Exception ex)
        {
            var userFacingError = DesktopOperationErrorFormatter.Build(
                operationLabel: "Start backend",
                fallbackStatusText: "Start failed",
                technicalMessage: ex.Message,
                controlApiFailure: _backendControlApiService.LastFailure,
                envPath: EnvFilePath);
            DesktopControlPlaneFeedback.ApplyError(
                statusText: userFacingError.StatusText,
                logMessage: $"Start backend failed: {ex.Message}",
                showDialog: true,
                dialogTitle: userFacingError.DialogTitle,
                dialogMessage: userFacingError.DialogMessage,
                setStatusText: (statusText) => StatusText = statusText,
                addLog: AddLog,
                showErrorDialog: (title, message) => System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error));
            ApplySuggestedErrorAction(userFacingError);
        }
        finally
        {
            UpdateCommandStates();
        }
    }

    private async Task StopBackendAsync()
    {
        try
        {
            StatusText = "Stopping backend...";
            var outcome = await BackendControlPlaneFacade.StopBackendAsync(
                prepareAsync: async () => { await LoadLocalEnvDocumentAsync(suppressErrors: true); },
                tryStopAsync: (cancellationToken) => _backendControlApiService.TryStopAsync(cancellationToken),
                getLastFailure: () => _backendControlApiService.LastFailure,
                isImmediateFailure: IsImmediateControlApiFailure,
                isProcessRunning: () => _botProcessService.IsRunning,
                stopProcessAsync: () => _botProcessService.StopAsync());

            ApplyBackendRuntimeStatus(outcome.AppliedStatus, outcome.ControlApiReachable);
            DesktopControlPlaneFeedback.ApplyOutcome(
                statusText: outcome.StatusText,
                logMessages: outcome.LogMessages,
                notifications: outcome.Notifications,
                setStatusText: (statusText) => StatusText = statusText,
                addLog: AddLog,
                notify: (notification) => NotificationRequested?.Invoke(this, notification));
        }
        catch (Exception ex)
        {
            var userFacingError = DesktopOperationErrorFormatter.Build(
                operationLabel: "Stop backend",
                fallbackStatusText: "Stop failed",
                technicalMessage: ex.Message,
                controlApiFailure: _backendControlApiService.LastFailure,
                envPath: EnvFilePath);
            DesktopControlPlaneFeedback.ApplyError(
                statusText: userFacingError.StatusText,
                logMessage: $"Stop backend failed: {ex.Message}",
                showDialog: true,
                dialogTitle: userFacingError.DialogTitle,
                dialogMessage: userFacingError.DialogMessage,
                setStatusText: (statusText) => StatusText = statusText,
                addLog: AddLog,
                showErrorDialog: (title, message) => System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error));
            ApplySuggestedErrorAction(userFacingError);
        }
        finally
        {
            UpdateCommandStates();
        }
    }

    private async Task ToggleAutoStartAsync()
    {
        try
        {
            var nextValue = !AutoStartEnabled;
            _autoStartService.SetEnabled(nextValue);
            AutoStartEnabled = nextValue;
            StatusText = nextValue ? "Startup enabled for resident mode" : "Startup disabled";
            AddLog(nextValue
                ? "Enabled resident mode startup at Windows sign-in."
                : "Disabled startup at Windows sign-in. The tray behavior still works after manual launch.");
            NotificationRequested?.Invoke(
                this,
                new TrayNotification
                {
                    Title = "Local AI Runtime",
                    Message = nextValue
                        ? "Resident mode enabled. It will start minimized and ensure the runtime after Windows sign-in."
                        : "Resident mode startup disabled. Manual launch still supports tray behavior.",
                    Icon = Forms.ToolTipIcon.Info
                });
        }
        catch (Exception ex)
        {
            StatusText = "Auto-start update failed";
            AddLog($"Auto-start update failed: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"Auto-start update failed:\n{ex.Message}",
                "Auto-start failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }

        await Task.CompletedTask;
    }

    private void AutoDetectBackendRoot()
    {
        _backendRootDetected = PathDiscoveryService.TryDiscoverBackendRoot(out var detectedPath);
        BackendRootPath = detectedPath;
        StatusText = _backendRootDetected ? "Backend root detected" : "Backend root not detected";
        AddLog(_backendRootDetected
            ? $"Auto-detected backend root: {BackendRootPath}"
            : $"Using current backend root: {BackendRootPath}");
        UpdateCommandStates();
    }

    private void OpenBackendFolder()
    {
        if (!Directory.Exists(BackendRootPath))
        {
            return;
        }

        _localPathOperationsService.OpenFolder(BackendRootPath);
        AddLog($"Opened backend folder: {BackendRootPath}");
    }

    private void OpenSessionStoreFolder()
    {
        if (!IsBackendRootValid)
        {
            return;
        }

        var sessionDirectory = Path.GetDirectoryName(SessionStorePathText) ?? Path.Combine(BackendRootPath, "data");
        _localPathOperationsService.OpenFolder(sessionDirectory);
        StatusText = "Opened session store folder";
        AddLog($"Opened session store folder: {sessionDirectory}");
    }

    private void OpenImageCacheFolder()
    {
        if (!IsBackendRootValid)
        {
            return;
        }

        _localPathOperationsService.OpenFolder(ImageCachePathText);
        StatusText = "Opened image cache folder";
        AddLog($"Opened image cache folder: {ImageCachePathText}");
        RefreshHealthReport();
    }

    private void ClearImageCache()
    {
        if (!IsBackendRootValid)
        {
            return;
        }

        try
        {
            var removedEntries = _localPathOperationsService.ClearDirectoryContents(ImageCachePathText);
            StatusText = removedEntries > 0 ? "Image cache cleared" : "Image cache already empty";
            AddLog(
                removedEntries > 0
                    ? $"Cleared image cache at {ImageCachePathText}, removed {removedEntries} entr{(removedEntries == 1 ? "y" : "ies")}."
                    : $"Image cache already empty: {ImageCachePathText}");
            OnPropertyChanged(nameof(ImageCacheStateText));
        }
        catch (Exception ex)
        {
            StatusText = "Image cache clear failed";
            AddLog($"Clearing image cache failed: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"Clearing image cache failed:\n{ex.Message}",
                "Clear image cache failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ExportStateSnapshotAsync()
    {
        if (!IsBackendRootValid)
        {
            StatusText = "Backend root is invalid";
            AddLog("Cannot export state snapshot because backend root is invalid.");
            return;
        }

        try
        {
            StatusText = "Exporting state snapshot...";
            var result = await _localStateSnapshotService.ExportAsync(BackendRootPath);
            LastStateSnapshotText = result.ArchivePath;
            await RefreshStateSnapshotsAsync(result.ArchivePath);
            StatusText = "State snapshot exported";
            AddLog(
                $"Exported local state snapshot to {result.ArchivePath} with {result.IncludedEntries.Count} entr{(result.IncludedEntries.Count == 1 ? "y" : "ies")}.");
            OnPropertyChanged(nameof(StateSnapshotFolderPathText));
        }
        catch (Exception ex)
        {
            StatusText = "State snapshot export failed";
            AddLog($"Exporting state snapshot failed: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"Exporting state snapshot failed:\n{ex.Message}",
                "Export state snapshot failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            UpdateCommandStates();
        }
    }

    private async Task ExportSafeStateSnapshotAsync()
    {
        if (!IsBackendRootValid)
        {
            StatusText = "Backend root is invalid";
            AddLog("Cannot export safe state snapshot because backend root is invalid.");
            return;
        }

        try
        {
            StatusText = "Exporting safe state snapshot...";
            var result = await _localStateSnapshotService.ExportSafeAsync(BackendRootPath);
            LastStateSnapshotText = result.ArchivePath;
            await RefreshStateSnapshotsAsync(result.ArchivePath);
            StatusText = "Safe state snapshot exported";
            AddLog(
                $"Exported safe state snapshot to {result.ArchivePath} with {result.IncludedEntries.Count} entr{(result.IncludedEntries.Count == 1 ? "y" : "ies")} and no .env secrets.");
            OnPropertyChanged(nameof(StateSnapshotFolderPathText));
        }
        catch (Exception ex)
        {
            StatusText = "Safe state snapshot export failed";
            AddLog($"Exporting safe state snapshot failed: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"Exporting safe state snapshot failed:\n{ex.Message}",
                "Export safe state snapshot failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            UpdateCommandStates();
        }
    }

    private async Task RestoreLatestStateSnapshotAsync()
    {
        if (!IsBackendRootValid)
        {
            StatusText = "Backend root is invalid";
            AddLog("Cannot restore state snapshot because backend root is invalid.");
            return;
        }

        try
        {
            StatusText = "Restoring latest state snapshot...";
            var snapshots = await _localStateSnapshotService.ListAsync(BackendRootPath);
            var latestSnapshot = snapshots.FirstOrDefault()
                ?? throw new InvalidOperationException("No state snapshots are available to restore.");
            var preview = await _localStateSnapshotService.PreviewAsync(BackendRootPath, latestSnapshot.ArchivePath);
            var result = await _localStateSnapshotService.RestoreLatestAsync(BackendRootPath);
            LastStateRestoreText = result.ArchivePath;
            ApplyRestoreResultSummary(result, preview);
            StatusText = "Latest state snapshot restored";
            AddLog(
                $"Restored local state snapshot from {result.ArchivePath} with {result.RestoredEntries.Count} entr{(result.RestoredEntries.Count == 1 ? "y" : "ies")}.");
            await RefreshStateSnapshotsAsync(result.ArchivePath);
            await LoadConfigAsync();
            ApplyRestoreAvailabilityCheck();
        }
        catch (Exception ex)
        {
            StatusText = "State snapshot restore failed";
            AddLog($"Restoring latest state snapshot failed: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"Restoring the latest state snapshot failed:\n{ex.Message}",
                "Restore state snapshot failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            UpdateCommandStates();
        }
    }

    private async Task RestoreSelectedStateSnapshotAsync()
    {
        if (!IsBackendRootValid || SelectedStateSnapshot is null)
        {
            return;
        }

        try
        {
            var preview = await _localStateSnapshotService.PreviewAsync(
                BackendRootPath,
                SelectedStateSnapshot.ArchivePath);

            if (!_confirmationDialogService.Confirm(
                    "Restore selected snapshot",
                    BuildRestoreConfirmationMessage(SelectedStateSnapshot, preview)))
            {
                StatusText = "Selected snapshot restore cancelled";
                AddLog($"Cancelled restoring selected snapshot: {SelectedStateSnapshot.ArchivePath}");
                return;
            }

            StatusText = "Restoring selected state snapshot...";
            var result = await _localStateSnapshotService.RestoreAsync(
                BackendRootPath,
                SelectedStateSnapshot.ArchivePath);
            LastStateRestoreText = result.ArchivePath;
            ApplyRestoreResultSummary(result, preview);
            StatusText = "Selected state snapshot restored";
            AddLog(
                $"Restored selected state snapshot from {result.ArchivePath} with {result.RestoredEntries.Count} entr{(result.RestoredEntries.Count == 1 ? "y" : "ies")}.");
            await RefreshStateSnapshotsAsync(result.ArchivePath);
            await LoadConfigAsync();
            ApplyRestoreAvailabilityCheck();
        }
        catch (Exception ex)
        {
            StatusText = "Selected snapshot restore failed";
            AddLog($"Restoring selected snapshot failed: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"Restoring the selected snapshot failed:\n{ex.Message}",
                "Restore selected snapshot failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            UpdateCommandStates();
        }
    }

    private async Task DeleteSelectedStateSnapshotAsync()
    {
        if (!IsBackendRootValid || SelectedStateSnapshot is null)
        {
            return;
        }

        try
        {
            var archivePath = SelectedStateSnapshot.ArchivePath;

            if (!_confirmationDialogService.Confirm(
                    "Delete selected snapshot",
                    BuildDeleteConfirmationMessage(SelectedStateSnapshot)))
            {
                StatusText = "Snapshot delete cancelled";
                AddLog($"Cancelled deleting selected snapshot: {archivePath}");
                return;
            }

            StatusText = "Deleting selected snapshot...";
            await _localStateSnapshotService.DeleteAsync(archivePath);
            AddLog($"Deleted state snapshot: {archivePath}");
            await RefreshStateSnapshotsAsync();
            StatusText = "Selected snapshot deleted";

            if (string.Equals(LastStateSnapshotText, archivePath, StringComparison.OrdinalIgnoreCase))
            {
                LastStateSnapshotText = "No state snapshot exported yet";
            }

            if (string.Equals(LastStateRestoreText, archivePath, StringComparison.OrdinalIgnoreCase))
            {
                ResetRestoreResultState();
            }
        }
        catch (Exception ex)
        {
            StatusText = "Snapshot delete failed";
            AddLog($"Deleting selected snapshot failed: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"Deleting the selected snapshot failed:\n{ex.Message}",
                "Delete selected snapshot failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            UpdateCommandStates();
        }
    }

    private void OpenStateSnapshotFolder()
    {
        if (!IsBackendRootValid)
        {
            return;
        }

        _localPathOperationsService.OpenFolder(StateSnapshotFolderPathText);
        StatusText = "Opened state snapshot folder";
        AddLog($"Opened state snapshot folder: {StateSnapshotFolderPathText}");
    }

    private void ApplyBaseUrlPreset(object? parameter)
    {
        var preset = parameter as string ?? string.Empty;
        OpenAiBaseUrl = preset switch
        {
            "__official__" => string.Empty,
            _ => preset
        };

        AddLog($"Applied API base URL preset: {(string.IsNullOrWhiteSpace(OpenAiBaseUrl) ? "official default" : OpenAiBaseUrl)}");
    }

    private void ClearLogs()
    {
        _logLines.Clear();
        LogText = string.Empty;
        AddLog("Logs cleared.");
    }

    private void OnProcessLogReceived(object? sender, string message)
    {
        RunOnUiDispatcher(() => AddLog(message), DispatcherPriority.Background);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        RunOnUiDispatcher(() =>
        {
            IsProcessRunning = false;
            StatusText = "Backend exited";
            UpdateCommandStates();
        });
    }

    private BotConfig BuildConfig()
    {
        return new BotConfig
        {
            OpenAiApiKey = OpenAiApiKey.Trim(),
            OpenAiDefaultApiKey = OpenAiDefaultApiKey.Trim(),
            OpenAiDefaultModel = OpenAiDefaultModel.Trim(),
            OpenAiModel = OpenAiModel.Trim(),
            OpenAiBaseUrl = OpenAiBaseUrl.Trim(),
            OpenAiDefaultBaseUrl = OpenAiDefaultBaseUrl.Trim(),
            OpenAiDefaultReasoningEffort = OpenAiDefaultReasoningEffort.Trim(),
            OpenAiAdvancedReasoningEffort = OpenAiAdvancedReasoningEffort.Trim(),
            OpenAiDefaultTextVerbosity = OpenAiDefaultTextVerbosity.Trim(),
            OpenAiAdvancedTextVerbosity = OpenAiAdvancedTextVerbosity.Trim(),
            OpenAiDefaultEnableWebSearch = OpenAiDefaultEnableWebSearch.Trim(),
            OpenAiAdvancedEnableWebSearch = OpenAiAdvancedEnableWebSearch.Trim(),
            OpenAiDefaultEnableCodeInterpreter = OpenAiDefaultEnableCodeInterpreter.Trim(),
            OpenAiAdvancedEnableCodeInterpreter = OpenAiAdvancedEnableCodeInterpreter.Trim(),
            OpenAiAdvancedTriggerPrefixes = OpenAiAdvancedTriggerPrefixes.Trim(),
            NapCatWsUrl = NapCatWsUrl.Trim(),
            NapCatToken = NapCatToken.Trim(),
            WechatBridgeUrl = WechatBridgeUrl.Trim(),
            WechatBridgeToken = WechatBridgeToken.Trim(),
            WechatBotPrefix = WechatBotPrefix.Trim(),
            BotPrefix = BotPrefix.Trim(),
            BotSystemPrompt = NormalizeBotSystemPrompt(BotSystemPrompt),
            BotPersona = BotPersona.Trim(),
            MaxOutputChars = MaxOutputChars.Trim(),
            AllowedChatIds = AllowedChatIds.Trim(),
            AllowedUserIds = AllowedUserIds.Trim()
        };
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
            BotSystemPrompt = NormalizeBotSystemPrompt(config.BotSystemPrompt),
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
            mergedConfig.BotSystemPrompt = NormalizeBotSystemPrompt(submittedConfig.BotSystemPrompt);
        }

        return mergedConfig;
    }

    private void ApplyConfigToView(BotConfig config)
    {
        OpenAiApiKey = config.OpenAiApiKey;
        OpenAiDefaultApiKey = config.OpenAiDefaultApiKey;
        OpenAiDefaultModel = config.OpenAiDefaultModel;
        OpenAiModel = config.OpenAiModel;
        OpenAiBaseUrl = config.OpenAiBaseUrl;
        OpenAiDefaultBaseUrl = config.OpenAiDefaultBaseUrl;
        OpenAiDefaultReasoningEffort = config.OpenAiDefaultReasoningEffort;
        OpenAiAdvancedReasoningEffort = config.OpenAiAdvancedReasoningEffort;
        OpenAiDefaultTextVerbosity = config.OpenAiDefaultTextVerbosity;
        OpenAiAdvancedTextVerbosity = config.OpenAiAdvancedTextVerbosity;
        OpenAiDefaultEnableWebSearch = config.OpenAiDefaultEnableWebSearch;
        OpenAiAdvancedEnableWebSearch = config.OpenAiAdvancedEnableWebSearch;
        OpenAiDefaultEnableCodeInterpreter = config.OpenAiDefaultEnableCodeInterpreter;
        OpenAiAdvancedEnableCodeInterpreter = config.OpenAiAdvancedEnableCodeInterpreter;
        OpenAiAdvancedTriggerPrefixes = config.OpenAiAdvancedTriggerPrefixes;
        NapCatWsUrl = config.NapCatWsUrl;
        NapCatToken = config.NapCatToken;
        WechatBridgeUrl = config.WechatBridgeUrl;
        WechatBridgeToken = config.WechatBridgeToken;
        WechatBotPrefix = config.WechatBotPrefix;
        BotPrefix = config.BotPrefix;
        BotSystemPrompt = NormalizeBotSystemPrompt(config.BotSystemPrompt);
        BotPersona = config.BotPersona;
        MaxOutputChars = config.MaxOutputChars;
        AllowedChatIds = config.AllowedChatIds;
        AllowedUserIds = config.AllowedUserIds;
    }

    private void AddLog(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        lock (_logSyncRoot)
        {
            _logLines.Enqueue(line);

            while (_logLines.Count > 400)
            {
                _logLines.Dequeue();
            }

            _logDirty = true;
        }
    }

    private async void OnStatusPollTimerTick(object? sender, EventArgs e)
    {
        await LoadLocalEnvDocumentAsync(suppressErrors: true);
        var status = await _backendControlApiService.TryGetStatusAsync();
        var statusFailure = _backendControlApiService.LastFailure;
        var pollOutcome = BackendControlApiStatusPollCoordinator.Evaluate(
            _runtimeSnapshot,
            _controlApiPollState,
            status,
            statusFailure,
            ControlApiRecoveryAttemptThreshold,
            ControlApiOutageNotificationThreshold);
        _controlApiPollState = pollOutcome.NextPollState;

        foreach (var logMessage in pollOutcome.LogMessages)
        {
            AddLog(logMessage);
        }

        foreach (var notification in pollOutcome.Notifications)
        {
            NotificationRequested?.Invoke(this, notification);
        }

        if (!pollOutcome.ShouldApplyRuntimeStatus)
        {
            _runtimeSnapshot = pollOutcome.NextRuntimeSnapshot;
            RefreshHealthReport();
            OnPropertyChanged(nameof(IsControlApiReachable));
            NotifyRuntimeSnapshotChanged();

            if (pollOutcome.ShouldAttemptRecovery)
            {
                _ = TryRecoverControlApiAsync("status-poll");
            }

            return;
        }

        ApplyBackendRuntimeStatus(status, true);
        ResetControlApiFailureState();
    }

    private void NotifyRuntimeSnapshotChanged()
    {
        BackendRuntimeSnapshotViewHelper.NotifyRuntimeSnapshotChanged(OnPropertyChanged);
    }

    private void RefreshHealthReport()
    {
        _healthReport = DesktopHealthReportBuilder.Build(
            BuildConfig(),
            _runtimeSnapshot,
            _backendControlApiService.LastFailure,
            IsBackendRootValid,
            HasUnsavedChanges,
            AutoStartEnabled);
        ReplaceHealthChecks(_healthReport.Checks);
        OnPropertyChanged(nameof(HealthStateText));
        OnPropertyChanged(nameof(HealthSummaryText));
        OnPropertyChanged(nameof(HealthChecklistStatusText));
        OnPropertyChanged(nameof(HealthReadyNowText));
        OnPropertyChanged(nameof(HealthPrimaryActionText));
        OnPropertyChanged(nameof(HealthPrimaryActionLabel));
        OnPropertyChanged(nameof(HealthPrimaryActionKey));
        OnPropertyChanged(nameof(HealthRuntimeExplanationText));
        OnPropertyChanged(nameof(HealthLatestIssueText));
        OnPropertyChanged(nameof(HealthLatestIssueActionLabel));
        OnPropertyChanged(nameof(HealthLatestIssueActionKey));
        OnPropertyChanged(nameof(IsOverallReadinessReady));
        OnPropertyChanged(nameof(IsOverallReadinessSetupComplete));
        OnPropertyChanged(nameof(OverallReadinessStateText));
        OnPropertyChanged(nameof(OverallReadinessSummaryText));
        OnPropertyChanged(nameof(OverallReadinessRecentActivityText));
        OnPropertyChanged(nameof(OverallReadinessActionLabel));
        OnPropertyChanged(nameof(OverallReadinessActionKey));
        OnPropertyChanged(nameof(FirstRunGuideText));
        OnPropertyChanged(nameof(FirstRunGuideSteps));
        OnPropertyChanged(nameof(FirstRunGuideProgressText));
        OnPropertyChanged(nameof(FirstRunGuideCurrentStepText));
        OnPropertyChanged(nameof(IsFirstRunGuideComplete));
        OnPropertyChanged(nameof(FirstRunGuideCompletionText));
        OnPropertyChanged(nameof(FirstRunStepsText));
        OnPropertyChanged(nameof(DailyUseGuideText));
        OnPropertyChanged(nameof(DailyUseGuideSteps));
        OnPropertyChanged(nameof(DailyUseGuideProgressText));
        OnPropertyChanged(nameof(DailyUseGuideCurrentStepText));
        OnPropertyChanged(nameof(IsDailyUseGuideComplete));
        OnPropertyChanged(nameof(DailyUseGuideCompletionText));
        OnPropertyChanged(nameof(DailyUseStepsText));
        OnPropertyChanged(nameof(ControlApiTokenStateText));
        OnPropertyChanged(nameof(ControlApiEndpointText));
        OnPropertyChanged(nameof(SessionStorePathText));
        OnPropertyChanged(nameof(SessionStoreStateText));
        OnPropertyChanged(nameof(ImageCachePathText));
        OnPropertyChanged(nameof(ImageCacheStateText));
        OnPropertyChanged(nameof(ActivityStatePathText));
        OnPropertyChanged(nameof(StateSnapshotFolderPathText));
        OnPropertyChanged(nameof(LastStateRestoreText));
        OnPropertyChanged(nameof(SnapshotRetentionHintText));
    }

    private async Task RefreshStateSnapshotsAsync()
    {
        await RefreshStateSnapshotsAsync(selectArchivePath: null);
    }

    private async Task RefreshStateSnapshotsAsync(string? selectArchivePath)
    {
        if (!IsBackendRootValid)
        {
            ReplaceStateSnapshots([]);
            SelectedStateSnapshot = null;
            SelectedStateSnapshotDiffText = "Select a snapshot to preview differences.";
            SelectedStateSnapshotAdviceText = "Restore advice will appear here.";
            return;
        }

        var snapshots = await _localStateSnapshotService.ListAsync(BackendRootPath);
        var selectedArchivePath = !string.IsNullOrWhiteSpace(selectArchivePath)
            ? selectArchivePath
            : SelectedStateSnapshot?.ArchivePath;

        ReplaceStateSnapshots(snapshots);
        SelectedStateSnapshot = StateSnapshots.FirstOrDefault(
            (snapshot) => string.Equals(snapshot.ArchivePath, selectedArchivePath, StringComparison.OrdinalIgnoreCase))
            ?? StateSnapshots.FirstOrDefault();
    }

    private async Task RefreshSelectedStateSnapshotPreviewAsync()
    {
        var selectedSnapshot = SelectedStateSnapshot;

        if (selectedSnapshot is null || !IsBackendRootValid)
        {
            SelectedStateSnapshotDiffText = "Select a snapshot to preview differences.";
            SelectedStateSnapshotAdviceText = "Restore advice will appear here.";
            return;
        }

        SelectedStateSnapshotDiffText = "Loading diff preview...";
        SelectedStateSnapshotAdviceText = "Loading restore advice...";

        try
        {
            var preview = await _localStateSnapshotService.PreviewAsync(
                BackendRootPath,
                selectedSnapshot.ArchivePath);

            if (!string.Equals(SelectedStateSnapshot?.ArchivePath, selectedSnapshot.ArchivePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SelectedStateSnapshotDiffText = string.Join(Environment.NewLine, preview.Lines);
            SelectedStateSnapshotAdviceText = string.Join(Environment.NewLine, preview.Recommendations);
        }
        catch (Exception ex)
        {
            if (!string.Equals(SelectedStateSnapshot?.ArchivePath, selectedSnapshot.ArchivePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SelectedStateSnapshotDiffText = $"Diff preview unavailable: {ex.Message}";
            SelectedStateSnapshotAdviceText = "Review the snapshot details carefully before restoring.";
        }
    }

    private void ReplaceHealthChecks(IEnumerable<DesktopHealthCheckItem> items)
    {
        HealthChecks.Clear();

        foreach (var item in items)
        {
            HealthChecks.Add(item);
        }
    }

    private void ReplaceStateSnapshots(IEnumerable<LocalStateSnapshotDescriptor> items)
    {
        StateSnapshots.Clear();

        foreach (var item in items)
        {
            StateSnapshots.Add(item);
        }
    }

    private void ApplySuggestedErrorAction(DesktopUserFacingOperationError error)
    {
        if (string.IsNullOrWhiteSpace(error.SuggestedActionKey))
        {
            return;
        }

        HealthActionRequested?.Invoke(this, error.SuggestedActionKey);
    }

    private void ExecuteHealthAction(object? parameter)
    {
        var actionKey = parameter as string;

        if (string.IsNullOrWhiteSpace(actionKey))
        {
            return;
        }

        switch (actionKey)
        {
            case DesktopHealthActionKeys.SaveConfig:
                if (SaveCommand.CanExecute(null))
                {
                    SaveCommand.Execute(null);
                }

                return;
            case DesktopHealthActionKeys.StartBackend:
                if (StartCommand.CanExecute(null))
                {
                    StartCommand.Execute(null);
                }

                return;
            case DesktopHealthActionKeys.ReloadConfig:
                if (ReloadCommand.CanExecute(null))
                {
                    ReloadCommand.Execute(null);
                }

                return;
            case DesktopHealthActionKeys.OpenBackendFolder:
                if (OpenBackendFolderCommand.CanExecute(null))
                {
                    OpenBackendFolderCommand.Execute(null);
                }

                return;
            case DesktopHealthActionKeys.ToggleAutoStart:
                if (ToggleAutoStartCommand.CanExecute(null))
                {
                    ToggleAutoStartCommand.Execute(null);
                }

                return;
            default:
                HealthActionRequested?.Invoke(this, actionKey);
                return;
        }
    }

    private void ApplyLocalControlPlaneSettings(EnvDocument document)
    {
        if (document.ExtraValues.TryGetValue(ControlApiTokenEnvKey, out var accessToken))
        {
            ControlApiToken = accessToken;
        }
        else
        {
            ControlApiToken = string.Empty;
        }

        OnPropertyChanged(nameof(ControlApiEndpointText));
    }

    private void ApplyBackendRuntimeStatus(BackendRuntimeStatus? status, bool controlApiReachable)
    {
        var projection = BackendRuntimeSnapshotCoordinator.ProjectRuntimeStatus(
            status,
            controlApiReachable,
            new BackendChannelActivityContext(
                QqRecentActivities,
                _lastQqRequestEventKey,
                _lastQqFailureEventKey,
                PinSelectedQqActivity,
                SelectedQqRecentActivity),
            new BackendChannelActivityContext(
                WechatRecentActivities,
                _lastWechatRequestEventKey,
                _lastWechatFailureEventKey,
                PinSelectedWechatActivity,
                SelectedWechatRecentActivity));

        BackendRuntimeSnapshotViewHelper.ReplaceRecentActivities(QqRecentActivities, projection.QqActivity.Items);
        BackendRuntimeSnapshotViewHelper.ReplaceRecentActivities(WechatRecentActivities, projection.WechatActivity.Items);
        _lastQqRequestEventKey = projection.QqActivity.LastRequestEventKey;
        _lastQqFailureEventKey = projection.QqActivity.LastFailureEventKey;
        _selectedQqRecentActivity = projection.QqActivity.SelectedItem;
        _lastWechatRequestEventKey = projection.WechatActivity.LastRequestEventKey;
        _lastWechatFailureEventKey = projection.WechatActivity.LastFailureEventKey;
        _selectedWechatRecentActivity = projection.WechatActivity.SelectedItem;
        IsProcessRunning = projection.SnapshotState.RuntimeActive == true;
        _runtimeSnapshot = projection.SnapshotState;
        RefreshHealthReport();

        if (HasStateRestoreResult)
        {
            ApplyRestoreAvailabilityCheck();
        }

        OnPropertyChanged(nameof(IsControlApiReachable));
        NotifyRuntimeSnapshotChanged();
        PersistActivityStateIfPossible();
    }

    private void LoadActivityState()
    {
        if (!IsBackendRootValid)
        {
            ApplyActivityState(_activityStatePolicy.CreateDefaultState());
            return;
        }

        ApplyActivityState(_activityStateStore.Load(BackendRootPath));
    }

    private void PersistActivityStateIfPossible()
    {
        if (_restoringActivityState || !IsBackendRootValid)
        {
            return;
        }

        var state = _activityStatePolicy.CreateSnapshot(
            QqRecentActivities,
            WechatRecentActivities,
            SelectedQqRecentActivity,
            SelectedWechatRecentActivity,
            PinSelectedQqActivity,
            PinSelectedWechatActivity,
            ShowOnlyQqFailures,
            ShowOnlyWechatFailures);

        try
        {
            _activityStateStore.Save(BackendRootPath, state);
        }
        catch (Exception ex)
        {
            AddLog($"Failed to persist local activity state: {ex.Message}");
        }
    }

    private void ApplyActivityState(DesktopActivityState? state)
    {
        var projection = BackendRuntimeSnapshotCoordinator.ProjectActivityRestore(
            _activityStatePolicy,
            state);

        _restoringActivityState = true;
        try
        {
            BackendRuntimeSnapshotViewHelper.ReplaceRecentActivities(QqRecentActivities, projection.QqRecentActivities);
            BackendRuntimeSnapshotViewHelper.ReplaceRecentActivities(WechatRecentActivities, projection.WechatRecentActivities);
            _pinSelectedQqActivity = projection.PinSelectedQqActivity;
            _pinSelectedWechatActivity = projection.PinSelectedWechatActivity;
            _showOnlyQqFailures = projection.ShowOnlyQqFailures;
            _showOnlyWechatFailures = projection.ShowOnlyWechatFailures;
            _selectedQqRecentActivity = projection.SelectedQqRecentActivity;
            _selectedWechatRecentActivity = projection.SelectedWechatRecentActivity;
        }
        finally
        {
            _restoringActivityState = false;
        }

        QqRecentActivitiesView.Refresh();
        WechatRecentActivitiesView.Refresh();
        SelectedQqRecentActivity = BackendRecentActivityCoordinator.ResolveSelectionAfterFilterChange(
            QqRecentActivities,
            SelectedQqRecentActivity,
            ShowOnlyQqFailures);
        SelectedWechatRecentActivity = BackendRecentActivityCoordinator.ResolveSelectionAfterFilterChange(
            WechatRecentActivities,
            SelectedWechatRecentActivity,
            ShowOnlyWechatFailures);
        OnPropertyChanged(nameof(OverallReadinessRecentActivityText));
        OnPropertyChanged(nameof(OverallReadinessActionLabel));
        OnPropertyChanged(nameof(OverallReadinessActionKey));
        NotifyRuntimeSnapshotChanged();
    }

    private void ResetControlApiFailureState()
    {
        _controlApiPollState = new BackendControlApiPollState();
        _controlApiRecoveryInProgress = false;
    }

    private async Task TryRecoverControlApiAsync(string reason)
    {
        if (_controlApiRecoveryInProgress || !IsBackendRootValid)
        {
            return;
        }

        _controlApiRecoveryInProgress = true;

        try
        {
            var outcome = await BackendControlApiRecoveryCoordinator.TryRecoverAsync(
                reason,
                prepareAsync: async () => { await LoadLocalEnvDocumentAsync(suppressErrors: true); },
                tryGetStatusAsync: (cancellationToken) => _backendControlApiService.TryGetStatusAsync(cancellationToken),
                getLastFailure: () => _backendControlApiService.LastFailure,
                isImmediateFailure: IsImmediateControlApiFailure,
                isProcessRunning: () => _botProcessService.IsRunning,
                startProcess: () => _botProcessService.Start(BackendRootPath),
                tryStartAsync: (cancellationToken) => _backendControlApiService.TryStartAsync(cancellationToken),
                waitForStatusAsync: (cancellationToken) => BackendControlApiStatusWaiter.WaitForStatusAsync(
                    (innerCancellationToken) => _backendControlApiService.TryGetStatusAsync(innerCancellationToken),
                    () => _backendControlApiService.LastFailure,
                    cancellationToken: cancellationToken),
                detachProcess: () => _botProcessService.Detach());

            foreach (var logMessage in outcome.LogMessages)
            {
                AddLog(logMessage);
            }

            if (outcome.RecoveredStatus is not null)
            {
                ApplyBackendRuntimeStatus(outcome.RecoveredStatus, true);
                ResetControlApiFailureState();
            }
        }
        finally
        {
            _controlApiRecoveryInProgress = false;
        }
    }

    private async Task<BackendControlConfigResponse> SaveConfigThroughControlApiAsync(BotConfig config)
    {
        return await BackendControlPlaneFacade.SaveConfigAsync(
            prepareAsync: async () => { await LoadLocalEnvDocumentAsync(suppressErrors: true); },
            config,
            trySaveConfigAsync: (submittedConfig, cancellationToken) => _backendControlApiService.TrySaveConfigAsync(submittedConfig, cancellationToken),
            getLastFailure: () => _backendControlApiService.LastFailure,
            tryGetStatusAsync: (cancellationToken) => _backendControlApiService.TryGetStatusAsync(cancellationToken),
            isImmediateFailure: IsImmediateControlApiFailure,
            tryRecoverControlApiAsync: () => TryRecoverControlApiAsync("save-config"));
    }

    private async Task<EnvDocument> LoadLocalEnvDocumentAsync(bool suppressErrors = false)
    {
        if (!IsBackendRootValid)
        {
            _backendControlApiService.SetAccessToken(null);
            return new EnvDocument();
        }

        try
        {
            var document = await _localConfigFallbackReader.LoadAsync(BackendRootPath);
            ApplyControlApiAccessToken(document);
            return document;
        }
        catch
        {
            _backendControlApiService.SetAccessToken(null);

            if (suppressErrors)
            {
                return new EnvDocument();
            }

            throw;
        }
    }

    private void ApplyControlApiAccessToken(EnvDocument? document)
    {
        if (document?.ExtraValues.TryGetValue(ControlApiTokenEnvKey, out var accessToken) == true &&
            !string.IsNullOrWhiteSpace(accessToken))
        {
            _backendControlApiService.SetAccessToken(accessToken);
            return;
        }

        _backendControlApiService.SetAccessToken(null);
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

    private bool FilterQqRecentActivity(object item)
    {
        return BackendRecentActivityViewStateHelper.ShouldInclude(item, ShowOnlyQqFailures);
    }

    private bool FilterWechatRecentActivity(object item)
    {
        return BackendRecentActivityViewStateHelper.ShouldInclude(item, ShowOnlyWechatFailures);
    }

    private static string NormalizeBotSystemPrompt(string? botSystemPrompt)
    {
        var normalizedSystemPrompt = string.IsNullOrWhiteSpace(botSystemPrompt)
            ? string.Empty
            : botSystemPrompt.Trim();

        return string.IsNullOrWhiteSpace(normalizedSystemPrompt)
            ? DefaultBotInstructionsTextValue
            : normalizedSystemPrompt;
    }

    private static string BuildEffectiveBotInstructions(string? botSystemPrompt, string? botPersona)
    {
        var normalizedSystemPrompt = NormalizeBotSystemPrompt(botSystemPrompt);
        var normalizedPersona = string.IsNullOrWhiteSpace(botPersona) ? string.Empty : botPersona.Trim();

        if (string.IsNullOrWhiteSpace(normalizedPersona))
        {
            return normalizedSystemPrompt;
        }

        return $"{normalizedSystemPrompt}{Environment.NewLine}{Environment.NewLine}附加人格设定:{Environment.NewLine}{normalizedPersona}";
    }

    private static string BuildSelectedStateSnapshotImpactText(LocalStateSnapshotDescriptor snapshot)
    {
        var impactParts = new List<string>();

        if (snapshot.IncludedEntries.Any(static entry => string.Equals(entry, "app/.env", StringComparison.Ordinal)))
        {
            impactParts.Add(".env");
        }

        if (snapshot.IncludedEntries.Any(static entry => entry.StartsWith("app/data/", StringComparison.Ordinal)))
        {
            impactParts.Add("data/");
        }

        if (snapshot.IncludedEntries.Any(static entry => string.Equals(entry, "desktop/activity-state.json", StringComparison.Ordinal)))
        {
            impactParts.Add("desktop activity state");
        }

        var overwriteTargetText = impactParts.Count > 0
            ? string.Join(", ", impactParts)
            : "the files listed in this snapshot";
        var secretRiskText = snapshot.IncludesSecrets
            ? "This snapshot includes secrets from .env."
            : "No .env secret flag was recorded for this snapshot.";

        return $"Restoring this snapshot will overwrite {overwriteTargetText}. {secretRiskText}";
    }

    private static string BuildRestoreConfirmationMessage(
        LocalStateSnapshotDescriptor snapshot,
        LocalStateSnapshotPreviewResult preview)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"Restore snapshot: {snapshot.FileName}",
                snapshot.Summary,
                BuildSelectedStateSnapshotImpactText(snapshot),
                string.Join(Environment.NewLine, preview.Lines),
                string.Join(Environment.NewLine, preview.Recommendations),
                "Continue?"
            ]);
    }

    private static string BuildRestoreSummaryText(
        LocalStateSnapshotRestoreResult restoreResult,
        LocalStateSnapshotPreviewResult? preview)
    {
        var restoredTargets = new List<string>();

        if (restoreResult.RestoredEntries.Any(static entry => string.Equals(entry, "app/.env", StringComparison.Ordinal)))
        {
            restoredTargets.Add(".env");
        }

        if (restoreResult.RestoredEntries.Any(static entry => entry.StartsWith("app/data/", StringComparison.Ordinal)))
        {
            restoredTargets.Add("data/");
        }

        if (restoreResult.RestoredEntries.Any(static entry => string.Equals(entry, "desktop/activity-state.json", StringComparison.Ordinal)))
        {
            restoredTargets.Add("desktop activity state");
        }

        var targetText = restoredTargets.Count > 0
            ? string.Join(", ", restoredTargets)
            : "tracked state files";
        var archiveName = Path.GetFileName(restoreResult.ArchivePath);
        var sessionCountLine = preview?.Lines.FirstOrDefault(
            static line => line.StartsWith("sessions.json conversations:", StringComparison.Ordinal));
        var latestActivityLine = preview?.Lines.FirstOrDefault(
            static line => line.StartsWith("sessions.json latest activity:", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(sessionCountLine) && string.IsNullOrWhiteSpace(latestActivityLine))
        {
            return $"Restored {targetText} from {archiveName}.";
        }

        var summaryParts = new List<string>
        {
            $"Restored {targetText} from {archiveName}."
        };

        if (!string.IsNullOrWhiteSpace(sessionCountLine))
        {
            summaryParts.Add(
                $"Session count now should match snapshot: {ExtractSnapshotSide(sessionCountLine)} conversations.");
        }

        if (!string.IsNullOrWhiteSpace(latestActivityLine))
        {
            summaryParts.Add(
                $"Latest session activity now should match snapshot: {ExtractSnapshotSide(latestActivityLine)}.");
        }

        return string.Join(" ", summaryParts);
    }

    private void ApplyRestoreResultSummary(
        LocalStateSnapshotRestoreResult restoreResult,
        LocalStateSnapshotPreviewResult? preview)
    {
        _lastStateRestoreResult = restoreResult;
        _lastStateRestorePreview = preview;
        LastStateRestoreSummaryText = BuildRestoreSummaryText(restoreResult, preview);
        LastStateRestoreIssueText = BuildRestoreIssueText();
        LastStateRestoreTargetsText = BuildRestoreTargetsText(restoreResult);
        LastStateRestoreSessionsText = BuildRestoreSessionSummaryText(preview);
        LastStateRestoreLatestActivityText = BuildRestoreLatestActivitySummaryText(preview);
        LastStateRestoreAdviceText = BuildRestoreAdviceText(preview);
    }

    private void ApplyRestoreAvailabilityCheck()
    {
        LastStateRestoreIssueText = BuildRestoreIssueText();
        LastStateRestoreControlPlaneText = BuildRestoreControlPlaneText();
        LastStateRestoreRuntimeText = BuildRestoreRuntimeText();
        var actions = BuildRestoreResultActions();
        ApplyRestoreResultActions(actions);
        LastStateRestoreNextStepText = BuildRestoreNextStepText(actions);
    }

    private string BuildRestoreIssueText()
    {
        var lastFailure = _backendControlApiService.LastFailure;

        if (lastFailure.Kind == BackendControlApiFailureKind.Unauthorized)
        {
            return DidLastRestoreChangeControlApiToken()
                ? "Issue: this restore changed the local control token, so the desktop token no longer matches the backend."
                : "Issue: desktop cannot reattach after restore because the local control API token no longer matches.";
        }

        if (!IsControlApiReachable)
        {
            return StartCommand.CanExecute(null)
                ? "Issue: restore finished, but the backend host is stopped so the desktop cannot reattach yet."
                : "Issue: restore finished, but the desktop is still waiting to reattach to the local control API.";
        }

        if (StartCommand.CanExecute(null))
        {
            return DidLastRestoreOverwriteEnv()
                ? "Issue: restore finished and the snapshot changed runtime settings, but the backend host is currently stopped."
                : "Issue: the backend host is currently stopped after restore.";
        }

        if (!IsQqRuntimeReady())
        {
            return DidLastRestoreChangeNapCatSettings()
                ? "Issue: QQ is still not ready because the restored snapshot changed NapCat settings and the runtime has not reconnected yet."
                : "Issue: the backend is running, but QQ is still not ready after restore. Check NapCat connection settings.";
        }

        if (IsWechatConfiguredAfterRestore() && !IsWechatRuntimeReady())
        {
            return DidLastRestoreChangeWechatSettings()
                ? "Issue: WeChat is still not ready because the restored snapshot changed bridge settings and the worker has not reconnected yet."
                : "Issue: WeChat is configured, but the bridge is not ready after restore. Check bridge settings.";
        }

        return DidLastRestoreOverwriteEnv() || DidLastRestoreRestoreSessionStore()
            ? "Issue: no immediate post-restore problems were detected. The restored state is ready to verify."
            : "Issue: no immediate post-restore problems were detected.";
    }

    private IReadOnlyList<RestoreResultAction> BuildRestoreResultActions()
    {
        var actions = new List<RestoreResultAction>();
        var lastFailure = _backendControlApiService.LastFailure;

        void addAction(string label, string key)
        {
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (actions.Any((action) => string.Equals(action.Key, key, StringComparison.Ordinal)))
            {
                return;
            }

            actions.Add(new RestoreResultAction(label, key));
        }

        if (lastFailure.Kind == BackendControlApiFailureKind.Unauthorized)
        {
            addAction(
                DidLastRestoreChangeControlApiToken() ? "Update local control token" : "Open local control settings",
                DesktopHealthActionKeys.FocusControlApiToken);
            addAction("Reload config", DesktopHealthActionKeys.ReloadConfig);

            if (StartCommand.CanExecute(null))
            {
                addAction("Start backend", DesktopHealthActionKeys.StartBackend);
            }

            return actions;
        }

        if (!IsControlApiReachable)
        {
            if (StartCommand.CanExecute(null))
            {
                addAction("Start backend", DesktopHealthActionKeys.StartBackend);
                addAction("Reload config", DesktopHealthActionKeys.ReloadConfig);
            }
            else
            {
                addAction("Reload config", DesktopHealthActionKeys.ReloadConfig);
            }

            if (DidLastRestoreOverwriteEnv())
            {
                addAction("Open local control settings", DesktopHealthActionKeys.FocusControlApiToken);
            }

            return actions;
        }

        if (StartCommand.CanExecute(null))
        {
            addAction("Start backend", DesktopHealthActionKeys.StartBackend);
            addAction("Reload config", DesktopHealthActionKeys.ReloadConfig);
            addAction("Open local control settings", DesktopHealthActionKeys.FocusControlApiToken);
            return actions;
        }

        if (!IsQqRuntimeReady() || !IsWechatRuntimeReady())
        {
            if (!IsQqRuntimeReady())
            {
                addAction(
                    DidLastRestoreChangeNapCatSettings() ? "Review restored NapCat settings" : "Check NapCat config",
                    DesktopHealthActionKeys.FocusNapCatUrl);
                addAction("Reload config", DesktopHealthActionKeys.ReloadConfig);
                return actions;
            }

            addAction(
                DidLastRestoreChangeWechatSettings() ? "Review restored WeChat bridge" : "Check WeChat bridge",
                DesktopHealthActionKeys.FocusWechatUrl);
            addAction("Reload config", DesktopHealthActionKeys.ReloadConfig);
            return actions;
        }

        addAction("Reload config", DesktopHealthActionKeys.ReloadConfig);
        addAction("Open local control settings", DesktopHealthActionKeys.FocusControlApiToken);
        return actions;
    }

    private void ApplyRestoreResultActions(IReadOnlyList<RestoreResultAction> actions)
    {
        var primaryAction = actions.ElementAtOrDefault(0);
        var secondaryAction = actions.ElementAtOrDefault(1);
        var tertiaryAction = actions.ElementAtOrDefault(2);

        LastStateRestorePrimaryActionLabel = primaryAction?.Label ?? string.Empty;
        LastStateRestorePrimaryActionKey = primaryAction?.Key ?? string.Empty;
        LastStateRestoreSecondaryActionLabel = secondaryAction?.Label ?? string.Empty;
        LastStateRestoreSecondaryActionKey = secondaryAction?.Key ?? string.Empty;
        LastStateRestoreTertiaryActionLabel = tertiaryAction?.Label ?? string.Empty;
        LastStateRestoreTertiaryActionKey = tertiaryAction?.Key ?? string.Empty;
    }

    private static string BuildRestoreTargetsText(LocalStateSnapshotRestoreResult restoreResult)
    {
        var restoredTargets = new List<string>();

        if (restoreResult.RestoredEntries.Any(static entry => string.Equals(entry, "app/.env", StringComparison.Ordinal)))
        {
            restoredTargets.Add(".env");
        }

        if (restoreResult.RestoredEntries.Any(static entry => entry.StartsWith("app/data/", StringComparison.Ordinal)))
        {
            restoredTargets.Add("data/");
        }

        if (restoreResult.RestoredEntries.Any(static entry => string.Equals(entry, "desktop/activity-state.json", StringComparison.Ordinal)))
        {
            restoredTargets.Add("desktop activity state");
        }

        return restoredTargets.Count > 0
            ? $"Restored targets: {string.Join(", ", restoredTargets)}"
            : "Restored targets: tracked state files";
    }

    private static string BuildRestoreSessionSummaryText(LocalStateSnapshotPreviewResult? preview)
    {
        var sessionCountLine = preview?.Lines.FirstOrDefault(
            static line => line.StartsWith("sessions.json conversations:", StringComparison.Ordinal));

        return string.IsNullOrWhiteSpace(sessionCountLine)
            ? "Session summary: not available"
            : $"Session summary: {sessionCountLine}";
    }

    private static string BuildRestoreLatestActivitySummaryText(LocalStateSnapshotPreviewResult? preview)
    {
        var latestActivityLine = preview?.Lines.FirstOrDefault(
            static line => line.StartsWith("sessions.json latest activity:", StringComparison.Ordinal));

        return string.IsNullOrWhiteSpace(latestActivityLine)
            ? "Latest activity summary: not available"
            : $"Latest activity summary: {latestActivityLine}";
    }

    private static string BuildRestoreAdviceText(LocalStateSnapshotPreviewResult? preview)
    {
        if (preview?.Recommendations is not { Count: > 0 })
        {
            return "Post-restore advice: review the restored state and confirm it matches what you expected.";
        }

        return $"Post-restore advice: {string.Join(" ", preview.Recommendations)}";
    }

    private string BuildRestoreControlPlaneText()
    {
        var lastFailure = _backendControlApiService.LastFailure;

        if (lastFailure.Kind == BackendControlApiFailureKind.Unauthorized)
        {
            return DidLastRestoreChangeControlApiToken()
                ? "Control plane check: the restored snapshot rolled QQ_AI_BOT_CONTROL_API_TOKEN to a different value, so this window must save the same token locally before it can control the runtime again."
                : "Control plane check: the backend rejected the local desktop token. Save the matching QQ_AI_BOT_CONTROL_API_TOKEN in this window before reloading.";
        }

        if (!IsControlApiReachable)
        {
            return StartCommand.CanExecute(null)
                ? "Control plane check: the backend host is stopped, so the local control API is offline until you start it again."
                : "Control plane check: desktop is still waiting for the local control API to come back. Runtime state may be stale until it reconnects.";
        }

        return "Control plane check: desktop is attached to the local control API and can apply follow-up actions now.";
    }

    private string BuildRestoreRuntimeText()
    {
        var lastFailure = _backendControlApiService.LastFailure;

        if (lastFailure.Kind == BackendControlApiFailureKind.Unauthorized)
        {
            return "Runtime check: the backend may still be running, but this window cannot verify QQ or WeChat readiness until the local control token matches again.";
        }

        if (!IsControlApiReachable)
        {
            return StartCommand.CanExecute(null)
                ? "Runtime check: the backend is stopped, so restored QQ and WeChat settings are not active yet."
                : "Runtime check: QQ and WeChat status cannot be verified yet because the desktop is not attached to live runtime state.";
        }

        if (StartCommand.CanExecute(null))
        {
            return DidLastRestoreOverwriteEnv()
                ? "Runtime check: the backend is stopped, so restored channel settings and session state are waiting to be applied."
                : "Runtime check: the backend is stopped, so QQ and WeChat are not active yet.";
        }

        if (!IsQqRuntimeReady())
        {
            return DidLastRestoreChangeNapCatSettings()
                ? "Runtime check: QQ is still waiting for NapCat, and this snapshot changed NapCat settings. Confirm the saved URL and token still match the live NapCat service."
                : "Runtime check: QQ is still waiting for NapCat. Confirm the service is online and the saved URL or token is correct.";
        }

        if (IsWechatConfiguredAfterRestore() && !IsWechatRuntimeReady())
        {
            return DidLastRestoreChangeWechatSettings()
                ? "Runtime check: QQ is ready. WeChat is still waiting for the restored bridge settings to match a live bridge service."
                : "Runtime check: QQ is ready. WeChat is configured, but the bridge is still not ready.";
        }

        return DidLastRestoreRestoreSessionStore()
            ? "Runtime check: QQ and WeChat status look healthy, and the restored session store should now be active."
            : "Runtime check: QQ and WeChat status look healthy after restore.";
    }

    private string BuildRestoreNextStepText(IReadOnlyList<RestoreResultAction> actions)
    {
        var primaryAction = actions.FirstOrDefault();

        if (primaryAction is null)
        {
            return "Next step: review the current state and recent activity.";
        }

        return primaryAction.Key switch
        {
            DesktopHealthActionKeys.FocusControlApiToken => DidLastRestoreChangeControlApiToken()
                ? "Next step: update the local control token in this window so it matches the restored backend .env, then reload config."
                : "Next step: open local control settings, confirm QQ_AI_BOT_CONTROL_API_TOKEN matches the backend, then reload config.",
            DesktopHealthActionKeys.StartBackend => DidLastRestoreOverwriteEnv() || DidLastRestoreRestoreSessionStore()
                ? "Next step: start the backend so the restored config and session state become live again."
                : "Next step: start the backend from this window.",
            DesktopHealthActionKeys.FocusNapCatUrl => DidLastRestoreChangeNapCatSettings()
                ? "Next step: review the restored NapCat URL and token, then confirm the live NapCat service still matches them."
                : "Next step: check NapCat connectivity and reload config if you change the saved settings.",
            DesktopHealthActionKeys.FocusWechatUrl => DidLastRestoreChangeWechatSettings()
                ? "Next step: review the restored WeChat bridge settings and confirm the bridge service is online."
                : "Next step: check the WeChat bridge service and reload config if you change the saved settings.",
            DesktopHealthActionKeys.ReloadConfig => !IsControlApiReachable
                ? "Next step: reload config after the local control API comes back so this window can refresh live state."
                : "Next step: reload config to verify the restored state.",
            _ => $"Next step: {primaryAction.Label}"
        };
    }

    private void ResetRestoreResultState()
    {
        _lastStateRestoreResult = null;
        _lastStateRestorePreview = null;
        LastStateRestoreText = "No state snapshot restored yet";
        LastStateRestoreSummaryText = "Restore result summary will appear here.";
        LastStateRestoreIssueText = "Post-restore issue summary will appear here.";
        LastStateRestoreTargetsText = "Restore targets will appear here.";
        LastStateRestoreSessionsText = "Session summary will appear here.";
        LastStateRestoreLatestActivityText = "Latest activity summary will appear here.";
        LastStateRestoreAdviceText = "Post-restore advice will appear here.";
        LastStateRestoreControlPlaneText = "Post-restore control plane check will appear here.";
        LastStateRestoreRuntimeText = "Post-restore runtime check will appear here.";
        LastStateRestoreNextStepText = "Post-restore next step will appear here.";
        LastStateRestorePrimaryActionLabel = string.Empty;
        LastStateRestorePrimaryActionKey = string.Empty;
        LastStateRestoreSecondaryActionLabel = string.Empty;
        LastStateRestoreSecondaryActionKey = string.Empty;
        LastStateRestoreTertiaryActionLabel = string.Empty;
        LastStateRestoreTertiaryActionKey = string.Empty;
    }

    private bool DidLastRestoreOverwriteEnv() =>
        _lastStateRestoreResult?.RestoredEntries.Any(static entry => string.Equals(entry, "app/.env", StringComparison.Ordinal)) == true;

    private bool DidLastRestoreRestoreSessionStore() =>
        _lastStateRestoreResult?.RestoredEntries.Any(static entry => entry.StartsWith("app/data/", StringComparison.Ordinal)) == true;

    private bool DidLastRestoreChangeControlApiToken() =>
        GetLastRestoreChangedTrackedEnvKeys().Contains(ControlApiTokenEnvKey);

    private bool DidLastRestoreChangeNapCatSettings() =>
        GetLastRestoreChangedTrackedEnvKeys().Any(static key => key.StartsWith("NAPCAT_", StringComparison.OrdinalIgnoreCase));

    private bool DidLastRestoreChangeWechatSettings() =>
        GetLastRestoreChangedTrackedEnvKeys().Any(static key => key.StartsWith("WECHAT_", StringComparison.OrdinalIgnoreCase));

    private string[] GetLastRestoreChangedTrackedEnvKeys()
    {
        var trackedKeysLine = _lastStateRestorePreview?.Lines.FirstOrDefault(
            static line => line.StartsWith(".env tracked keys changed:", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(trackedKeysLine))
        {
            return [];
        }

        return trackedKeysLine[".env tracked keys changed:".Length..]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private bool IsQqRuntimeReady() => _runtimeSnapshot.RuntimeReady == true;

    private bool IsWechatConfiguredAfterRestore() =>
        _runtimeSnapshot.WechatConfigured == true || !string.IsNullOrWhiteSpace(WechatBridgeUrl);

    private bool IsWechatRuntimeReady() =>
        !IsWechatConfiguredAfterRestore() || _runtimeSnapshot.WechatRuntimeReady == true;

    private static string ExtractSnapshotSide(string line)
    {
        const string marker = "-> snapshot ";
        var markerIndex = line.IndexOf(marker, StringComparison.Ordinal);

        if (markerIndex < 0)
        {
            return line;
        }

        return line[(markerIndex + marker.Length)..].Trim();
    }

    private static string BuildDeleteConfirmationMessage(LocalStateSnapshotDescriptor snapshot)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"Delete snapshot: {snapshot.FileName}",
                snapshot.Summary,
                "This permanently removes the selected archive from the local snapshot folder.",
                "Continue?"
            ]);
    }

    private sealed record RestoreResultAction(string Label, string Key);

    private void ClearActivityHistory(
        ObservableCollection<BackendRecentActivityItem> recentActivityItems,
        Action clearSelection,
        Action clearPinnedState)
    {
        recentActivityItems.Clear();
        clearSelection();
        clearPinnedState();
        OnPropertyChanged(nameof(OverallReadinessRecentActivityText));
        OnPropertyChanged(nameof(OverallReadinessActionLabel));
        OnPropertyChanged(nameof(OverallReadinessActionKey));
        UpdateCommandStates();
        NotifyRuntimeSnapshotChanged();
    }

    private void OnLogFlushTimerTick(object? sender, EventArgs e)
    {
        if (!_logDirty)
        {
            return;
        }

        string newLogText;

        lock (_logSyncRoot)
        {
            if (!_logDirty)
            {
                return;
            }

            newLogText = string.Join(Environment.NewLine, _logLines);
            _logDirty = false;
        }

        LogText = newLogText;
    }

    private void SetTrackedProperty(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName) && !_suspendDirtyTracking)
        {
            HasUnsavedChanges = true;
        }

        RefreshHealthReport();
    }

    private void SetTrackedBooleanStringProperty(
        ref string field,
        string value,
        string booleanPropertyName,
        [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            OnPropertyChanged(booleanPropertyName);

            if (!_suspendDirtyTracking)
            {
                HasUnsavedChanges = true;
            }

            RefreshHealthReport();
        }
    }

    private static bool ParseBooleanFlag(string value)
    {
        var normalizedValue = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalizedValue is "1" or "true" or "yes" or "on";
    }

    private void UpdateCommandStates()
    {
        _reloadCommand.RaiseCanExecuteChanged();
        _saveCommand.RaiseCanExecuteChanged();
        _saveLocalControlPlaneCommand.RaiseCanExecuteChanged();
        _startCommand.RaiseCanExecuteChanged();
        _stopCommand.RaiseCanExecuteChanged();
        _toggleAutoStartCommand.RaiseCanExecuteChanged();
        _autoDetectCommand.RaiseCanExecuteChanged();
        _openBackendFolderCommand.RaiseCanExecuteChanged();
        _applyBaseUrlPresetCommand.RaiseCanExecuteChanged();
        _clearLogsCommand.RaiseCanExecuteChanged();
        _runHealthActionCommand.RaiseCanExecuteChanged();
        _openSessionStoreFolderCommand.RaiseCanExecuteChanged();
        _openImageCacheFolderCommand.RaiseCanExecuteChanged();
        _clearImageCacheCommand.RaiseCanExecuteChanged();
        _exportStateSnapshotCommand.RaiseCanExecuteChanged();
        _exportSafeStateSnapshotCommand.RaiseCanExecuteChanged();
        _restoreLatestStateSnapshotCommand.RaiseCanExecuteChanged();
        _refreshStateSnapshotsCommand.RaiseCanExecuteChanged();
        _restoreSelectedStateSnapshotCommand.RaiseCanExecuteChanged();
        _deleteSelectedStateSnapshotCommand.RaiseCanExecuteChanged();
        _openStateSnapshotFolderCommand.RaiseCanExecuteChanged();
        _clearQqActivityHistoryCommand.RaiseCanExecuteChanged();
        _clearWechatActivityHistoryCommand.RaiseCanExecuteChanged();
    }

    private void RunOnUiDispatcher(Action action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        if (_disposed)
        {
            return;
        }

        if (_uiDispatcher.HasShutdownStarted || _uiDispatcher.HasShutdownFinished)
        {
            return;
        }

        if (_uiDispatcher.CheckAccess())
        {
            action();
            return;
        }

        _uiDispatcher.BeginInvoke(action, priority);
    }

    private string ResolveLocalExtraValue(string key, string fallback)
    {
        return _envDocument.ExtraValues.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : fallback;
    }
}
