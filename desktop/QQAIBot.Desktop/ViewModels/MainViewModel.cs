using System.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;

using QQAIBot.Desktop.Infrastructure;
using QQAIBot.Desktop.Models;
using QQAIBot.Desktop.Services;

namespace QQAIBot.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const int ControlApiRecoveryAttemptThreshold = 2;
    private const int ControlApiOutageNotificationThreshold = 3;
    private const string ControlApiTokenEnvKey = "QQ_AI_BOT_CONTROL_API_TOKEN";
    private static readonly string DefaultBotInstructionsTextValue = string.Join(
        Environment.NewLine,
        [
            "你是 QQ 群助手。",
            "默认使用简体中文。",
            "回答直接、准确、简洁。",
            "不要说教。",
            "不要输出多余免责声明。",
            "不确定时明确说不确定。"
        ]);

    private readonly IAutoStartService _autoStartService;
    private readonly ILocalConfigFallbackReader _localConfigFallbackReader;
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
    private readonly AsyncRelayCommand _startCommand;
    private readonly AsyncRelayCommand _stopCommand;
    private readonly AsyncRelayCommand _toggleAutoStartCommand;
    private readonly RelayCommand _autoDetectCommand;
    private readonly RelayCommand _openBackendFolderCommand;
    private readonly RelayCommand _applyBaseUrlPresetCommand;
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
    private BackendRuntimeSnapshotViewState _runtimeSnapshot = new();
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

    public MainViewModel(
        IAutoStartService? autoStartService = null,
        ILocalConfigFallbackReader? localConfigFallbackReader = null,
        IBackendControlApiService? backendControlApiService = null,
        IBotProcessService? botProcessService = null,
        IActivityStateStore? activityStateStore = null)
    {
        _autoStartService = autoStartService ?? new AutoStartService();
        _localConfigFallbackReader = localConfigFallbackReader ?? new LocalEnvConfigFallbackReader();
        _backendControlApiService = backendControlApiService ?? new BackendControlApiService();
        _botProcessService = botProcessService ?? new BotProcessService();
        _activityStateStore = activityStateStore ?? new LocalActivityStateStore();
        _activityStatePolicy = DesktopActivityStatePolicy.Default;
        _uiDispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _reloadCommand = new AsyncRelayCommand(LoadConfigAsync, CanLoadOrSave);
        _saveCommand = new AsyncRelayCommand(SaveConfigAsync, CanLoadOrSave);
        _startCommand = new AsyncRelayCommand(StartBackendAsync, CanStartBackend);
        _stopCommand = new AsyncRelayCommand(StopBackendAsync, CanStopBackend);
        _toggleAutoStartCommand = new AsyncRelayCommand(ToggleAutoStartAsync);
        _autoDetectCommand = new RelayCommand(_ => AutoDetectBackendRoot());
        _openBackendFolderCommand = new RelayCommand(
            _ => OpenBackendFolder(),
            _ => Directory.Exists(BackendRootPath)
        );
        _applyBaseUrlPresetCommand = new RelayCommand(ApplyBaseUrlPreset);
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
        AddLog("Desktop UI initialized.");
    }

    public ICommand ReloadCommand => _reloadCommand;
    public ICommand SaveCommand => _saveCommand;
    public ICommand StartCommand => _startCommand;
    public ICommand StopCommand => _stopCommand;
    public ICommand ToggleAutoStartCommand => _toggleAutoStartCommand;
    public ICommand AutoDetectCommand => _autoDetectCommand;
    public ICommand OpenBackendFolderCommand => _openBackendFolderCommand;
    public ICommand ApplyBaseUrlPresetCommand => _applyBaseUrlPresetCommand;
    public ICommand ClearLogsCommand => _clearLogsCommand;
    public ICommand ClearQqActivityHistoryCommand => _clearQqActivityHistoryCommand;
    public ICommand ClearWechatActivityHistoryCommand => _clearWechatActivityHistoryCommand;

    public ObservableCollection<BackendRecentActivityItem> QqRecentActivities { get; } = [];

    public ObservableCollection<BackendRecentActivityItem> WechatRecentActivities { get; } = [];

    public ICollectionView QqRecentActivitiesView { get; }

    public ICollectionView WechatRecentActivitiesView { get; }

    public string BackendRootPath
    {
        get => _backendRootPath;
        set
        {
            if (SetProperty(ref _backendRootPath, value))
            {
                OnPropertyChanged(nameof(EnvFilePath));
                OnPropertyChanged(nameof(IsBackendRootValid));
                OnPropertyChanged(nameof(BackendRootStateText));
                LoadActivityState();
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
            }
        }
    }

    public string AutoStartStateText => AutoStartEnabled ? "Startup enabled" : "Startup disabled";

    public string AutoStartButtonText => AutoStartEnabled ? "Disable startup" : "Enable startup";

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (SetProperty(ref _hasUnsavedChanges, value))
            {
                OnPropertyChanged(nameof(ConfigStateText));
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
                CopyControlApiToken(localEnvDocument, _envDocument);
            }
            else
            {
                _envDocument = localEnvDocument;
            }

            ApplyConfigToView(_envDocument.Config);
            ApplyBackendRuntimeStatus(apiStatus, apiStatus is not null);

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
            DesktopControlPlaneFeedback.ApplyError(
                statusText: "Load failed",
                logMessage: $"Load config failed: {ex.Message}",
                showDialog: true,
                dialogTitle: "Load failed",
                dialogMessage: $"Load config failed:\n{ex.Message}",
                setStatusText: (statusText) => StatusText = statusText,
                addLog: AddLog,
                showErrorDialog: (title, message) => System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error));
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
            DesktopControlPlaneFeedback.ApplyError(
                statusText: "Save failed",
                logMessage: $"Save config failed: {ex.Message}",
                showDialog: showUiErrors,
                dialogTitle: "Save failed",
                dialogMessage: $"Save config failed:\n{ex.Message}",
                setStatusText: (statusText) => StatusText = statusText,
                addLog: AddLog,
                showErrorDialog: (title, message) => System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error));

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
            DesktopControlPlaneFeedback.ApplyError(
                statusText: "Start failed",
                logMessage: $"Start backend failed: {ex.Message}",
                showDialog: true,
                dialogTitle: "Start failed",
                dialogMessage: $"Start backend failed:\n{ex.Message}",
                setStatusText: (statusText) => StatusText = statusText,
                addLog: AddLog,
                showErrorDialog: (title, message) => System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error));
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
            DesktopControlPlaneFeedback.ApplyError(
                statusText: "Stop failed",
                logMessage: $"Stop backend failed: {ex.Message}",
                showDialog: true,
                dialogTitle: "Stop failed",
                dialogMessage: $"Stop backend failed:\n{ex.Message}",
                setStatusText: (statusText) => StatusText = statusText,
                addLog: AddLog,
                showErrorDialog: (title, message) => System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error));
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
            StatusText = nextValue ? "Startup enabled" : "Startup disabled";
            AddLog(nextValue
                ? "Enabled app launch at Windows sign-in."
                : "Disabled app launch at Windows sign-in.");
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

        Process.Start(new ProcessStartInfo
        {
            FileName = BackendRootPath,
            UseShellExecute = true
        });
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

    private static void CopyControlApiToken(EnvDocument? source, EnvDocument target)
    {
        if (source?.ExtraValues.TryGetValue(ControlApiTokenEnvKey, out var accessToken) == true)
        {
            target.ExtraValues[ControlApiTokenEnvKey] = accessToken;
            return;
        }

        target.ExtraValues.Remove(ControlApiTokenEnvKey);
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

    private void ClearActivityHistory(
        ObservableCollection<BackendRecentActivityItem> recentActivityItems,
        Action clearSelection,
        Action clearPinnedState)
    {
        recentActivityItems.Clear();
        clearSelection();
        clearPinnedState();
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
        _startCommand.RaiseCanExecuteChanged();
        _stopCommand.RaiseCanExecuteChanged();
        _toggleAutoStartCommand.RaiseCanExecuteChanged();
        _autoDetectCommand.RaiseCanExecuteChanged();
        _openBackendFolderCommand.RaiseCanExecuteChanged();
        _applyBaseUrlPresetCommand.RaiseCanExecuteChanged();
        _clearLogsCommand.RaiseCanExecuteChanged();
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
