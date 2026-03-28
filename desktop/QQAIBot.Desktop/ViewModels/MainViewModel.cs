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
            "你是本地 AI 助手，会处理来自 QQ 和微信的消息。",
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
    private readonly DesktopControlPlaneSession _controlPlaneSession;
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
    private readonly AsyncRelayCommand _exportSafeRollbackSnapshotCommand;
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
    private string _statusText = "等待加载";
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
    private string _lastLoadedAtText = "未加载";
    private string _lastSavedAtText = "未保存";
    private string _logText = string.Empty;
    private bool _logDirty;
    private string _controlApiToken = string.Empty;
    private string _lastStateSnapshotText = "尚未导出状态快照";
    private string _lastStateRestoreText = "尚未恢复状态快照";
    private string _lastStateRestoreSummaryText = "恢复结果摘要会显示在这里。";
    private string _lastStateRestoreIssueText = "恢复后的问题摘要会显示在这里。";
    private string _lastStateRestoreTargetsText = "恢复目标会显示在这里。";
    private string _lastStateRestoreSessionsText = "会话摘要会显示在这里。";
    private string _lastStateRestoreLatestActivityText = "最近活动摘要会显示在这里。";
    private string _lastStateRestoreAdviceText = "恢复后的建议会显示在这里。";
    private string _lastStateRestoreControlPlaneText = "恢复后的控制面检查会显示在这里。";
    private string _lastStateRestoreRuntimeText = "恢复后的运行时检查会显示在这里。";
    private string _lastStateRestoreNextStepText = "恢复后的下一步建议会显示在这里。";
    private string _lastStateRestorePrimaryActionLabel = string.Empty;
    private string _lastStateRestorePrimaryActionKey = string.Empty;
    private string _lastStateRestoreSecondaryActionLabel = string.Empty;
    private string _lastStateRestoreSecondaryActionKey = string.Empty;
    private string _lastStateRestoreTertiaryActionLabel = string.Empty;
    private string _lastStateRestoreTertiaryActionKey = string.Empty;
    private string _selectedStateSnapshotImpactText = "恢复会覆盖这个快照中展示的状态。删除会永久移除当前选中的归档。";
    private string _selectedStateSnapshotDiffText = "选择一个快照以预览差异。";
    private string _selectedStateSnapshotAdviceText = "恢复建议会显示在这里。";
    private string _selectedStateSnapshotSafetyHeadlineText = "恢复前检查：先选择一个快照，看看会覆盖哪些当前状态。";
    private string _selectedStateSnapshotSafetyRecommendationText = "建议的第一步：先选中快照查看覆盖风险，再决定是否恢复。";
    private string _selectedStateSnapshotRollbackHintText = "安全回滚快照会保留当前状态的回退点，并且不复制 .env 密钥。";
    private LocalStateSnapshotDescriptor? _selectedStateSnapshot;
    private LocalStateSnapshotPreviewResult? _selectedStateSnapshotPreview;
    private LocalStateSnapshotRestoreResult? _lastStateRestoreResult;
    private LocalStateSnapshotPreviewResult? _lastStateRestorePreview;
    private BackendRuntimeSnapshotViewState _runtimeSnapshot = new();
    private DesktopLatestTurnOverview _latestTurnOverview = new();
    private DesktopHealthReport _healthReport = new();
    private DesktopGuideFlow _guideFlow = new();
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
        _controlPlaneSession = new DesktopControlPlaneSession(
            new DesktopSessionDependencies
            {
                LocalConfigFallbackReader = _localConfigFallbackReader,
                LocalBootstrapConfigStore = _localBootstrapConfigStore,
                LocalPathOperationsService = _localPathOperationsService,
                LocalStateSnapshotService = _localStateSnapshotService,
                BackendControlApiService = _backendControlApiService,
                BotProcessService = _botProcessService,
                ActivityStateStore = _activityStateStore,
                ActivityStatePolicy = _activityStatePolicy
            },
            BuildInitialShellState());
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
        _exportSafeRollbackSnapshotCommand = new AsyncRelayCommand(
            ExportSafeRollbackSnapshotAsync,
            () => IsBackendRootValid && SelectedStateSnapshot is not null);
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
        RefreshLatestTurnOverview();
        RefreshHealthReport();
        AddLog("Desktop 控制台已初始化。");
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
    public ICommand ExportSafeRollbackSnapshotCommand => _exportSafeRollbackSnapshotCommand;
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
                LastStateSnapshotText = "尚未导出状态快照";
                ResetRestoreResultState();
                ReplaceStateSnapshots([]);
                SelectedStateSnapshot = null;
                ApplyState(_controlPlaneSession.UpdateBackendRoot(value, _backendRootDetected));
                OnPropertyChanged(nameof(EnvFilePath));
                OnPropertyChanged(nameof(IsBackendRootValid));
                OnPropertyChanged(nameof(BackendRootStateText));
                UpdateCommandStates();
            }
        }
    }

    public string EnvFilePath => Path.Combine(BackendRootPath, ".env");

    public bool IsBackendRootValid => PathDiscoveryService.IsBackendRoot(BackendRootPath);

    public string BackendRootStateText => IsBackendRootValid
        ? (_backendRootDetected ? "已自动检测到 backend 根目录" : "backend 根目录有效")
        : "backend 根目录无效";

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
                    SyncSessionEditorState();
                }

                OnPropertyChanged(nameof(EffectiveBotInstructionsText));
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
                    SyncSessionEditorState();
                }

                OnPropertyChanged(nameof(EffectiveBotInstructionsText));
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
                SyncSessionEditorState();
                UpdateCommandStates();
            }
        }
    }

    public string ProcessStateText => IsProcessRunning ? "运行中" : "已停止";

    public string WechatRuntimeStateText =>
        _runtimeSnapshot.WechatConfigured != true
            ? "微信已关闭"
            : _runtimeSnapshot.WechatRuntimeActive == true
                ? "微信 runtime 运行中"
                : "微信 runtime 已停止";

    public string RuntimeReadyText =>
        _runtimeSnapshot.RuntimeReady == true ? "QQ 通道已就绪" : "QQ 通道未就绪";

    public string WechatRuntimeReadyText =>
        _runtimeSnapshot.WechatConfigured != true
            ? "微信通道已关闭"
            : _runtimeSnapshot.WechatRuntimeReady == true
                ? "微信通道已就绪"
                : "微信通道未就绪";

    public string WechatBridgeStateText =>
        _runtimeSnapshot.WechatBridgeConnected == true ? "微信桥接已连接" : "微信桥接未连接";

    public string WechatWorkerProcessText =>
        _runtimeSnapshot.WechatWorkerProcessId is int workerPid ? $"微信 worker PID {workerPid}" : "微信 worker 未运行";

    public string HealthStateText => _healthReport.StateText;

    public string HealthSummaryText => _healthReport.Summary;

    public string HealthChecklistStatusText => _healthReport.ChecklistStatus;

    public string HealthReadyNowText => _healthReport.ReadyNowText;

    public string HealthPrimaryActionText => _healthReport.PrimaryAction;

    public string HealthPrimaryActionLabel => _healthReport.PrimaryActionLabel;

    public string HealthPrimaryActionKey => _healthReport.PrimaryActionKey;

    public string HealthActionSummaryText => _healthReport.ActionSummary;

    public IReadOnlyList<DesktopNextActionItem> HealthNextActions => _healthReport.NextActions;

    public bool HasHealthNextActions => HealthNextActions.Count > 0;

    public string HealthRuntimeExplanationText => _healthReport.RuntimeExplanation;

    public string HealthLatestIssueText => _healthReport.LatestIssue;

    public string HealthLatestIssueActionLabel => _healthReport.LatestIssueActionLabel;

    public string HealthLatestIssueActionKey => _healthReport.LatestIssueActionKey;

    public DesktopHealthState LatestTurnState => _latestTurnOverview.State;

    public string LatestTurnHeadlineText => _latestTurnOverview.Headline;

    public string LatestTurnSummaryText => _latestTurnOverview.Summary;

    public string LatestTurnCapabilitiesText => _latestTurnOverview.Capabilities;

    public string LatestTurnReasonText => _latestTurnOverview.Reason;

    public string LatestTurnOutcomeText => _latestTurnOverview.Outcome;

    public string LatestTurnActionLabel => _latestTurnOverview.ActionLabel;

    public string LatestTurnActionKey => _latestTurnOverview.ActionKey;

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
        _selectedQqRecentActivity?.Summary ?? "请选择一条 QQ 活动记录";

    public string SelectedQqRecentActivityMetaText =>
        _selectedQqRecentActivity?.Meta ?? "当前未选择活动";

    public string SelectedQqRecentActivityDetailText =>
        _selectedQqRecentActivity?.Detail ?? "请选择一条 QQ 活动记录";

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
        BackendActivityProjectionFormatter.FormatRequestSummary(_runtimeSnapshot.LastWechatLlmRequest, "No WeChat requests captured yet");

    public string LatestWechatLlmDetailText =>
        BackendLlmProjectionFormatter.FormatRequestDetail(_runtimeSnapshot.LastWechatLlmRequest);

    public string LatestWechatActivitySummaryText =>
        BackendActivityProjectionFormatter.FormatActivitySummary(_runtimeSnapshot.LastWechatLlmRequest, _runtimeSnapshot.LastWechatLlmFailure, "No WeChat activity captured yet");

    public string LatestWechatRecentActivityText =>
        BackendActivityProjectionFormatter.FormatRecentActivity(WechatRecentActivities, "No recent WeChat activity yet");

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
        _selectedWechatRecentActivity?.Summary ?? "请选择一条 WeChat 活动记录";

    public string SelectedWechatRecentActivityMetaText =>
        _selectedWechatRecentActivity?.Meta ?? "当前未选择活动";

    public string SelectedWechatRecentActivityDetailText =>
        _selectedWechatRecentActivity?.Detail ?? "请选择一条 WeChat 活动记录";

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
        BackendActivityProjectionFormatter.FormatFailureSummary(_runtimeSnapshot.LastWechatLlmFailure, "No WeChat failures captured yet");

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
                SyncSessionEditorState();
            }
        }
    }

    public string AutoStartStateText => AutoStartEnabled ? "随 Windows 启动" : "手动启动";

    public string AutoStartButtonText => AutoStartEnabled ? "关闭开机启动" : "启用开机启动";

    public string ResidentModeDetailText => AutoStartEnabled
        ? "常驻模式：会在 Windows 登录时启动，以最小化方式恢复，并保留托盘入口便于快速控制。关闭窗口仍只会收进托盘；如果你要让 runtime 真正离线，请使用“停止后端”。"
        : "常驻模式：当前仍需手动启动。最小化或关闭窗口仍会收进托盘，“退出控制台”只会关闭桌面壳；如果你希望登录后自动恢复，请启用开机启动。";

    public string ShellRuntimeBoundaryText => IsProcessRunning
        ? "收进托盘：隐藏这个控制台窗口，但 backend 会继续运行。" + Environment.NewLine +
          "退出控制台：只关闭桌面壳和托盘图标，runtime 会继续运行，直到你主动停止。" + Environment.NewLine +
          "停止后端：停止本地 runtime，让 QQ / WeChat 下线。"
        : "收进托盘：隐藏这个控制台窗口，并保留托盘入口。" + Environment.NewLine +
          "退出控制台：只关闭桌面壳，你之后可以重新打开，不会改动本地状态。" + Environment.NewLine +
          "停止后端：当前 runtime 已离线，所以不会产生额外效果。";

    public string CloseToTrayBehaviorText => IsProcessRunning
        ? "隐藏这个控制台窗口，但 backend 会继续运行。"
        : "隐藏这个控制台窗口，并保留托盘入口。";

    public string ExitDesktopBehaviorText => IsProcessRunning
        ? "只关闭桌面壳和托盘图标。runtime 会继续运行，直到你主动停止。"
        : "只关闭桌面壳。之后可以重新打开，不会改动本地状态。";

    public string StopBackendBehaviorText => IsProcessRunning
        ? "停止本地 runtime，让 QQ / 微信下线。"
        : "当前 runtime 已离线，所以不会产生额外效果。";

    public string ReopenDesktopBehaviorText => IsProcessRunning
        ? "之后可以从桌面快捷方式或开始菜单重新打开 Local AI Runtime。它会恢复这个控制台窗口，并重新附着到同一个正在运行的 runtime，而不是再启动一个新的桌面壳。"
        : "之后可以从桌面快捷方式或开始菜单重新打开 Local AI Runtime。它会恢复这个控制台窗口，而不是再启动一个新的桌面壳；如果 runtime 当前离线，再手动启动即可。";

    public bool IsOverallReadinessReady => _guideFlow.IsOverallReadinessReady;

    public bool IsOverallReadinessSetupComplete => _guideFlow.IsOverallReadinessSetupComplete;

    public string OverallReadinessStateText => _guideFlow.OverallReadinessStateText;

    public string OverallReadinessSummaryText => _guideFlow.OverallReadinessSummaryText;

    public string OverallReadinessRecentActivityText => _guideFlow.OverallReadinessRecentActivityText;

    public string OverallReadinessActionLabel => _guideFlow.OverallReadinessActionLabel;

    public string OverallReadinessActionKey => _guideFlow.OverallReadinessActionKey;


    public string FirstRunGuideText => _guideFlow.FirstRunGuideText;

    public IReadOnlyList<DesktopGuideStepItem> FirstRunGuideSteps => _guideFlow.FirstRunGuideSteps;

    public string FirstRunGuideProgressText => _guideFlow.FirstRunGuideProgressText;

    public string FirstRunGuideCurrentStepText => _guideFlow.FirstRunGuideCurrentStepText;

    public bool IsFirstRunGuideComplete => _guideFlow.IsFirstRunGuideComplete;

    public string FirstRunGuideCompletionText => _guideFlow.FirstRunGuideCompletionText;

    public string FirstRunStepsText => _guideFlow.FirstRunStepsText;

    public string DailyUseGuideText => _guideFlow.DailyUseGuideText;

    public IReadOnlyList<DesktopGuideStepItem> DailyUseGuideSteps => _guideFlow.DailyUseGuideSteps;

    public string DailyUseGuideProgressText => _guideFlow.DailyUseGuideProgressText;

    public string DailyUseGuideCurrentStepText => _guideFlow.DailyUseGuideCurrentStepText;

    public bool IsDailyUseGuideComplete => _guideFlow.IsDailyUseGuideComplete;

    public string DailyUseGuideCompletionText => _guideFlow.DailyUseGuideCompletionText;

    public string DailyUseStepsText => _guideFlow.DailyUseStepsText;

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (SetProperty(ref _hasUnsavedChanges, value))
            {
                OnPropertyChanged(nameof(ConfigStateText));
                SyncSessionEditorState();
            }
        }
    }

    public string ConfigStateText => HasUnsavedChanges ? "有未保存修改" : "配置已同步";

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
            ? "本机令牌未设置"
            : _backendControlApiService.LastFailure.Kind == BackendControlApiFailureKind.Unauthorized
                ? "本机令牌已保存，但后端仍然拒绝它"
                : "本机令牌已配置";

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
            "尚未恢复状态快照",
            StringComparison.Ordinal);

    public LocalStateSnapshotDescriptor? SelectedStateSnapshot
    {
        get => _selectedStateSnapshot;
        set
        {
            if (SetProperty(ref _selectedStateSnapshot, value))
            {
                _selectedStateSnapshotPreview = null;
                OnPropertyChanged(nameof(SelectedStateSnapshotSummaryText));
                OnPropertyChanged(nameof(SelectedStateSnapshotDetailText));
                ApplySelectedStateSnapshotPresentation();
                _ = RefreshSelectedStateSnapshotPreviewAsync();
                UpdateCommandStates();
            }
        }
    }

    public string SelectedStateSnapshotSummaryText =>
        SelectedStateSnapshot?.Summary ?? "选择一个快照以查看或恢复。";

    public string SelectedStateSnapshotDetailText =>
        SelectedStateSnapshot?.Detail ?? "快照详情会显示在这里。";

    public string SelectedStateSnapshotImpactText
    {
        get => _selectedStateSnapshotImpactText;
        private set => SetProperty(ref _selectedStateSnapshotImpactText, value);
    }

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

    public string SelectedStateSnapshotSafetyHeadlineText
    {
        get => _selectedStateSnapshotSafetyHeadlineText;
        private set => SetProperty(ref _selectedStateSnapshotSafetyHeadlineText, value);
    }

    public string SelectedStateSnapshotSafetyRecommendationText
    {
        get => _selectedStateSnapshotSafetyRecommendationText;
        private set => SetProperty(ref _selectedStateSnapshotSafetyRecommendationText, value);
    }

    public string SelectedStateSnapshotRollbackHintText
    {
        get => _selectedStateSnapshotRollbackHintText;
        private set => SetProperty(ref _selectedStateSnapshotRollbackHintText, value);
    }

    public string SnapshotRetentionHintText =>
        "快照会一直保留，直到你手动删除。较早的快照仍可能包含 .env 密钥。";

    public string SessionStoreStateText =>
        !IsBackendRootValid
            ? "后端目录有效后才能显示会话路径"
            : File.Exists(SessionStorePathText)
                ? "会话历史文件已存在"
                : "首次保存会话后会创建历史文件";

    public string ImageCacheStateText
    {
        get
        {
            if (!IsBackendRootValid)
            {
                return "后端目录有效后才能显示缓存路径";
            }

            if (!Directory.Exists(ImageCachePathText))
            {
                return "图片缓存为空";
            }

            var cachedFileCount = Directory.GetFiles(ImageCachePathText, "*", SearchOption.AllDirectories).Length;
            return cachedFileCount == 0
                ? "图片缓存为空"
                : $"{cachedFileCount} 个缓存图片文件";
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
        if (!_suspendDirtyTracking)
        {
            SyncSessionEditorState();
        }
        ApplyCommandResult(await _controlPlaneSession.LoadConfigAsync());
        return;

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
            StatusText = "正在加载配置...";
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
                    ? "配置已加载（需要重启）"
                    : "配置已加载",
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
                envPath: EnvFilePath,
                canStartBackend: StartCommand.CanExecute(null));
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
        return await _controlPlaneSession.LoadAuthoritativeConfigAsync();

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
        SyncSessionEditorState();
        ApplyCommandResult(await _controlPlaneSession.SaveLocalControlPlaneAsync(ControlApiToken));
        await LoadConfigAsync();
        return;

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
                localControlSettingsOperation: true,
                canStartBackend: StartCommand.CanExecute(null));
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
        SyncSessionEditorState();
        var sessionSaveResult = await _controlPlaneSession.SaveConfigAsync(BuildConfig(), showUiErrors);
        ApplyCommandResult(sessionSaveResult, showUiErrors);
        return sessionSaveResult.Succeeded;

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
            StatusText = "正在保存配置...";
            var submittedConfig = BuildConfig();
            _envDocument.Config = submittedConfig;
            var apiResult = await SaveConfigThroughControlApiAsync(submittedConfig);
            _envDocument.Config = MergeSavedConfig(submittedConfig, apiResult);
            ApplyConfigToView(_envDocument.Config);
            DesktopControlPlaneFeedback.ApplyOutcome(
                statusText: apiResult.RestartRequired ? "配置已保存（需要重启）" : "配置已保存",
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
                envPath: EnvFilePath,
                canStartBackend: StartCommand.CanExecute(null));
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
        SyncSessionEditorState();
        ApplyCommandResult(await _controlPlaneSession.StartBackendAsync(BuildConfig()));
        return;

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
                envPath: EnvFilePath,
                canStartBackend: StartCommand.CanExecute(null));
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
        SyncSessionEditorState();
        ApplyCommandResult(await _controlPlaneSession.StopBackendAsync());
        return;

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
                envPath: EnvFilePath,
                canStartBackend: StartCommand.CanExecute(null));
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
            StatusText = nextValue ? "已启用常驻模式开机启动" : "已关闭开机启动";
            AddLog(nextValue
                ? "已启用常驻模式开机启动。"
                : "已关闭开机启动。手动启动后，托盘行为仍然可用。");
            NotificationRequested?.Invoke(
                this,
                new TrayNotification
                {
                    Title = "Local AI Runtime",
                    Message = nextValue
                        ? "已启用常驻模式。Windows 登录后会以最小化方式启动并确保 runtime 运行。"
                        : "已关闭常驻模式开机启动。手动启动后仍可使用托盘模式。",
                    Icon = Forms.ToolTipIcon.Info
                });
        }
        catch (Exception ex)
        {
            StatusText = "更新开机启动失败";
            AddLog($"更新开机启动失败: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"更新开机启动失败：\n{ex.Message}",
                "更新开机启动失败",
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
        StatusText = _backendRootDetected ? "已检测到 backend 根目录" : "未检测到 backend 根目录";
        AddLog(_backendRootDetected
            ? $"自动检测到 backend 根目录: {BackendRootPath}"
            : $"继续使用当前 backend 根目录: {BackendRootPath}");
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
            StatusText = removedEntries > 0 ? "图片缓存已清理" : "图片缓存本来就是空的";
            AddLog(
                removedEntries > 0
                    ? $"已清理图片缓存 {ImageCachePathText}，移除了 {removedEntries} 个条目。"
                    : $"图片缓存本来就是空的: {ImageCachePathText}");
            OnPropertyChanged(nameof(ImageCacheStateText));
        }
        catch (Exception ex)
        {
            StatusText = "清空图片缓存失败";
            AddLog($"清空图片缓存失败：{ex.Message}");
            System.Windows.MessageBox.Show(
                $"清空图片缓存失败：\n{ex.Message}",
                "清空图片缓存失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ExportStateSnapshotAsync()
    {
        if (!IsBackendRootValid)
        {
            StatusText = "后端目录无效";
            AddLog("后端目录无效，无法导出状态快照。");
            return;
        }

        try
        {
            StatusText = "正在导出状态快照...";
            var result = await _localStateSnapshotService.ExportAsync(BackendRootPath);
            LastStateSnapshotText = result.ArchivePath;
            await RefreshStateSnapshotsAsync(result.ArchivePath);
            StatusText = "状态快照已导出";
            AddLog($"已导出本地状态快照到 {result.ArchivePath}，包含 {result.IncludedEntries.Count} 项。");
            OnPropertyChanged(nameof(StateSnapshotFolderPathText));
        }
        catch (Exception ex)
        {
            StatusText = "导出状态快照失败";
            AddLog($"导出状态快照失败：{ex.Message}");
            System.Windows.MessageBox.Show(
                $"导出状态快照失败：\n{ex.Message}",
                "导出状态快照失败",
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
            StatusText = "后端目录无效";
            AddLog("后端目录无效，无法导出安全状态快照。");
            return;
        }

        try
        {
            StatusText = "正在导出安全状态快照...";
            var result = await _localStateSnapshotService.ExportSafeAsync(BackendRootPath);
            LastStateSnapshotText = result.ArchivePath;
            await RefreshStateSnapshotsAsync(result.ArchivePath);
            StatusText = "安全状态快照已导出";
            AddLog($"已导出安全状态快照到 {result.ArchivePath}，包含 {result.IncludedEntries.Count} 项，且不含 .env 密钥。");
            OnPropertyChanged(nameof(StateSnapshotFolderPathText));
        }
        catch (Exception ex)
        {
            StatusText = "导出安全状态快照失败";
            AddLog($"导出安全状态快照失败：{ex.Message}");
            System.Windows.MessageBox.Show(
                $"导出安全状态快照失败：\n{ex.Message}",
                "导出安全状态快照失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            UpdateCommandStates();
        }
    }

    private async Task ExportSafeRollbackSnapshotAsync()
    {
        if (!IsBackendRootValid || SelectedStateSnapshot is null)
        {
            return;
        }

        var restoreTargetArchivePath = SelectedStateSnapshot.ArchivePath;
        var restoreTargetFileName = SelectedStateSnapshot.FileName;

        try
        {
            StatusText = "正在导出安全回滚快照...";
            var result = await _localStateSnapshotService.ExportSafeAsync(BackendRootPath);
            LastStateSnapshotText = result.ArchivePath;
            await RefreshStateSnapshotsAsync(restoreTargetArchivePath);
            StatusText = "安全回滚快照已导出";
            AddLog($"已在恢复 {restoreTargetFileName} 前导出安全回滚快照到 {result.ArchivePath}，原始恢复目标保持选中。");
            OnPropertyChanged(nameof(StateSnapshotFolderPathText));
        }
        catch (Exception ex)
        {
            StatusText = "导出安全回滚快照失败";
            AddLog($"导出安全回滚快照失败：{ex.Message}");
            System.Windows.MessageBox.Show(
                $"导出安全回滚快照失败：\n{ex.Message}",
                "导出安全回滚快照失败",
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
            StatusText = "后端目录无效";
            AddLog("后端目录无效，无法恢复状态快照。");
            return;
        }

        try
        {
            var snapshots = await _localStateSnapshotService.ListAsync(BackendRootPath);
            var latestSnapshot = snapshots.FirstOrDefault()
                ?? throw new InvalidOperationException("当前没有可恢复的状态快照。");
            var preview = await _localStateSnapshotService.PreviewAsync(BackendRootPath, latestSnapshot.ArchivePath);

            if (!_confirmationDialogService.Confirm(
                    "恢复最新快照",
                    BuildRestoreConfirmationMessage(latestSnapshot, preview)))
            {
                StatusText = "已取消恢复最新快照";
                AddLog($"已取消恢复最新快照：{latestSnapshot.ArchivePath}");
                return;
            }

            StatusText = "正在恢复最新状态快照...";
            var result = await _localStateSnapshotService.RestoreLatestAsync(BackendRootPath);
            LastStateRestoreText = result.ArchivePath;
            ApplyRestoreResultSummary(result, preview);
            StatusText = "最新状态快照已恢复";
            AddLog($"已从 {result.ArchivePath} 恢复本地状态快照，共恢复 {result.RestoredEntries.Count} 项。");
            await RefreshStateSnapshotsAsync(result.ArchivePath);
            await LoadConfigAsync();
            ApplyRestoreAvailabilityCheck();
        }
        catch (Exception ex)
        {
            StatusText = "恢复状态快照失败";
            AddLog($"恢复最新状态快照失败：{ex.Message}");
            System.Windows.MessageBox.Show(
                $"恢复最新状态快照失败：\n{ex.Message}",
                "恢复状态快照失败",
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
                    "恢复选中快照",
                    BuildRestoreConfirmationMessage(SelectedStateSnapshot, preview)))
            {
                StatusText = "已取消恢复选中快照";
                AddLog($"已取消恢复选中快照：{SelectedStateSnapshot.ArchivePath}");
                return;
            }

            StatusText = "正在恢复选中状态快照...";
            var result = await _localStateSnapshotService.RestoreAsync(
                BackendRootPath,
                SelectedStateSnapshot.ArchivePath);
            LastStateRestoreText = result.ArchivePath;
            ApplyRestoreResultSummary(result, preview);
            StatusText = "选中状态快照已恢复";
            AddLog($"已从 {result.ArchivePath} 恢复选中状态快照，共恢复 {result.RestoredEntries.Count} 项。");
            await RefreshStateSnapshotsAsync(result.ArchivePath);
            await LoadConfigAsync();
            ApplyRestoreAvailabilityCheck();
        }
        catch (Exception ex)
        {
            StatusText = "恢复选中快照失败";
            AddLog($"恢复选中快照失败：{ex.Message}");
            System.Windows.MessageBox.Show(
                $"恢复选中快照失败：\n{ex.Message}",
                "恢复选中快照失败",
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
                    "删除选中快照",
                    BuildDeleteConfirmationMessage(SelectedStateSnapshot)))
            {
                StatusText = "已取消删除快照";
                AddLog($"已取消删除选中快照：{archivePath}");
                return;
            }

            StatusText = "正在删除选中快照...";
            await _localStateSnapshotService.DeleteAsync(archivePath);
            AddLog($"已删除状态快照：{archivePath}");
            await RefreshStateSnapshotsAsync();
            StatusText = "选中快照已删除";

            if (string.Equals(LastStateSnapshotText, archivePath, StringComparison.OrdinalIgnoreCase))
            {
                LastStateSnapshotText = "尚未导出状态快照";
            }

            if (string.Equals(LastStateRestoreText, archivePath, StringComparison.OrdinalIgnoreCase))
            {
                ResetRestoreResultState();
            }
        }
        catch (Exception ex)
        {
            StatusText = "删除快照失败";
            AddLog($"删除选中快照失败：{ex.Message}");
            System.Windows.MessageBox.Show(
                $"删除选中快照失败：\n{ex.Message}",
                "删除选中快照失败",
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
        StatusText = "已打开状态快照目录";
        AddLog($"已打开状态快照目录：{StateSnapshotFolderPathText}");
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
            StatusText = "后端已退出";
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

    private void RefreshLatestTurnOverview()
    {
        _latestTurnOverview = BackendLatestTurnOverviewBuilder.Build(_runtimeSnapshot);
        OnPropertyChanged(nameof(LatestTurnState));
        OnPropertyChanged(nameof(LatestTurnHeadlineText));
        OnPropertyChanged(nameof(LatestTurnSummaryText));
        OnPropertyChanged(nameof(LatestTurnCapabilitiesText));
        OnPropertyChanged(nameof(LatestTurnReasonText));
        OnPropertyChanged(nameof(LatestTurnOutcomeText));
        OnPropertyChanged(nameof(LatestTurnActionLabel));
        OnPropertyChanged(nameof(LatestTurnActionKey));
    }

    private void RefreshGuideFlow()
    {
        _guideFlow = DesktopGuideFlowBuilder.Build(
            new DesktopGuideFlowContext
            {
                IsBackendRootValid = IsBackendRootValid,
                HasUnsavedChanges = HasUnsavedChanges,
                CanStartBackend = StartCommand.CanExecute(null),
                IsProcessRunning = IsProcessRunning,
                IsQqRuntimeReady = _runtimeSnapshot.RuntimeReady == true,
                AutoStartEnabled = AutoStartEnabled,
                HealthLatestIssueText = HealthLatestIssueText,
                HealthLatestIssueActionLabel = HealthLatestIssueActionLabel,
                HealthLatestIssueActionKey = HealthLatestIssueActionKey,
                HealthChecks = _healthReport.Checks,
                QqRecentActivities = QqRecentActivities.ToArray(),
                WechatRecentActivities = WechatRecentActivities.ToArray()
            });
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
    }

    private async void OnStatusPollTimerTick(object? sender, EventArgs e)
    {
        SyncSessionEditorState();
        ApplyCommandResult(await _controlPlaneSession.PollStatusAsync(), showErrorDialog: false);
        return;

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
            RefreshLatestTurnOverview();
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
        OnPropertyChanged(nameof(HealthActionSummaryText));
        OnPropertyChanged(nameof(HealthNextActions));
        OnPropertyChanged(nameof(HasHealthNextActions));
        OnPropertyChanged(nameof(HealthRuntimeExplanationText));
        OnPropertyChanged(nameof(HealthLatestIssueText));
        OnPropertyChanged(nameof(HealthLatestIssueActionLabel));
        OnPropertyChanged(nameof(HealthLatestIssueActionKey));
        RefreshGuideFlow();
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
            ApplySelectedStateSnapshotPresentation();
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
            _selectedStateSnapshotPreview = null;
            ApplySelectedStateSnapshotPresentation();
            return;
        }

        ApplySelectedStateSnapshotPresentation(
            diffTextOverride: "Loading diff preview...",
            adviceTextOverride: "Loading restore advice...");

        try
        {
            var preview = await _localStateSnapshotService.PreviewAsync(
                BackendRootPath,
                selectedSnapshot.ArchivePath);

            if (!string.Equals(SelectedStateSnapshot?.ArchivePath, selectedSnapshot.ArchivePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedStateSnapshotPreview = preview;
            ApplySelectedStateSnapshotPresentation();
        }
        catch (Exception ex)
        {
            if (!string.Equals(SelectedStateSnapshot?.ArchivePath, selectedSnapshot.ArchivePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedStateSnapshotPreview = null;
            ApplySelectedStateSnapshotPresentation(
                diffTextOverride: $"Diff preview unavailable: {ex.Message}",
                adviceTextOverride: "Review the snapshot details carefully before restoring.");
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

    private DesktopShellState BuildInitialShellState()
    {
        return new DesktopShellState
        {
            ConfigEditorState = new DesktopConfigEditorState
            {
                Config = BuildConfig(),
                ControlApiToken = _controlApiToken,
                HasUnsavedChanges = _hasUnsavedChanges,
                LastLoadedAtText = _lastLoadedAtText,
                LastSavedAtText = _lastSavedAtText
            },
            RuntimeShellState = new DesktopRuntimeSnapshotState
            {
                RuntimeSnapshot = _runtimeSnapshot,
                LatestTurnOverview = _latestTurnOverview,
                HealthReport = _healthReport,
                GuideFlow = _guideFlow,
                ControlApiPollState = _controlApiPollState,
                IsProcessRunning = _isProcessRunning,
                AutoStartEnabled = _autoStartEnabled,
                CanStartBackend = CanStartBackend()
            },
            RecentActivityState = new DesktopRecentActivityState
            {
                QqRecentActivities = QqRecentActivities.ToArray(),
                WechatRecentActivities = WechatRecentActivities.ToArray(),
                LastQqRequestEventKey = _lastQqRequestEventKey,
                LastQqFailureEventKey = _lastQqFailureEventKey,
                LastWechatRequestEventKey = _lastWechatRequestEventKey,
                LastWechatFailureEventKey = _lastWechatFailureEventKey,
                PinSelectedQqActivity = _pinSelectedQqActivity,
                PinSelectedWechatActivity = _pinSelectedWechatActivity,
                ShowOnlyQqFailures = _showOnlyQqFailures,
                ShowOnlyWechatFailures = _showOnlyWechatFailures,
                SelectedQqRecentActivity = _selectedQqRecentActivity,
                SelectedWechatRecentActivity = _selectedWechatRecentActivity
            },
            SnapshotState = new DesktopSnapshotState
            {
                StateSnapshots = StateSnapshots.ToArray(),
                SelectedStateSnapshot = _selectedStateSnapshot,
                SelectedStateSnapshotPreview = _selectedStateSnapshotPreview,
                LastStateRestoreResult = _lastStateRestoreResult,
                LastStateRestorePreview = _lastStateRestorePreview,
                LastStateSnapshotText = _lastStateSnapshotText,
                LastStateRestoreText = _lastStateRestoreText,
                LastStateRestoreSummaryText = _lastStateRestoreSummaryText,
                LastStateRestoreIssueText = _lastStateRestoreIssueText,
                LastStateRestoreTargetsText = _lastStateRestoreTargetsText,
                LastStateRestoreSessionsText = _lastStateRestoreSessionsText,
                LastStateRestoreLatestActivityText = _lastStateRestoreLatestActivityText,
                LastStateRestoreAdviceText = _lastStateRestoreAdviceText,
                LastStateRestoreControlPlaneText = _lastStateRestoreControlPlaneText,
                LastStateRestoreRuntimeText = _lastStateRestoreRuntimeText,
                LastStateRestoreNextStepText = _lastStateRestoreNextStepText,
                LastStateRestorePrimaryActionLabel = _lastStateRestorePrimaryActionLabel,
                LastStateRestorePrimaryActionKey = _lastStateRestorePrimaryActionKey,
                LastStateRestoreSecondaryActionLabel = _lastStateRestoreSecondaryActionLabel,
                LastStateRestoreSecondaryActionKey = _lastStateRestoreSecondaryActionKey,
                LastStateRestoreTertiaryActionLabel = _lastStateRestoreTertiaryActionLabel,
                LastStateRestoreTertiaryActionKey = _lastStateRestoreTertiaryActionKey,
                SelectedStateSnapshotImpactText = _selectedStateSnapshotImpactText,
                SelectedStateSnapshotDiffText = _selectedStateSnapshotDiffText,
                SelectedStateSnapshotAdviceText = _selectedStateSnapshotAdviceText,
                SelectedStateSnapshotSafetyHeadlineText = _selectedStateSnapshotSafetyHeadlineText,
                SelectedStateSnapshotSafetyRecommendationText = _selectedStateSnapshotSafetyRecommendationText,
                SelectedStateSnapshotRollbackHintText = _selectedStateSnapshotRollbackHintText
            },
            LocalDocumentState = new DesktopLocalDocumentState
            {
                BackendRootPath = _backendRootPath,
                BackendRootDetected = _backendRootDetected,
                ConfigDocument = new DesktopConfigDocumentState
                {
                    Document = _envDocument
                }
            },
            UiFeedbackState = new DesktopUiFeedbackState
            {
                StatusText = _statusText,
                LogText = _logText
            }
        };
    }

    private void SyncSessionEditorState()
    {
        ApplyState(
            _controlPlaneSession.UpdateEditorState(
                BuildConfig(),
                ControlApiToken,
                HasUnsavedChanges,
                LastLoadedAtText,
                LastSavedAtText,
                AutoStartEnabled,
                CanStartBackend(),
                LogText));
    }

    private void ApplyState(DesktopShellState shellState)
    {
        _suspendDirtyTracking = true;
        try
        {
            _envDocument = shellState.LocalDocumentState.ConfigDocument.Document;
            _backendRootPath = shellState.LocalDocumentState.BackendRootPath;
            _backendRootDetected = shellState.LocalDocumentState.BackendRootDetected;
            _runtimeSnapshot = shellState.RuntimeShellState.RuntimeSnapshot;
            _latestTurnOverview = shellState.RuntimeShellState.LatestTurnOverview;
            _healthReport = shellState.RuntimeShellState.HealthReport;
            _guideFlow = shellState.RuntimeShellState.GuideFlow;
            _controlApiPollState = shellState.RuntimeShellState.ControlApiPollState;
            _autoStartEnabled = shellState.RuntimeShellState.AutoStartEnabled;
            _isProcessRunning = shellState.RuntimeShellState.IsProcessRunning;
            _controlApiRecoveryInProgress = shellState.RuntimeShellState.ControlApiRecoveryInProgress;
            _lastQqRequestEventKey = shellState.RecentActivityState.LastQqRequestEventKey;
            _lastQqFailureEventKey = shellState.RecentActivityState.LastQqFailureEventKey;
            _lastWechatRequestEventKey = shellState.RecentActivityState.LastWechatRequestEventKey;
            _lastWechatFailureEventKey = shellState.RecentActivityState.LastWechatFailureEventKey;
            _pinSelectedQqActivity = shellState.RecentActivityState.PinSelectedQqActivity;
            _pinSelectedWechatActivity = shellState.RecentActivityState.PinSelectedWechatActivity;
            _showOnlyQqFailures = shellState.RecentActivityState.ShowOnlyQqFailures;
            _showOnlyWechatFailures = shellState.RecentActivityState.ShowOnlyWechatFailures;
            _selectedQqRecentActivity = shellState.RecentActivityState.SelectedQqRecentActivity;
            _selectedWechatRecentActivity = shellState.RecentActivityState.SelectedWechatRecentActivity;
            _selectedStateSnapshot = shellState.SnapshotState.SelectedStateSnapshot;
            _selectedStateSnapshotPreview = shellState.SnapshotState.SelectedStateSnapshotPreview;
            _lastStateRestoreResult = shellState.SnapshotState.LastStateRestoreResult;
            _lastStateRestorePreview = shellState.SnapshotState.LastStateRestorePreview;
            _lastStateSnapshotText = shellState.SnapshotState.LastStateSnapshotText;
            _lastStateRestoreText = shellState.SnapshotState.LastStateRestoreText;
            _lastStateRestoreSummaryText = shellState.SnapshotState.LastStateRestoreSummaryText;
            _lastStateRestoreIssueText = shellState.SnapshotState.LastStateRestoreIssueText;
            _lastStateRestoreTargetsText = shellState.SnapshotState.LastStateRestoreTargetsText;
            _lastStateRestoreSessionsText = shellState.SnapshotState.LastStateRestoreSessionsText;
            _lastStateRestoreLatestActivityText = shellState.SnapshotState.LastStateRestoreLatestActivityText;
            _lastStateRestoreAdviceText = shellState.SnapshotState.LastStateRestoreAdviceText;
            _lastStateRestoreControlPlaneText = shellState.SnapshotState.LastStateRestoreControlPlaneText;
            _lastStateRestoreRuntimeText = shellState.SnapshotState.LastStateRestoreRuntimeText;
            _lastStateRestoreNextStepText = shellState.SnapshotState.LastStateRestoreNextStepText;
            _lastStateRestorePrimaryActionLabel = shellState.SnapshotState.LastStateRestorePrimaryActionLabel;
            _lastStateRestorePrimaryActionKey = shellState.SnapshotState.LastStateRestorePrimaryActionKey;
            _lastStateRestoreSecondaryActionLabel = shellState.SnapshotState.LastStateRestoreSecondaryActionLabel;
            _lastStateRestoreSecondaryActionKey = shellState.SnapshotState.LastStateRestoreSecondaryActionKey;
            _lastStateRestoreTertiaryActionLabel = shellState.SnapshotState.LastStateRestoreTertiaryActionLabel;
            _lastStateRestoreTertiaryActionKey = shellState.SnapshotState.LastStateRestoreTertiaryActionKey;
            _selectedStateSnapshotImpactText = shellState.SnapshotState.SelectedStateSnapshotImpactText;
            _selectedStateSnapshotDiffText = shellState.SnapshotState.SelectedStateSnapshotDiffText;
            _selectedStateSnapshotAdviceText = shellState.SnapshotState.SelectedStateSnapshotAdviceText;
            _selectedStateSnapshotSafetyHeadlineText = shellState.SnapshotState.SelectedStateSnapshotSafetyHeadlineText;
            _selectedStateSnapshotSafetyRecommendationText = shellState.SnapshotState.SelectedStateSnapshotSafetyRecommendationText;
            _selectedStateSnapshotRollbackHintText = shellState.SnapshotState.SelectedStateSnapshotRollbackHintText;
            _statusText = shellState.UiFeedbackState.StatusText;
            _logText = shellState.UiFeedbackState.LogText;
            _controlApiToken = shellState.ConfigEditorState.ControlApiToken;
            _hasUnsavedChanges = shellState.ConfigEditorState.HasUnsavedChanges;
            _lastLoadedAtText = shellState.ConfigEditorState.LastLoadedAtText;
            _lastSavedAtText = shellState.ConfigEditorState.LastSavedAtText;

            var config = shellState.ConfigEditorState.Config;
            _openAiApiKey = config.OpenAiApiKey;
            _openAiDefaultApiKey = config.OpenAiDefaultApiKey;
            _openAiDefaultModel = config.OpenAiDefaultModel;
            _openAiModel = config.OpenAiModel;
            _openAiBaseUrl = config.OpenAiBaseUrl;
            _openAiDefaultBaseUrl = config.OpenAiDefaultBaseUrl;
            _openAiDefaultReasoningEffort = config.OpenAiDefaultReasoningEffort;
            _openAiAdvancedReasoningEffort = config.OpenAiAdvancedReasoningEffort;
            _openAiDefaultTextVerbosity = config.OpenAiDefaultTextVerbosity;
            _openAiAdvancedTextVerbosity = config.OpenAiAdvancedTextVerbosity;
            _openAiDefaultEnableWebSearch = config.OpenAiDefaultEnableWebSearch;
            _openAiAdvancedEnableWebSearch = config.OpenAiAdvancedEnableWebSearch;
            _openAiDefaultEnableCodeInterpreter = config.OpenAiDefaultEnableCodeInterpreter;
            _openAiAdvancedEnableCodeInterpreter = config.OpenAiAdvancedEnableCodeInterpreter;
            _openAiAdvancedTriggerPrefixes = config.OpenAiAdvancedTriggerPrefixes;
            _napCatWsUrl = config.NapCatWsUrl;
            _napCatToken = config.NapCatToken;
            _wechatBridgeUrl = config.WechatBridgeUrl;
            _wechatBridgeToken = config.WechatBridgeToken;
            _wechatBotPrefix = config.WechatBotPrefix;
            _botPrefix = config.BotPrefix;
            _botSystemPrompt = NormalizeBotSystemPrompt(config.BotSystemPrompt);
            _botPersona = config.BotPersona;
            _maxOutputChars = config.MaxOutputChars;
            _allowedChatIds = config.AllowedChatIds;
            _allowedUserIds = config.AllowedUserIds;
            BackendRuntimeSnapshotViewHelper.ReplaceRecentActivities(QqRecentActivities, shellState.RecentActivityState.QqRecentActivities);
            BackendRuntimeSnapshotViewHelper.ReplaceRecentActivities(WechatRecentActivities, shellState.RecentActivityState.WechatRecentActivities);
            ReplaceHealthChecks(shellState.RuntimeShellState.HealthReport.Checks);
            ReplaceStateSnapshots(shellState.SnapshotState.StateSnapshots);
        }
        finally
        {
            _suspendDirtyTracking = false;
        }

        QqRecentActivitiesView.Refresh();
        WechatRecentActivitiesView.Refresh();

        RefreshLatestTurnOverview();
        RefreshHealthReport();
        ApplySelectedStateSnapshotPresentation();
        ApplyRestorePresentation();

        foreach (var propertyName in DesktopShellPropertyCatalog.AllPropertyNames())
        {
            OnPropertyChanged(propertyName);
        }

        OnPropertyChanged(nameof(OpenAiApiKey));
        OnPropertyChanged(nameof(OpenAiDefaultApiKey));
        OnPropertyChanged(nameof(OpenAiDefaultModel));
        OnPropertyChanged(nameof(OpenAiModel));
        OnPropertyChanged(nameof(OpenAiBaseUrl));
        OnPropertyChanged(nameof(OpenAiDefaultBaseUrl));
        OnPropertyChanged(nameof(OpenAiDefaultReasoningEffort));
        OnPropertyChanged(nameof(OpenAiAdvancedReasoningEffort));
        OnPropertyChanged(nameof(OpenAiDefaultTextVerbosity));
        OnPropertyChanged(nameof(OpenAiAdvancedTextVerbosity));
        OnPropertyChanged(nameof(OpenAiDefaultEnableWebSearch));
        OnPropertyChanged(nameof(OpenAiAdvancedEnableWebSearch));
        OnPropertyChanged(nameof(OpenAiDefaultEnableCodeInterpreter));
        OnPropertyChanged(nameof(OpenAiAdvancedEnableCodeInterpreter));
        OnPropertyChanged(nameof(IsOpenAiDefaultEnableWebSearchEnabled));
        OnPropertyChanged(nameof(IsOpenAiAdvancedEnableWebSearchEnabled));
        OnPropertyChanged(nameof(IsOpenAiDefaultEnableCodeInterpreterEnabled));
        OnPropertyChanged(nameof(IsOpenAiAdvancedEnableCodeInterpreterEnabled));
        OnPropertyChanged(nameof(OpenAiAdvancedTriggerPrefixes));
        OnPropertyChanged(nameof(NapCatWsUrl));
        OnPropertyChanged(nameof(NapCatToken));
        OnPropertyChanged(nameof(WechatBridgeUrl));
        OnPropertyChanged(nameof(WechatBridgeToken));
        OnPropertyChanged(nameof(WechatBotPrefix));
        OnPropertyChanged(nameof(BotPrefix));
        OnPropertyChanged(nameof(BotSystemPrompt));
        OnPropertyChanged(nameof(BotPersona));
        OnPropertyChanged(nameof(MaxOutputChars));
        OnPropertyChanged(nameof(AllowedChatIds));
        OnPropertyChanged(nameof(AllowedUserIds));
        OnPropertyChanged(nameof(EffectiveBotInstructionsText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(LastLoadedAtText));
        OnPropertyChanged(nameof(LastSavedAtText));
        OnPropertyChanged(nameof(ControlApiToken));
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(LogText));
        OnPropertyChanged(nameof(HasStateRestoreResult));
    }

    private void ApplyCommandResult(DesktopCommandResult result, bool showErrorDialog = true)
    {
        if (result.NextState is not null)
        {
            ApplyState(result.NextState);
        }

        foreach (var logMessage in result.LogMessages)
        {
            AddLog(logMessage);
        }

        foreach (var notification in result.Notifications)
        {
            NotificationRequested?.Invoke(this, notification);
        }

        if (result.Error is not null)
        {
            if (showErrorDialog)
            {
                System.Windows.MessageBox.Show(
                    result.Error.DialogMessage,
                    result.Error.DialogTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            if (!string.IsNullOrWhiteSpace(result.SuggestedHealthActionKey))
            {
                HealthActionRequested?.Invoke(this, result.SuggestedHealthActionKey);
            }
        }

        UpdateCommandStates();
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
        RefreshLatestTurnOverview();
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
        RefreshGuideFlow();
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
                "会被覆盖的内容",
                presentation.ImpactText,
                string.Empty,
                "恢复前建议",
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

    private void ApplyRestoreResultSummary(
        LocalStateSnapshotRestoreResult restoreResult,
        LocalStateSnapshotPreviewResult? preview)
    {
        _lastStateRestoreResult = restoreResult;
        _lastStateRestorePreview = preview;
        ApplyRestorePresentation();
    }

    private void ApplyRestoreAvailabilityCheck()
    {
        ApplyRestorePresentation();
    }

    private void ApplySelectedStateSnapshotPresentation(
        string? diffTextOverride = null,
        string? adviceTextOverride = null)
    {
        var presentation = LocalStateSnapshotPresentationBuilder.BuildSelectionPresentation(
            SelectedStateSnapshot,
            _selectedStateSnapshotPreview,
            diffTextOverride,
            adviceTextOverride);
        SelectedStateSnapshotImpactText = presentation.ImpactText;
        SelectedStateSnapshotDiffText = presentation.DiffText;
        SelectedStateSnapshotAdviceText = presentation.AdviceText;
        SelectedStateSnapshotSafetyHeadlineText = presentation.SafetyHeadlineText;
        SelectedStateSnapshotSafetyRecommendationText = presentation.SafetyRecommendationText;
        SelectedStateSnapshotRollbackHintText = presentation.RollbackHintText;
    }

    private void ApplyRestorePresentation()
    {
        var presentation = LocalStateSnapshotPresentationBuilder.BuildRestorePresentation(
            _lastStateRestoreResult,
            _lastStateRestorePreview,
            BuildRestorePresentationContext());
        LastStateRestoreSummaryText = presentation.SummaryText;
        LastStateRestoreIssueText = presentation.IssueText;
        LastStateRestoreTargetsText = presentation.TargetsText;
        LastStateRestoreSessionsText = presentation.SessionsText;
        LastStateRestoreLatestActivityText = presentation.LatestActivityText;
        LastStateRestoreAdviceText = presentation.AdviceText;
        LastStateRestoreControlPlaneText = presentation.ControlPlaneText;
        LastStateRestoreRuntimeText = presentation.RuntimeText;
        LastStateRestoreNextStepText = presentation.NextStepText;
        LastStateRestorePrimaryActionLabel = presentation.PrimaryAction.Label;
        LastStateRestorePrimaryActionKey = presentation.PrimaryAction.Key;
        LastStateRestoreSecondaryActionLabel = presentation.SecondaryAction.Label;
        LastStateRestoreSecondaryActionKey = presentation.SecondaryAction.Key;
        LastStateRestoreTertiaryActionLabel = presentation.TertiaryAction.Label;
        LastStateRestoreTertiaryActionKey = presentation.TertiaryAction.Key;
    }

    private LocalStateSnapshotRestoreRuntimeContext BuildRestorePresentationContext()
    {
        return new LocalStateSnapshotRestoreRuntimeContext
        {
            ControlApiFailure = _backendControlApiService.LastFailure,
            IsControlApiReachable = IsControlApiReachable,
            CanStartBackend = StartCommand.CanExecute(null),
            IsQqRuntimeReady = _runtimeSnapshot.RuntimeReady == true,
            IsWechatConfigured = _runtimeSnapshot.WechatConfigured == true || !string.IsNullOrWhiteSpace(WechatBridgeUrl),
            IsWechatRuntimeReady = _runtimeSnapshot.WechatRuntimeReady == true
        };
    }

    private void ResetRestoreResultState()
    {
        _lastStateRestoreResult = null;
        _lastStateRestorePreview = null;
        LastStateRestoreText = "尚未恢复状态快照";
        ApplyRestorePresentation();
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

    private void ClearActivityHistory(
        ObservableCollection<BackendRecentActivityItem> recentActivityItems,
        Action clearSelection,
        Action clearPinnedState)
    {
        recentActivityItems.Clear();
        clearSelection();
        clearPinnedState();
        RefreshGuideFlow();
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
            SyncSessionEditorState();
        }
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

            if (!_suspendDirtyTracking)
            {
                SyncSessionEditorState();
            }
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
        _exportSafeRollbackSnapshotCommand.RaiseCanExecuteChanged();
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
