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
    private static readonly string[] ConfigEditorProjectionPropertyNames =
    [
        nameof(OpenAiApiKey),
        nameof(OpenAiDefaultApiKey),
        nameof(OpenAiDefaultModel),
        nameof(OpenAiModel),
        nameof(OpenAiBaseUrl),
        nameof(OpenAiDefaultBaseUrl),
        nameof(OpenAiDefaultReasoningEffort),
        nameof(OpenAiAdvancedReasoningEffort),
        nameof(OpenAiDefaultTextVerbosity),
        nameof(OpenAiAdvancedTextVerbosity),
        nameof(OpenAiDefaultEnableWebSearch),
        nameof(OpenAiAdvancedEnableWebSearch),
        nameof(OpenAiDefaultEnableCodeInterpreter),
        nameof(OpenAiAdvancedEnableCodeInterpreter),
        nameof(IsOpenAiDefaultEnableWebSearchEnabled),
        nameof(IsOpenAiAdvancedEnableWebSearchEnabled),
        nameof(IsOpenAiDefaultEnableCodeInterpreterEnabled),
        nameof(IsOpenAiAdvancedEnableCodeInterpreterEnabled),
        nameof(OpenAiAdvancedTriggerPrefixes),
        nameof(DeepSeekFallbackEnabled),
        nameof(DeepSeekApiKey),
        nameof(DeepSeekModel),
        nameof(DeepSeekBaseUrl),
        nameof(IsDeepSeekFallbackEnabled),
        nameof(NapCatWsUrl),
        nameof(NapCatToken),
        nameof(WechatBridgeUrl),
        nameof(WechatBridgeToken),
        nameof(WechatBotPrefix),
        nameof(BotPrefix),
        nameof(BotSystemPrompt),
        nameof(BotPersona),
        nameof(MaxOutputChars),
        nameof(AllowedChatIds),
        nameof(AllowedUserIds),
        nameof(EffectiveBotInstructionsText)
    ];
    private static readonly string[] ShellFeedbackPropertyNames =
    [
        nameof(StatusText),
        nameof(LastLoadedAtText),
        nameof(LastSavedAtText),
        nameof(ControlApiToken),
        nameof(HasUnsavedChanges),
        nameof(LogText),
        nameof(HasStateRestoreResult)
    ];

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
    private bool _isBackendRootValid;
    private string _envFilePath = string.Empty;
    private string _backendRootStateText = "backend 根目录无效";
    private string _sessionStorePathText = string.Empty;
    private string _sessionStoreStateText = "后端目录有效后才能显示会话路径";
    private string _imageCachePathText = string.Empty;
    private string _imageCacheStateText = "后端目录有效后才能显示缓存路径";
    private string _activityStatePathText = string.Empty;
    private string _stateSnapshotFolderPathText = string.Empty;
    private string _controlApiEndpointText = "http://127.0.0.1:3199";
    private string _controlApiTokenStateText = "本机令牌未设置";
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
    private string _deepSeekFallbackEnabled = "false";
    private string _deepSeekApiKey = string.Empty;
    private string _deepSeekModel = "deepseek-chat";
    private string _deepSeekBaseUrl = "https://api.deepseek.com/v1";
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
    private bool _disposed;
    private bool _pinSelectedQqActivity;
    private bool _pinSelectedWechatActivity;
    private bool _showOnlyQqFailures;
    private bool _showOnlyWechatFailures;
    private BackendRecentActivityItem? _selectedQqRecentActivity;
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
            new DesktopShellState());
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
            _ => ClearQqActivityHistory(),
            _ => QqRecentActivities.Count > 0);
        _clearWechatActivityHistoryCommand = new RelayCommand(
            _ => ClearWechatActivityHistory(),
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

        AutoStartEnabled = ReadAutoStartEnabled();
        SyncSessionEditorState();
        AutoDetectBackendRoot();
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
                ApplyStateAndRefreshCommands(_controlPlaneSession.UpdateBackendRoot(value, _backendRootDetected));
            }
        }
    }

    public string EnvFilePath => _envFilePath;

    public bool IsBackendRootValid => _isBackendRootValid;

    public string BackendRootStateText => _backendRootStateText;

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

    public string DeepSeekFallbackEnabled
    {
        get => _deepSeekFallbackEnabled;
        set => SetTrackedBooleanStringProperty(
            ref _deepSeekFallbackEnabled,
            value,
            nameof(IsDeepSeekFallbackEnabled));
    }

    public string DeepSeekApiKey
    {
        get => _deepSeekApiKey;
        set => SetTrackedProperty(ref _deepSeekApiKey, value);
    }

    public string DeepSeekModel
    {
        get => _deepSeekModel;
        set => SetTrackedProperty(ref _deepSeekModel, value);
    }

    public string DeepSeekBaseUrl
    {
        get => _deepSeekBaseUrl;
        set => SetTrackedProperty(ref _deepSeekBaseUrl, value);
    }

    public bool IsDeepSeekFallbackEnabled
    {
        get => ParseBooleanFlag(DeepSeekFallbackEnabled);
        set => DeepSeekFallbackEnabled = value ? "true" : "false";
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
            if (_pinSelectedQqActivity != value)
            {
                ApplyState(_controlPlaneSession.ApplyActivityStateSelection(pinSelectedQqActivity: value));
            }
        }
    }

    public bool ShowOnlyQqFailures
    {
        get => _showOnlyQqFailures;
        set
        {
            if (_showOnlyQqFailures != value)
            {
                ApplyState(_controlPlaneSession.ApplyActivityStateSelection(showOnlyQqFailures: value));
            }
        }
    }

    public BackendRecentActivityItem? SelectedQqRecentActivity
    {
        get => _selectedQqRecentActivity;
        set
        {
            if (!ReferenceEquals(_selectedQqRecentActivity, value))
            {
                ApplyState(_controlPlaneSession.ApplyActivityStateSelection(
                    selectedQqRecentActivity: value,
                    updateQqSelection: true));
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
            if (_pinSelectedWechatActivity != value)
            {
                ApplyState(_controlPlaneSession.ApplyActivityStateSelection(pinSelectedWechatActivity: value));
            }
        }
    }

    public bool ShowOnlyWechatFailures
    {
        get => _showOnlyWechatFailures;
        set
        {
            if (_showOnlyWechatFailures != value)
            {
                ApplyState(_controlPlaneSession.ApplyActivityStateSelection(showOnlyWechatFailures: value));
            }
        }
    }

    public BackendRecentActivityItem? SelectedWechatRecentActivity
    {
        get => _selectedWechatRecentActivity;
        set
        {
            if (!ReferenceEquals(_selectedWechatRecentActivity, value))
            {
                ApplyState(_controlPlaneSession.ApplyActivityStateSelection(
                    selectedWechatRecentActivity: value,
                    updateWechatSelection: true));
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
                SyncSessionEditorState();
            }
        }
    }

    public string ControlApiTokenStateText => _controlApiTokenStateText;

    public string ControlApiEndpointText => _controlApiEndpointText;

    public string SessionStorePathText => _sessionStorePathText;

    public string ImageCachePathText => _imageCachePathText;

    public string ActivityStatePathText => _activityStatePathText;

    public string StateSnapshotFolderPathText => _stateSnapshotFolderPathText;

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
                ApplyStateAndRefreshCommands(_controlPlaneSession.UpdateSelectedStateSnapshot(value));
                _ = RefreshSelectedStateSnapshotPreviewAsync();
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

    public string SessionStoreStateText => _sessionStoreStateText;

    public string ImageCacheStateText => _imageCacheStateText;

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

    private bool ReadAutoStartEnabled()
    {
        try
        {
            return _autoStartService.IsEnabled();
        }
        catch (Exception ex)
        {
            AddLog($"Failed to read auto-start state: {ex.Message}");
            return false;
        }
    }

    private async Task LoadConfigAsync()
    {
        if (!_suspendDirtyTracking)
        {
            SyncSessionEditorState();
        }
        ApplyCommandResult(await _controlPlaneSession.LoadConfigAsync());
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
    }

    private async Task<bool> SaveConfigAsync(bool showUiErrors)
    {
        SyncSessionEditorState();
        var sessionSaveResult = await _controlPlaneSession.SaveConfigAsync(BuildConfig(), showUiErrors);
        ApplyCommandResult(sessionSaveResult, showUiErrors);
        return sessionSaveResult.Succeeded;
    }

    private async Task StartBackendAsync()
    {
        SyncSessionEditorState();
        ApplyCommandResult(await _controlPlaneSession.StartBackendAsync(BuildConfig()));
    }

    private async Task StopBackendAsync()
    {
        SyncSessionEditorState();
        ApplyCommandResult(await _controlPlaneSession.StopBackendAsync());
    }

    private async Task ToggleAutoStartAsync()
    {
        try
        {
            var nextValue = !AutoStartEnabled;
            _autoStartService.SetEnabled(nextValue);
            AutoStartEnabled = nextValue;
            ApplyUiOutcome(
                nextValue ? "已启用常驻模式开机启动" : "已关闭开机启动",
                logMessages:
                [
                    nextValue
                        ? "已启用常驻模式开机启动。"
                        : "已关闭开机启动。手动启动后，托盘行为仍然可用。"
                ],
                notifications:
                [
                    new TrayNotification
                    {
                        Title = "Local AI Runtime",
                        Message = nextValue
                            ? "已启用常驻模式。Windows 登录后会以最小化方式启动并确保 runtime 运行。"
                            : "已关闭常驻模式开机启动。手动启动后仍可使用托盘模式。",
                        Icon = Forms.ToolTipIcon.Info
                    }
                ]);
        }
        catch (Exception ex)
        {
            ApplyUiError(
                statusText: "更新开机启动失败",
                logMessage: $"更新开机启动失败: {ex.Message}",
                showDialog: true,
                dialogTitle: "更新开机启动失败",
                dialogMessage: $"更新开机启动失败：\n{ex.Message}");
        }

        await Task.CompletedTask;
    }

    private void AutoDetectBackendRoot()
    {
        _backendRootDetected = PathDiscoveryService.TryDiscoverBackendRoot(out var detectedPath);
        BackendRootPath = detectedPath;
        ApplyUiOutcome(
            _backendRootDetected ? "已检测到 backend 根目录" : "未检测到 backend 根目录",
            logMessages:
            [
                _backendRootDetected
                    ? $"自动检测到 backend 根目录: {BackendRootPath}"
                    : $"继续使用当前 backend 根目录: {BackendRootPath}"
            ],
            refreshCommands: true);
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
        ApplyCommandResult(_controlPlaneSession.OpenSessionStoreFolder());
    }

    private void OpenImageCacheFolder()
    {
        ApplyCommandResult(_controlPlaneSession.OpenImageCacheFolder());
    }

    private void ClearImageCache()
    {
        ApplyCommandResult(_controlPlaneSession.ClearImageCache());
    }

    private async Task ExportStateSnapshotAsync()
    {
        ApplyCommandResult(await _controlPlaneSession.ExportStateSnapshotAsync());
    }

    private async Task ExportSafeStateSnapshotAsync()
    {
        ApplyCommandResult(await _controlPlaneSession.ExportSafeStateSnapshotAsync());
    }

    private async Task ExportSafeRollbackSnapshotAsync()
    {
        if (!IsBackendRootValid || SelectedStateSnapshot is null)
        {
            return;
        }

        ApplyCommandResult(await _controlPlaneSession.ExportSafeRollbackSnapshotAsync(SelectedStateSnapshot.ArchivePath));
    }

    private async Task RestoreLatestStateSnapshotAsync()
    {
        if (!IsBackendRootValid)
        {
            ApplyUiOutcome("后端目录无效", ["后端目录无效，无法恢复状态快照。"]);
            return;
        }

        await RunConfirmedSnapshotOperationAsync(
            buildConfirmationAsync: () => _controlPlaneSession.BuildRestoreLatestStateSnapshotConfirmationAsync(),
            inProgressStatusText: "正在恢复最新状态快照...",
            cancelledStatusText: "已取消恢复最新快照",
            buildCancelledLogMessage: (confirmation) => $"已取消恢复最新快照：{confirmation.ArchivePath}",
            failureStatusText: "恢复状态快照失败",
            failureDialogTitle: "恢复状态快照失败",
            buildFailureLogMessage: (ex) => $"恢复最新状态快照失败：{ex.Message}",
            buildFailureDialogMessage: (ex) => $"恢复最新状态快照失败：\n{ex.Message}",
            executeConfirmedAsync: async (_) => ApplyCommandResult(await _controlPlaneSession.RestoreLatestStateSnapshotAsync()));
    }

    private async Task RestoreSelectedStateSnapshotAsync()
    {
        if (!IsBackendRootValid || SelectedStateSnapshot is null)
        {
            return;
        }

        await RunConfirmedSnapshotOperationAsync(
            buildConfirmationAsync: () => _controlPlaneSession.BuildRestoreSelectedStateSnapshotConfirmationAsync(
                SelectedStateSnapshot.ArchivePath),
            inProgressStatusText: "正在恢复选中状态快照...",
            cancelledStatusText: "已取消恢复选中快照",
            buildCancelledLogMessage: (confirmation) => $"已取消恢复选中快照：{confirmation.ArchivePath}",
            failureStatusText: "恢复选中快照失败",
            failureDialogTitle: "恢复选中快照失败",
            buildFailureLogMessage: (ex) => $"恢复选中快照失败：{ex.Message}",
            buildFailureDialogMessage: (ex) => $"恢复选中快照失败：\n{ex.Message}",
            executeConfirmedAsync: async (confirmation) => ApplyCommandResult(
                await _controlPlaneSession.RestoreSelectedStateSnapshotAsync(confirmation.ArchivePath)));
    }

    private async Task DeleteSelectedStateSnapshotAsync()
    {
        if (!IsBackendRootValid || SelectedStateSnapshot is null)
        {
            return;
        }

        var archivePath = SelectedStateSnapshot.ArchivePath;
        await RunConfirmedSnapshotOperationAsync(
            buildConfirmationAsync: () => Task.FromResult(_controlPlaneSession.BuildDeleteSelectedStateSnapshotConfirmation(archivePath)),
            inProgressStatusText: "正在删除选中快照...",
            cancelledStatusText: "已取消删除快照",
            buildCancelledLogMessage: (_) => $"已取消删除选中快照：{archivePath}",
            failureStatusText: "删除快照失败",
            failureDialogTitle: "删除选中快照失败",
            buildFailureLogMessage: (ex) => $"删除选中快照失败：{ex.Message}",
            buildFailureDialogMessage: (ex) => $"删除选中快照失败：\n{ex.Message}",
            executeConfirmedAsync: async (confirmation) => ApplyCommandResult(
                await _controlPlaneSession.DeleteSelectedStateSnapshotAsync(confirmation.ArchivePath)));
    }

    private void OpenStateSnapshotFolder()
    {
        ApplyCommandResult(_controlPlaneSession.OpenStateSnapshotFolder());
    }

    private void ApplyBaseUrlPreset(object? parameter)
    {
        var preset = parameter as string ?? string.Empty;
        const string DeepSeekPresetPrefix = "deepseek:";

        if (preset.StartsWith(DeepSeekPresetPrefix, StringComparison.Ordinal))
        {
            DeepSeekBaseUrl = preset[DeepSeekPresetPrefix.Length..];
            AddLog($"Applied DeepSeek base URL preset: {DeepSeekBaseUrl}");
            return;
        }

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
            DeepSeekFallbackEnabled = DeepSeekFallbackEnabled.Trim(),
            DeepSeekApiKey = DeepSeekApiKey.Trim(),
            DeepSeekModel = DeepSeekModel.Trim(),
            DeepSeekBaseUrl = DeepSeekBaseUrl.Trim(),
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
        SyncSessionEditorState();
        ApplyCommandResult(await _controlPlaneSession.PollStatusAsync(), showErrorDialog: false);
    }

    private async Task RefreshStateSnapshotsAsync()
    {
        ApplyCommandResult(await _controlPlaneSession.RefreshStateSnapshotsAsync(selectArchivePath: null), showErrorDialog: false);
    }

    private async Task RefreshStateSnapshotsAsync(string? selectArchivePath)
    {
        ApplyCommandResult(await _controlPlaneSession.RefreshStateSnapshotsAsync(selectArchivePath), showErrorDialog: false);
    }

    private async Task RefreshSelectedStateSnapshotPreviewAsync()
    {
        ApplyCommandResult(await _controlPlaneSession.RefreshSelectedStateSnapshotPreviewAsync(), showErrorDialog: false);
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
            ApplyLocalDocumentState(shellState.LocalDocumentState);
            ApplyRuntimeShellState(shellState.RuntimeShellState);
            ApplyRecentActivityState(shellState.RecentActivityState);
            ApplySnapshotState(shellState.SnapshotState);
            ApplyUiFeedbackState(shellState.UiFeedbackState);
            ApplyConfigEditorState(shellState.ConfigEditorState);
            ApplyShellCollections(shellState);
        }
        finally
        {
            _suspendDirtyTracking = false;
        }

        RefreshCollectionViews();
        NotifyShellProjectionPropertiesChanged();
        NotifyPropertyGroup(ConfigEditorProjectionPropertyNames);
        NotifyPropertyGroup(ShellFeedbackPropertyNames);
    }

    private void ApplyStateAndRefreshCommands(DesktopShellState shellState)
    {
        ApplyState(shellState);
        UpdateCommandStates();
    }

    private void ApplyLocalDocumentState(DesktopLocalDocumentState localDocumentState)
    {
        _envDocument = localDocumentState.ConfigDocument.Document;
        _backendRootPath = localDocumentState.BackendRootPath;
        _backendRootDetected = localDocumentState.BackendRootDetected;
        _isBackendRootValid = localDocumentState.IsBackendRootValid;
        _envFilePath = localDocumentState.EnvFilePath;
        _backendRootStateText = localDocumentState.BackendRootStateText;
        _sessionStorePathText = localDocumentState.SessionStorePathText;
        _sessionStoreStateText = localDocumentState.SessionStoreStateText;
        _imageCachePathText = localDocumentState.ImageCachePathText;
        _imageCacheStateText = localDocumentState.ImageCacheStateText;
        _activityStatePathText = localDocumentState.ActivityStatePathText;
        _stateSnapshotFolderPathText = localDocumentState.StateSnapshotFolderPathText;
        _controlApiEndpointText = localDocumentState.ControlApiEndpointText;
        _controlApiTokenStateText = localDocumentState.ControlApiTokenStateText;
    }

    private void ApplyRuntimeShellState(DesktopRuntimeSnapshotState runtimeShellState)
    {
        _runtimeSnapshot = runtimeShellState.RuntimeSnapshot;
        _latestTurnOverview = runtimeShellState.LatestTurnOverview;
        _healthReport = runtimeShellState.HealthReport;
        _guideFlow = runtimeShellState.GuideFlow;
        _controlApiPollState = runtimeShellState.ControlApiPollState;
        _autoStartEnabled = runtimeShellState.AutoStartEnabled;
        _isProcessRunning = runtimeShellState.IsProcessRunning;
    }

    private void ApplyRecentActivityState(DesktopRecentActivityState recentActivityState)
    {
        _pinSelectedQqActivity = recentActivityState.PinSelectedQqActivity;
        _pinSelectedWechatActivity = recentActivityState.PinSelectedWechatActivity;
        _showOnlyQqFailures = recentActivityState.ShowOnlyQqFailures;
        _showOnlyWechatFailures = recentActivityState.ShowOnlyWechatFailures;
        _selectedQqRecentActivity = recentActivityState.SelectedQqRecentActivity;
        _selectedWechatRecentActivity = recentActivityState.SelectedWechatRecentActivity;
    }

    private void ApplySnapshotState(DesktopSnapshotState snapshotState)
    {
        _selectedStateSnapshot = snapshotState.SelectedStateSnapshot;
        _selectedStateSnapshotPreview = snapshotState.SelectedStateSnapshotPreview;
        _lastStateRestoreResult = snapshotState.LastStateRestoreResult;
        _lastStateRestorePreview = snapshotState.LastStateRestorePreview;
        _lastStateSnapshotText = snapshotState.LastStateSnapshotText;
        _lastStateRestoreText = snapshotState.LastStateRestoreText;
        _lastStateRestoreSummaryText = snapshotState.LastStateRestoreSummaryText;
        _lastStateRestoreIssueText = snapshotState.LastStateRestoreIssueText;
        _lastStateRestoreTargetsText = snapshotState.LastStateRestoreTargetsText;
        _lastStateRestoreSessionsText = snapshotState.LastStateRestoreSessionsText;
        _lastStateRestoreLatestActivityText = snapshotState.LastStateRestoreLatestActivityText;
        _lastStateRestoreAdviceText = snapshotState.LastStateRestoreAdviceText;
        _lastStateRestoreControlPlaneText = snapshotState.LastStateRestoreControlPlaneText;
        _lastStateRestoreRuntimeText = snapshotState.LastStateRestoreRuntimeText;
        _lastStateRestoreNextStepText = snapshotState.LastStateRestoreNextStepText;
        _lastStateRestorePrimaryActionLabel = snapshotState.LastStateRestorePrimaryActionLabel;
        _lastStateRestorePrimaryActionKey = snapshotState.LastStateRestorePrimaryActionKey;
        _lastStateRestoreSecondaryActionLabel = snapshotState.LastStateRestoreSecondaryActionLabel;
        _lastStateRestoreSecondaryActionKey = snapshotState.LastStateRestoreSecondaryActionKey;
        _lastStateRestoreTertiaryActionLabel = snapshotState.LastStateRestoreTertiaryActionLabel;
        _lastStateRestoreTertiaryActionKey = snapshotState.LastStateRestoreTertiaryActionKey;
        _selectedStateSnapshotImpactText = snapshotState.SelectedStateSnapshotImpactText;
        _selectedStateSnapshotDiffText = snapshotState.SelectedStateSnapshotDiffText;
        _selectedStateSnapshotAdviceText = snapshotState.SelectedStateSnapshotAdviceText;
        _selectedStateSnapshotSafetyHeadlineText = snapshotState.SelectedStateSnapshotSafetyHeadlineText;
        _selectedStateSnapshotSafetyRecommendationText = snapshotState.SelectedStateSnapshotSafetyRecommendationText;
        _selectedStateSnapshotRollbackHintText = snapshotState.SelectedStateSnapshotRollbackHintText;
    }

    private void ApplyUiFeedbackState(DesktopUiFeedbackState uiFeedbackState)
    {
        _statusText = uiFeedbackState.StatusText;
        _logText = uiFeedbackState.LogText;
    }

    private void ApplyConfigEditorState(DesktopConfigEditorState configEditorState)
    {
        _controlApiToken = configEditorState.ControlApiToken;
        _hasUnsavedChanges = configEditorState.HasUnsavedChanges;
        _lastLoadedAtText = configEditorState.LastLoadedAtText;
        _lastSavedAtText = configEditorState.LastSavedAtText;
        ApplyConfigFields(configEditorState.Config);
    }

    private void ApplyConfigFields(BotConfig config)
    {
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
        _deepSeekFallbackEnabled = config.DeepSeekFallbackEnabled;
        _deepSeekApiKey = config.DeepSeekApiKey;
        _deepSeekModel = config.DeepSeekModel;
        _deepSeekBaseUrl = config.DeepSeekBaseUrl;
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
    }

    private void ApplyShellCollections(DesktopShellState shellState)
    {
        BackendRuntimeSnapshotViewHelper.ReplaceRecentActivities(QqRecentActivities, shellState.RecentActivityState.QqRecentActivities);
        BackendRuntimeSnapshotViewHelper.ReplaceRecentActivities(WechatRecentActivities, shellState.RecentActivityState.WechatRecentActivities);
        ReplaceHealthChecks(shellState.RuntimeShellState.HealthReport.Checks);
        ReplaceStateSnapshots(shellState.SnapshotState.StateSnapshots);
    }

    private void RefreshCollectionViews()
    {
        QqRecentActivitiesView.Refresh();
        WechatRecentActivitiesView.Refresh();
    }

    private void NotifyShellProjectionPropertiesChanged()
    {
        NotifyPropertyGroup(DesktopShellPropertyCatalog.RuntimeSnapshotPropertyNames);
        NotifyPropertyGroup(DesktopShellPropertyCatalog.HealthPropertyNames);
        NotifyPropertyGroup(DesktopShellPropertyCatalog.GuidePropertyNames);
        NotifyPropertyGroup(DesktopShellPropertyCatalog.SnapshotPropertyNames);
        NotifyPropertyGroup(DesktopShellPropertyCatalog.LocalDocumentPropertyNames);
    }

    private void NotifyPropertyGroup(IEnumerable<string> propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void ApplyCommandResult(DesktopCommandResult result, bool showErrorDialog = true)
    {
        if (result.NextState is not null)
        {
            ApplyState(result.NextState);
        }

        DesktopControlPlaneFeedback.ApplyCommandResult(
            result,
            showDialog: showErrorDialog,
            setStatusText: (text) => StatusText = text,
            addLog: AddLog,
            notify: (notification) => NotificationRequested?.Invoke(this, notification),
            showErrorDialog: ShowOperationErrorDialog,
            routeSuggestedAction: (actionKey) => HealthActionRequested?.Invoke(this, actionKey));

        UpdateCommandStates();
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

    private static void ShowOperationErrorDialog(string title, string message)
    {
        System.Windows.MessageBox.Show(
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private async Task RunConfirmedSnapshotOperationAsync(
        Func<Task<DesktopConfirmationPrompt>> buildConfirmationAsync,
        string inProgressStatusText,
        string cancelledStatusText,
        Func<DesktopConfirmationPrompt, string> buildCancelledLogMessage,
        string failureStatusText,
        string failureDialogTitle,
        Func<Exception, string> buildFailureLogMessage,
        Func<Exception, string> buildFailureDialogMessage,
        Func<DesktopConfirmationPrompt, Task> executeConfirmedAsync)
    {
        try
        {
            var confirmation = await buildConfirmationAsync();

            if (!_confirmationDialogService.Confirm(confirmation.Title, confirmation.Message))
            {
                ApplyUiOutcome(cancelledStatusText, [buildCancelledLogMessage(confirmation)]);
                return;
            }

            StatusText = inProgressStatusText;
            await executeConfirmedAsync(confirmation);
        }
        catch (Exception ex)
        {
            ApplyUiError(
                statusText: failureStatusText,
                logMessage: buildFailureLogMessage(ex),
                showDialog: true,
                dialogTitle: failureDialogTitle,
                dialogMessage: buildFailureDialogMessage(ex));
        }
        finally
        {
            UpdateCommandStates();
        }
    }

    private void ApplyUiOutcome(
        string statusText,
        IEnumerable<string>? logMessages = null,
        IEnumerable<TrayNotification>? notifications = null,
        bool refreshCommands = false)
    {
        DesktopControlPlaneFeedback.ApplyOutcome(
            statusText,
            logMessages,
            notifications,
            setStatusText: (text) => StatusText = text,
            addLog: AddLog,
            notify: (notification) => NotificationRequested?.Invoke(this, notification));

        if (refreshCommands)
        {
            UpdateCommandStates();
        }
    }

    private void ApplyUiError(
        string statusText,
        string logMessage,
        bool showDialog,
        string dialogTitle,
        string dialogMessage,
        bool refreshCommands = false)
    {
        DesktopControlPlaneFeedback.ApplyError(
            statusText,
            logMessage,
            showDialog,
            dialogTitle,
            dialogMessage,
            setStatusText: (text) => StatusText = text,
            addLog: AddLog,
            showErrorDialog: ShowOperationErrorDialog);

        if (refreshCommands)
        {
            UpdateCommandStates();
        }
    }

    private void ClearQqActivityHistory()
    {
        ApplyStateAndRefreshCommands(_controlPlaneSession.ClearQqActivityHistory());
    }

    private void ClearWechatActivityHistory()
    {
        ApplyStateAndRefreshCommands(_controlPlaneSession.ClearWechatActivityHistory());
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

}
