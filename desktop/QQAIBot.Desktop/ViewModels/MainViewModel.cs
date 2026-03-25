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
    private string _botPersona = string.Empty;
    private string _maxOutputChars = "800";
    private string _allowedChatIds = string.Empty;
    private string _allowedUserIds = string.Empty;
    private string _lastLoadedAtText = "Not loaded";
    private string _lastSavedAtText = "Not saved";
    private string _logText = string.Empty;
    private bool _logDirty;
    private int? _lastWorkerProcessId;
    private int? _lastWechatWorkerProcessId;
    private bool? _lastRuntimeActive;
    private bool? _lastRuntimeReady;
    private bool? _lastWechatConfigured;
    private bool? _lastWechatRuntimeActive;
    private bool? _lastWechatRuntimeReady;
    private bool? _lastWechatBridgeConnected;
    private bool? _lastControlApiReachable;
    private int _consecutiveControlApiFailures;
    private bool _controlApiOutageNotified;
    private bool _controlApiUnauthorizedNotified;
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
    private BackendLlmRequestStatus? _lastQqLlmRequest;
    private BackendLlmFailureStatus? _lastQqLlmFailure;
    private string _lastWechatRequestEventKey = string.Empty;
    private string _lastWechatFailureEventKey = string.Empty;
    private BackendRecentActivityItem? _selectedWechatRecentActivity;
    private BackendLlmRequestStatus? _lastWechatLlmRequest;
    private BackendLlmFailureStatus? _lastWechatLlmFailure;

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

    public string EffectiveBotInstructionsText => BuildEffectiveBotInstructions(BotPersona);

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
        _lastWechatConfigured != true
            ? "Wechat disabled"
            : _lastWechatRuntimeActive == true
                ? "Wechat runtime active"
                : "Wechat runtime stopped";

    public string RuntimeReadyText =>
        _lastRuntimeReady == true ? "QQ channel ready" : "QQ channel not ready";

    public string WechatRuntimeReadyText =>
        _lastWechatConfigured != true
            ? "Wechat channel disabled"
            : _lastWechatRuntimeReady == true
                ? "Wechat channel ready"
                : "Wechat channel not ready";

    public string WechatBridgeStateText =>
        _lastWechatBridgeConnected == true ? "Wechat bridge connected" : "Wechat bridge disconnected";

    public string WechatWorkerProcessText =>
        _lastWechatWorkerProcessId is int workerPid ? $"Wechat worker PID {workerPid}" : "Wechat worker not running";

    public string LatestQqLlmSummaryText =>
        FormatLlmRequestSummary(_lastQqLlmRequest, "No QQ requests captured yet");

    public string LatestQqLlmDetailText =>
        FormatLlmRequestDetail(_lastQqLlmRequest);

    public string LatestQqActivitySummaryText =>
        FormatActivitySummary(_lastQqLlmRequest, _lastQqLlmFailure, "No QQ activity captured yet");

    public string LatestQqRecentActivityText =>
        FormatRecentActivity(QqRecentActivities, "No recent QQ activity yet");

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
                EnsureSelectedActivityVisible(
                    QqRecentActivitiesView,
                    () => SelectedQqRecentActivity,
                    (item) => SelectedQqRecentActivity = item);
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
        FormatActivityState(_lastQqLlmRequest, _lastQqLlmFailure);

    public string LatestQqLatestSuccessText =>
        FormatLatestSuccess(_lastQqLlmRequest);

    public string LatestQqLatestFailureText =>
        FormatLatestFailure(_lastQqLlmFailure);

    public string LatestQqRecoveryText =>
        FormatRecoveryState(_lastQqLlmRequest, _lastQqLlmFailure);

    public string LatestQqRequestTimelineText =>
        FormatRequestTimeline(_lastQqLlmRequest);

    public string LatestQqDecisionTriggerText =>
        FormatLlmDecisionTrigger(_lastQqLlmRequest);

    public string LatestQqDecisionCapabilityText =>
        FormatLlmDecisionCapability(_lastQqLlmRequest);

    public string LatestQqDecisionUpgradeText =>
        FormatLlmDecisionUpgrade(_lastQqLlmRequest);

    public string LatestQqRequestedCapabilitiesText =>
        FormatLlmRequestedCapabilities(_lastQqLlmRequest);

    public string LatestWechatLlmSummaryText =>
        FormatLlmRequestSummary(_lastWechatLlmRequest, "No Wechat requests captured yet");

    public string LatestWechatLlmDetailText =>
        FormatLlmRequestDetail(_lastWechatLlmRequest);

    public string LatestWechatActivitySummaryText =>
        FormatActivitySummary(_lastWechatLlmRequest, _lastWechatLlmFailure, "No Wechat activity captured yet");

    public string LatestWechatRecentActivityText =>
        FormatRecentActivity(WechatRecentActivities, "No recent Wechat activity yet");

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
                EnsureSelectedActivityVisible(
                    WechatRecentActivitiesView,
                    () => SelectedWechatRecentActivity,
                    (item) => SelectedWechatRecentActivity = item);
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
        FormatActivityState(_lastWechatLlmRequest, _lastWechatLlmFailure);

    public string LatestWechatLatestSuccessText =>
        FormatLatestSuccess(_lastWechatLlmRequest);

    public string LatestWechatLatestFailureText =>
        FormatLatestFailure(_lastWechatLlmFailure);

    public string LatestWechatRecoveryText =>
        FormatRecoveryState(_lastWechatLlmRequest, _lastWechatLlmFailure);

    public string LatestWechatRequestTimelineText =>
        FormatRequestTimeline(_lastWechatLlmRequest);

    public string LatestWechatDecisionTriggerText =>
        FormatLlmDecisionTrigger(_lastWechatLlmRequest);

    public string LatestWechatDecisionCapabilityText =>
        FormatLlmDecisionCapability(_lastWechatLlmRequest);

    public string LatestWechatDecisionUpgradeText =>
        FormatLlmDecisionUpgrade(_lastWechatLlmRequest);

    public string LatestWechatRequestedCapabilitiesText =>
        FormatLlmRequestedCapabilities(_lastWechatLlmRequest);

    public string LatestQqFailureSummaryText =>
        FormatLlmFailureSummary(_lastQqLlmFailure, "No QQ failures captured yet");

    public string LatestQqFailureTimelineText =>
        FormatFailureTimeline(_lastQqLlmFailure);

    public string LatestQqFailureTriggerText =>
        FormatLlmFailureTrigger(_lastQqLlmFailure);

    public string LatestQqFailureCapabilityText =>
        FormatLlmFailureCapability(_lastQqLlmFailure);

    public string LatestQqFailureUpgradeText =>
        FormatLlmFailureUpgrade(_lastQqLlmFailure);

    public string LatestQqFailureErrorText =>
        FormatLlmFailureError(_lastQqLlmFailure);

    public string LatestWechatFailureSummaryText =>
        FormatLlmFailureSummary(_lastWechatLlmFailure, "No Wechat failures captured yet");

    public string LatestWechatFailureTimelineText =>
        FormatFailureTimeline(_lastWechatLlmFailure);

    public string LatestWechatFailureTriggerText =>
        FormatLlmFailureTrigger(_lastWechatLlmFailure);

    public string LatestWechatFailureCapabilityText =>
        FormatLlmFailureCapability(_lastWechatLlmFailure);

    public string LatestWechatFailureUpgradeText =>
        FormatLlmFailureUpgrade(_lastWechatLlmFailure);

    public string LatestWechatFailureErrorText =>
        FormatLlmFailureError(_lastWechatLlmFailure);

    public bool IsControlApiReachable => _lastControlApiReachable == true;

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
            StatusText = "Backend root is invalid";
            AddLog("Cannot load config because backend root is invalid.");
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
            StatusText = apiConfig?.RestartRequired == true
                ? "Config loaded (restart required)"
                : "Config loaded";
            AddLog(apiConfig is not null
                ? $"Loaded config via control API: {apiConfig.EnvPath}"
                : $"Loaded config from file: {EnvFilePath}");
        }
        catch (Exception ex)
        {
            StatusText = "Load failed";
            AddLog($"Load config failed: {ex.Message}");
            System.Windows.MessageBox.Show($"Load config failed:\n{ex.Message}", "Load failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _suspendDirtyTracking = false;
            UpdateCommandStates();
        }
    }

    private async Task<(BackendControlConfigResponse? ApiConfig, BackendRuntimeStatus? ApiStatus)> LoadConfigFromAuthoritativeSourceAsync()
    {
        var apiConfig = await _backendControlApiService.TryGetConfigAsync();
        var configFailure = _backendControlApiService.LastFailure;
        var apiStatus = await _backendControlApiService.TryGetStatusAsync();

        if (apiConfig is not null)
        {
            return (apiConfig, apiStatus);
        }

        if (IsImmediateControlApiFailure(configFailure))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(configFailure.Message)
                    ? "Control API rejected the config request."
                    : configFailure.Message);
        }

        if (configFailure.Kind == BackendControlApiFailureKind.Unreachable)
        {
            await TryRecoverControlApiAsync("load-config");
            apiConfig = await _backendControlApiService.TryGetConfigAsync();
            apiStatus = await _backendControlApiService.TryGetStatusAsync();

            if (apiConfig is not null)
            {
                return (apiConfig, apiStatus);
            }
        }

        return (null, apiStatus);
    }

    private async Task SaveConfigAsync()
    {
        await SaveConfigAsync(showUiErrors: true);
    }

    private async Task<bool> SaveConfigAsync(bool showUiErrors)
    {
        if (!IsBackendRootValid)
        {
            StatusText = "Backend root is invalid";
            AddLog("Cannot save config because backend root is invalid.");
            return false;
        }

        try
        {
            StatusText = "Saving config...";
            _envDocument.Config = BuildConfig();
            var apiResult = await SaveConfigThroughControlApiAsync(_envDocument.Config);
            _envDocument.Config = BuildConfigCopy(apiResult);
            ApplyConfigToView(_envDocument.Config);
            StatusText = apiResult.RestartRequired ? "Config saved (restart required)" : "Config saved";
            AddLog($"Saved config via control API: {apiResult.EnvPath}");

            LastSavedAtText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            HasUnsavedChanges = false;
            return true;
        }
        catch (Exception ex)
        {
            StatusText = "Save failed";
            AddLog($"Save config failed: {ex.Message}");

            if (showUiErrors)
            {
                System.Windows.MessageBox.Show($"Save config failed:\n{ex.Message}", "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }

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
            await LoadLocalEnvDocumentAsync(suppressErrors: true);

            var existingStatus = await _backendControlApiService.TryGetStatusAsync();
            if (existingStatus is not null)
            {
                var attachStatus = await _backendControlApiService.TryStartAsync() ?? existingStatus;
                ApplyBackendRuntimeStatus(attachStatus, true);
                _botProcessService.Detach();
                StatusText = IsProcessRunning ? "Backend is running" : "Backend start command was ignored";
                AddLog($"Attached to existing backend host: {attachStatus.ControlApiUrl}");
                await LoadConfigAsync();
                return;
            }

            var existingStatusFailure = _backendControlApiService.LastFailure;
            if (IsImmediateControlApiFailure(existingStatusFailure))
            {
                throw new InvalidOperationException(existingStatusFailure.Message);
            }

            _botProcessService.Start(BackendRootPath);

            var status = await WaitForBackendControlStatusAsync();
            if (status is null)
            {
                var waitFailure = _backendControlApiService.LastFailure;
                if (IsImmediateControlApiFailure(waitFailure))
                {
                    throw new InvalidOperationException(waitFailure.Message);
                }

                StatusText = "Backend started, waiting for control API";
                return;
            }

            var startedStatus = await _backendControlApiService.TryStartAsync();
            ApplyBackendRuntimeStatus(startedStatus, startedStatus is not null);
            StatusText = IsProcessRunning ? "Backend is running" : "Backend start command was ignored";

            if (startedStatus is not null)
            {
                _botProcessService.Detach();
                AddLog($"Control API ready: {startedStatus.ControlApiUrl}");
                await LoadConfigAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = "Start failed";
            AddLog($"Start backend failed: {ex.Message}");
            System.Windows.MessageBox.Show($"Start backend failed:\n{ex.Message}", "Start failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
            await LoadLocalEnvDocumentAsync(suppressErrors: true);
            var stoppedStatus = await _backendControlApiService.TryStopAsync();

            if (stoppedStatus is not null)
            {
                ApplyBackendRuntimeStatus(stoppedStatus, true);
                StatusText = stoppedStatus.RuntimeActive ? "Backend is still running" : "Backend stopped";
                AddLog($"Sent stop command via control API: {stoppedStatus.ControlApiUrl}");
                NotificationRequested?.Invoke(
                    this,
                    new TrayNotification
                    {
                        Title = "QQ AI Bot",
                        Message = stoppedStatus.RuntimeActive
                            ? "Stop command was ignored because runtime is still active."
                            : "Runtime stopped by user.",
                        Icon = stoppedStatus.RuntimeActive
                            ? System.Windows.Forms.ToolTipIcon.Warning
                            : System.Windows.Forms.ToolTipIcon.Info
                    }
                );
                return;
            }

            var stopFailure = _backendControlApiService.LastFailure;
            if (IsImmediateControlApiFailure(stopFailure))
            {
                throw new InvalidOperationException(stopFailure.Message);
            }

            if (_botProcessService.IsRunning)
            {
                await _botProcessService.StopAsync();
                ApplyBackendRuntimeStatus(null, false);
                StatusText = "Backend host stopped";
                NotificationRequested?.Invoke(
                    this,
                    new TrayNotification
                    {
                        Title = "QQ AI Bot",
                        Message = "Backend host stopped by user.",
                        Icon = System.Windows.Forms.ToolTipIcon.Info
                    }
                );
            }
            else
            {
                ApplyBackendRuntimeStatus(null, false);
                StatusText = "Backend is not reachable";
                AddLog("Control API is unavailable and no local backend host process is attached.");
            }
        }
        catch (Exception ex)
        {
            StatusText = "Stop failed";
            AddLog($"Stop backend failed: {ex.Message}");
            System.Windows.MessageBox.Show($"Stop backend failed:\n{ex.Message}", "Stop failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
            BotPersona = config.BotPersona,
            MaxOutputChars = config.MaxOutputChars,
            AllowedChatIds = config.AllowedChatIds,
            AllowedUserIds = config.AllowedUserIds
        };
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
        BotPersona = config.BotPersona;
        MaxOutputChars = config.MaxOutputChars;
        AllowedChatIds = config.AllowedChatIds;
        AllowedUserIds = config.AllowedUserIds;
    }

    private async Task<BackendRuntimeStatus?> WaitForBackendControlStatusAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var status = await _backendControlApiService.TryGetStatusAsync();

            if (status is not null)
            {
                return status;
            }

            if (_backendControlApiService.LastFailure.Kind != BackendControlApiFailureKind.Unreachable)
            {
                return null;
            }

            await Task.Delay(300);
        }

        return null;
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

        if (status is null)
        {
            if (statusFailure.Kind != BackendControlApiFailureKind.Unreachable)
            {
                _lastControlApiReachable = true;
                OnPropertyChanged(nameof(IsControlApiReachable));

                if (statusFailure.Kind == BackendControlApiFailureKind.Unauthorized &&
                    !_controlApiUnauthorizedNotified)
                {
                    AddLog("Control API authentication failed. Update QQ_AI_BOT_CONTROL_API_TOKEN in the local .env.");
                    _controlApiUnauthorizedNotified = true;
                }

                return;
            }

            _controlApiUnauthorizedNotified = false;
            _consecutiveControlApiFailures += 1;
            _lastControlApiReachable = false;
            OnPropertyChanged(nameof(IsControlApiReachable));
            _lastWechatBridgeConnected = false;
            NotifyRuntimeSnapshotChanged();

            if (_consecutiveControlApiFailures == ControlApiRecoveryAttemptThreshold)
            {
                _ = TryRecoverControlApiAsync("status-poll");
            }

            if (!_controlApiOutageNotified &&
                _consecutiveControlApiFailures >= ControlApiOutageNotificationThreshold)
            {
                AddLog("Control API became unreachable.");
                NotificationRequested?.Invoke(
                    this,
                    new TrayNotification
                    {
                        Title = "QQ AI Bot",
                        Message = "Control API is unreachable. The supervisor may be stopped or restarting.",
                        Icon = System.Windows.Forms.ToolTipIcon.Warning
                    }
                );
                _controlApiOutageNotified = true;
            }

            return;
        }

        _controlApiUnauthorizedNotified = false;
        if (_lastControlApiReachable == false && _controlApiOutageNotified)
        {
            AddLog("Control API became reachable again.");
            NotificationRequested?.Invoke(
                this,
                new TrayNotification
                {
                    Title = "QQ AI Bot",
                    Message = "Control API is reachable again.",
                    Icon = System.Windows.Forms.ToolTipIcon.Info
                }
            );
        }

        _lastControlApiReachable = true;
        IsProcessRunning = status.RuntimeActive;

        if (_lastWorkerProcessId is int previousWorkerPid &&
            status.WorkerProcessId is int currentWorkerPid &&
            previousWorkerPid != currentWorkerPid)
        {
            AddLog($"Worker restarted: {previousWorkerPid} -> {currentWorkerPid}");
            NotificationRequested?.Invoke(
                this,
                new TrayNotification
                {
                    Title = "QQ AI Bot",
                    Message = $"Worker restarted automatically ({previousWorkerPid} -> {currentWorkerPid}).",
                    Icon = System.Windows.Forms.ToolTipIcon.Info
                }
            );
        }

        if (_lastWechatWorkerProcessId is int previousWechatWorkerPid &&
            status.WechatWorkerProcessId is int currentWechatWorkerPid &&
            previousWechatWorkerPid != currentWechatWorkerPid)
        {
            AddLog($"Wechat worker restarted: {previousWechatWorkerPid} -> {currentWechatWorkerPid}");
            NotificationRequested?.Invoke(
                this,
                new TrayNotification
                {
                    Title = "QQ AI Bot",
                    Message = $"Wechat worker restarted automatically ({previousWechatWorkerPid} -> {currentWechatWorkerPid}).",
                    Icon = System.Windows.Forms.ToolTipIcon.Info
                }
            );
        }

        if (_lastRuntimeActive == true && !status.RuntimeActive)
        {
            AddLog("Runtime became inactive.");
            NotificationRequested?.Invoke(
                this,
                new TrayNotification
                {
                    Title = "QQ AI Bot",
                    Message = "Runtime is stopped.",
                    Icon = System.Windows.Forms.ToolTipIcon.Warning
                }
            );
        }

        ApplyBackendRuntimeStatus(status, true);
        ResetControlApiFailureState();
    }

    private void NotifyRuntimeSnapshotChanged()
    {
        OnPropertyChanged(nameof(WechatRuntimeStateText));
        OnPropertyChanged(nameof(RuntimeReadyText));
        OnPropertyChanged(nameof(WechatRuntimeReadyText));
        OnPropertyChanged(nameof(WechatBridgeStateText));
        OnPropertyChanged(nameof(WechatWorkerProcessText));
        OnPropertyChanged(nameof(ShowOnlyQqFailures));
        OnPropertyChanged(nameof(ShowOnlyWechatFailures));
        OnPropertyChanged(nameof(PinSelectedQqActivity));
        OnPropertyChanged(nameof(PinSelectedWechatActivity));
        OnPropertyChanged(nameof(LatestQqActivitySummaryText));
        OnPropertyChanged(nameof(LatestQqRecentActivityText));
        OnPropertyChanged(nameof(SelectedQqRecentActivity));
        OnPropertyChanged(nameof(SelectedQqRecentActivitySummaryText));
        OnPropertyChanged(nameof(SelectedQqRecentActivityMetaText));
        OnPropertyChanged(nameof(SelectedQqRecentActivityDetailText));
        OnPropertyChanged(nameof(LatestQqActivityStateText));
        OnPropertyChanged(nameof(LatestQqLatestSuccessText));
        OnPropertyChanged(nameof(LatestQqLatestFailureText));
        OnPropertyChanged(nameof(LatestQqRecoveryText));
        OnPropertyChanged(nameof(LatestQqLlmSummaryText));
        OnPropertyChanged(nameof(LatestQqLlmDetailText));
        OnPropertyChanged(nameof(LatestQqRequestTimelineText));
        OnPropertyChanged(nameof(LatestQqDecisionTriggerText));
        OnPropertyChanged(nameof(LatestQqDecisionCapabilityText));
        OnPropertyChanged(nameof(LatestQqDecisionUpgradeText));
        OnPropertyChanged(nameof(LatestQqRequestedCapabilitiesText));
        OnPropertyChanged(nameof(LatestQqFailureSummaryText));
        OnPropertyChanged(nameof(LatestQqFailureTimelineText));
        OnPropertyChanged(nameof(LatestQqFailureTriggerText));
        OnPropertyChanged(nameof(LatestQqFailureCapabilityText));
        OnPropertyChanged(nameof(LatestQqFailureUpgradeText));
        OnPropertyChanged(nameof(LatestQqFailureErrorText));
        OnPropertyChanged(nameof(LatestWechatActivitySummaryText));
        OnPropertyChanged(nameof(LatestWechatRecentActivityText));
        OnPropertyChanged(nameof(SelectedWechatRecentActivity));
        OnPropertyChanged(nameof(SelectedWechatRecentActivitySummaryText));
        OnPropertyChanged(nameof(SelectedWechatRecentActivityMetaText));
        OnPropertyChanged(nameof(SelectedWechatRecentActivityDetailText));
        OnPropertyChanged(nameof(LatestWechatActivityStateText));
        OnPropertyChanged(nameof(LatestWechatLatestSuccessText));
        OnPropertyChanged(nameof(LatestWechatLatestFailureText));
        OnPropertyChanged(nameof(LatestWechatRecoveryText));
        OnPropertyChanged(nameof(LatestWechatLlmSummaryText));
        OnPropertyChanged(nameof(LatestWechatLlmDetailText));
        OnPropertyChanged(nameof(LatestWechatRequestTimelineText));
        OnPropertyChanged(nameof(LatestWechatDecisionTriggerText));
        OnPropertyChanged(nameof(LatestWechatDecisionCapabilityText));
        OnPropertyChanged(nameof(LatestWechatDecisionUpgradeText));
        OnPropertyChanged(nameof(LatestWechatRequestedCapabilitiesText));
        OnPropertyChanged(nameof(LatestWechatFailureSummaryText));
        OnPropertyChanged(nameof(LatestWechatFailureTimelineText));
        OnPropertyChanged(nameof(LatestWechatFailureTriggerText));
        OnPropertyChanged(nameof(LatestWechatFailureCapabilityText));
        OnPropertyChanged(nameof(LatestWechatFailureUpgradeText));
        OnPropertyChanged(nameof(LatestWechatFailureErrorText));
    }

    private void ApplyBackendRuntimeStatus(BackendRuntimeStatus? status, bool controlApiReachable)
    {
        TrackRecentActivity(
            status?.LastQqLlmRequest,
            status?.LastQqLlmFailure,
            QqRecentActivities,
            ref _lastQqRequestEventKey,
            ref _lastQqFailureEventKey,
            () => PinSelectedQqActivity,
            () => SelectedQqRecentActivity,
            (item) => SelectedQqRecentActivity = item);
        TrackRecentActivity(
            status?.LastWechatLlmRequest,
            status?.LastWechatLlmFailure,
            WechatRecentActivities,
            ref _lastWechatRequestEventKey,
            ref _lastWechatFailureEventKey,
            () => PinSelectedWechatActivity,
            () => SelectedWechatRecentActivity,
            (item) => SelectedWechatRecentActivity = item);
        IsProcessRunning = status?.RuntimeActive == true;
        _lastWorkerProcessId = status?.WorkerProcessId;
        _lastWechatWorkerProcessId = status?.WechatWorkerProcessId;
        _lastRuntimeActive = status?.RuntimeActive;
        _lastRuntimeReady = status?.RuntimeReady;
        _lastWechatConfigured = status?.WechatConfigured;
        _lastWechatRuntimeActive = status?.WechatRuntimeActive;
        _lastWechatRuntimeReady = status?.WechatRuntimeReady;
        _lastWechatBridgeConnected = status?.WechatBridgeConnected;
        _lastQqLlmRequest = status?.LastQqLlmRequest;
        _lastQqLlmFailure = status?.LastQqLlmFailure;
        _lastWechatLlmRequest = status?.LastWechatLlmRequest;
        _lastWechatLlmFailure = status?.LastWechatLlmFailure;
        _lastControlApiReachable = controlApiReachable;
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
        var normalizedState = _activityStatePolicy.Normalize(state) ?? _activityStatePolicy.CreateDefaultState();

        _restoringActivityState = true;
        try
        {
            ReplaceRecentActivities(QqRecentActivities, normalizedState.QqRecentActivities);
            ReplaceRecentActivities(WechatRecentActivities, normalizedState.WechatRecentActivities);
            _pinSelectedQqActivity = normalizedState.PinSelectedQqActivity;
            _pinSelectedWechatActivity = normalizedState.PinSelectedWechatActivity;
            _showOnlyQqFailures = normalizedState.ShowOnlyQqFailures;
            _showOnlyWechatFailures = normalizedState.ShowOnlyWechatFailures;
            _selectedQqRecentActivity = _activityStatePolicy.ResolveSelectedItem(
                QqRecentActivities,
                normalizedState.SelectedQqEventKey);
            _selectedWechatRecentActivity = _activityStatePolicy.ResolveSelectedItem(
                WechatRecentActivities,
                normalizedState.SelectedWechatEventKey);
        }
        finally
        {
            _restoringActivityState = false;
        }

        QqRecentActivitiesView.Refresh();
        WechatRecentActivitiesView.Refresh();
        EnsureSelectedActivityVisible(
            QqRecentActivitiesView,
            () => SelectedQqRecentActivity,
            (item) => SelectedQqRecentActivity = item);
        EnsureSelectedActivityVisible(
            WechatRecentActivitiesView,
            () => SelectedWechatRecentActivity,
            (item) => SelectedWechatRecentActivity = item);
        NotifyRuntimeSnapshotChanged();
    }

    private static void ReplaceRecentActivities(
        ObservableCollection<BackendRecentActivityItem> target,
        IEnumerable<BackendRecentActivityItem>? source)
    {
        target.Clear();

        if (source is null)
        {
            return;
        }

        foreach (var item in source)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.EventKey))
            {
                continue;
            }

            target.Add(item);
        }
    }

    private void ResetControlApiFailureState()
    {
        _consecutiveControlApiFailures = 0;
        _controlApiOutageNotified = false;
        _controlApiUnauthorizedNotified = false;
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
            await LoadLocalEnvDocumentAsync(suppressErrors: true);
            AddLog($"Control API unreachable; attempting backend recovery ({reason}).");

            var existingStatus = await _backendControlApiService.TryGetStatusAsync();
            if (existingStatus is not null)
            {
                ApplyBackendRuntimeStatus(existingStatus, true);
                ResetControlApiFailureState();
                AddLog("Control API recovered before local restart was needed.");
                return;
            }

            var existingStatusFailure = _backendControlApiService.LastFailure;
            if (IsImmediateControlApiFailure(existingStatusFailure))
            {
                AddLog($"Control API recovery aborted: {existingStatusFailure.Message}");
                return;
            }

            if (!_botProcessService.IsRunning)
            {
                _botProcessService.Start(BackendRootPath);
                AddLog("Started local backend host for control API recovery.");
            }

            var startStatus = await _backendControlApiService.TryStartAsync();
            if (startStatus is not null)
            {
                ApplyBackendRuntimeStatus(startStatus, true);
                _botProcessService.Detach();
                ResetControlApiFailureState();
                AddLog($"Control API recovery succeeded: {startStatus.ControlApiUrl}");
                return;
            }

            var startFailure = _backendControlApiService.LastFailure;
            if (IsImmediateControlApiFailure(startFailure))
            {
                AddLog($"Control API recovery aborted: {startFailure.Message}");
                return;
            }

            var recoveredStatus = await WaitForBackendControlStatusAsync();
            if (recoveredStatus is not null)
            {
                ApplyBackendRuntimeStatus(recoveredStatus, true);
                _botProcessService.Detach();
                ResetControlApiFailureState();
                AddLog($"Control API recovery succeeded: {recoveredStatus.ControlApiUrl}");
                return;
            }

            AddLog("Control API recovery attempt did not restore connectivity.");
        }
        catch (Exception ex)
        {
            AddLog($"Control API recovery failed: {ex.Message}");
        }
        finally
        {
            _controlApiRecoveryInProgress = false;
        }
    }

    private async Task<BackendControlConfigResponse> SaveConfigThroughControlApiAsync(BotConfig config)
    {
        await LoadLocalEnvDocumentAsync(suppressErrors: true);
        var apiResult = await _backendControlApiService.TrySaveConfigAsync(config);

        if (apiResult is not null)
        {
            return apiResult;
        }

        var initialFailure = _backendControlApiService.LastFailure;

        if (IsImmediateControlApiFailure(initialFailure))
        {
            throw new InvalidOperationException(initialFailure.Message);
        }

        var currentStatus = await _backendControlApiService.TryGetStatusAsync();

        if (currentStatus is null)
        {
            var currentStatusFailure = _backendControlApiService.LastFailure;
            if (IsImmediateControlApiFailure(currentStatusFailure))
            {
                throw new InvalidOperationException(currentStatusFailure.Message);
            }

            await TryRecoverControlApiAsync("save-config");
            apiResult = await _backendControlApiService.TrySaveConfigAsync(config);

            if (apiResult is not null)
            {
                return apiResult;
            }
        }

        var finalFailure = _backendControlApiService.LastFailure;
        var failureMessage = string.IsNullOrWhiteSpace(finalFailure.Message)
            ? "Control API is unavailable or rejected the config update."
            : finalFailure.Message;

        throw new InvalidOperationException(failureMessage);
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

    private static string FormatLlmRequestSummary(BackendLlmRequestStatus? request, string emptyText)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route) || string.IsNullOrWhiteSpace(request.Model))
        {
            return emptyText;
        }

        var apiStyle = string.IsNullOrWhiteSpace(request.EffectiveApiStyle) ? "unknown" : request.EffectiveApiStyle;
        return $"{request.Route} / {request.Model} / {apiStyle}";
    }

    private static string FormatActivitySummary(
        BackendLlmRequestStatus? request,
        BackendLlmFailureStatus? failure,
        string emptyText)
    {
        if (request is null && failure is null)
        {
            return emptyText;
        }

        var requestCapturedAt = ParseCapturedAt(request?.CapturedAt);
        var failureCapturedAt = ParseCapturedAt(failure?.CapturedAt);

        if (failureCapturedAt is not null &&
            (requestCapturedAt is null || failureCapturedAt >= requestCapturedAt))
        {
            return $"Latest event: failure at {FormatCapturedAt(failure!.CapturedAt)}";
        }

        if (requestCapturedAt is not null)
        {
            return $"Latest event: request at {FormatCapturedAt(request!.CapturedAt)}";
        }

        return emptyText;
    }

    private static string FormatRecentActivity(IEnumerable<BackendRecentActivityItem> items, string emptyText)
    {
        var visibleLines = items
            .Where(static item => item is not null && !string.IsNullOrWhiteSpace(item.Summary))
            .Select(static item => $"{item.EventType} | {item.Summary}")
            .ToArray();
        return visibleLines.Length > 0 ? string.Join(Environment.NewLine, visibleLines) : emptyText;
    }

    private bool FilterQqRecentActivity(object item)
    {
        return FilterRecentActivity(item, ShowOnlyQqFailures);
    }

    private bool FilterWechatRecentActivity(object item)
    {
        return FilterRecentActivity(item, ShowOnlyWechatFailures);
    }

    private static bool FilterRecentActivity(object item, bool failuresOnly)
    {
        if (item is not BackendRecentActivityItem activityItem)
        {
            return false;
        }

        return !failuresOnly || activityItem.IsFailure;
    }

    private static void EnsureSelectedActivityVisible(
        ICollectionView activityView,
        Func<BackendRecentActivityItem?> getSelectedItem,
        Action<BackendRecentActivityItem?> setSelectedItem)
    {
        var selectedItem = getSelectedItem();

        if (selectedItem is not null && activityView.Cast<object>().Contains(selectedItem))
        {
            return;
        }

        setSelectedItem(activityView.Cast<BackendRecentActivityItem>().FirstOrDefault());
    }

    private static string FormatActivityState(
        BackendLlmRequestStatus? request,
        BackendLlmFailureStatus? failure)
    {
        if (request is null && failure is null)
        {
            return "No activity";
        }

        var requestCapturedAt = ParseCapturedAt(request?.CapturedAt);
        var failureCapturedAt = ParseCapturedAt(failure?.CapturedAt);

        if (requestCapturedAt is not null && failureCapturedAt is not null)
        {
            if (requestCapturedAt > failureCapturedAt)
            {
                return "Recovered after failure";
            }

            if (failureCapturedAt > requestCapturedAt)
            {
                return "Failure is latest event";
            }

            return "Request and failure captured";
        }

        if (requestCapturedAt is not null)
        {
            return "Latest event is successful request";
        }

        return "Latest event is failure";
    }

    private static string FormatLatestSuccess(BackendLlmRequestStatus? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route))
        {
            return "Success | not captured";
        }

        return $"Success | {FormatCapturedAt(request.CapturedAt)}";
    }

    private static string FormatLatestFailure(BackendLlmFailureStatus? failure)
    {
        if (failure is null || string.IsNullOrWhiteSpace(failure.Route))
        {
            return "Failure | not captured";
        }

        return $"Failure | {FormatCapturedAt(failure.CapturedAt)}";
    }

    private static string FormatRecoveryState(
        BackendLlmRequestStatus? request,
        BackendLlmFailureStatus? failure)
    {
        var requestCapturedAt = ParseCapturedAt(request?.CapturedAt);
        var failureCapturedAt = ParseCapturedAt(failure?.CapturedAt);

        if (requestCapturedAt is null || failureCapturedAt is null)
        {
            return "Recovery | not observed";
        }

        if (requestCapturedAt > failureCapturedAt)
        {
            return $"Recovery | {FormatCapturedAt(request!.CapturedAt)}";
        }

        return "Recovery | pending";
    }

    private static string FormatRequestTimeline(BackendLlmRequestStatus? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route))
        {
            return "Request | n/a | not captured";
        }

        return $"Request | {FormatCapturedAt(request.CapturedAt)} | completed";
    }

    private static string FormatLlmRequestDetail(BackendLlmRequestStatus? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route))
        {
            return "No completed requests captured yet.";
        }

        var capturedAt = FormatCapturedAt(request.CapturedAt);
        var reasoning = string.IsNullOrWhiteSpace(request.EffectiveReasoningEffort) ? "none" : request.EffectiveReasoningEffort;
        var verbosity = string.IsNullOrWhiteSpace(request.EffectiveTextVerbosity) ? "none" : request.EffectiveTextVerbosity;
        var tools = request.EffectiveTools is { Length: > 0 }
            ? string.Join("+", request.EffectiveTools)
            : "none";
        var decisionSummary = FormatDecisionSummary(request);

        return $"At {capturedAt} | {decisionSummary} | reasoning={reasoning} | verbosity={verbosity} | tools={tools} | images={request.ImageCount}";
    }

    private static string FormatDecisionSummary(BackendLlmRequestStatus request)
    {
        var summary = request.DecisionSummary;

        if (summary is null)
        {
            var routeReason = string.IsNullOrWhiteSpace(request.RouteReason) ? "default" : request.RouteReason;
            return $"reason={routeReason}";
        }

        var trigger = FormatDecisionTrigger(summary);
        var capabilityReasons = FormatDecisionReasonGroup(summary.ReasonGroups?.CapabilityReasons, "default");
        var upgradeReasons = FormatDecisionReasonGroup(summary.ReasonGroups?.UpgradeReasons, "none");

        return $"trigger={trigger} | capability={capabilityReasons} | upgrade={upgradeReasons}";
    }

    private static string FormatDecisionTrigger(BackendDecisionSummary summary)
    {
        var triggerKind = string.IsNullOrWhiteSpace(summary.Trigger?.Kind)
            ? "default"
            : summary.Trigger.Kind;
        var matchedPrefix = string.IsNullOrWhiteSpace(summary.Trigger?.MatchedPrefix)
            ? summary.MatchedPrefix
            : summary.Trigger!.MatchedPrefix;

        return triggerKind == "directive" && !string.IsNullOrWhiteSpace(matchedPrefix)
            ? $"directive:{matchedPrefix}"
            : triggerKind;
    }

    private static string FormatDecisionReasonGroup(string[]? reasons, string emptyValue)
    {
        return reasons is { Length: > 0 }
            ? string.Join("+", reasons)
            : emptyValue;
    }

    private static string FormatLlmFailureSummary(BackendLlmFailureStatus? failure, string emptyText)
    {
        if (failure is null || string.IsNullOrWhiteSpace(failure.Route))
        {
            return emptyText;
        }

        return $"{failure.Route} / {FormatCapturedAt(failure.CapturedAt)}";
    }

    private static string FormatFailureTimeline(BackendLlmFailureStatus? failure)
    {
        if (failure is null || string.IsNullOrWhiteSpace(failure.Route))
        {
            return "Failure | n/a | not captured";
        }

        return $"Failure | {FormatCapturedAt(failure.CapturedAt)} | failed";
    }

    private static string FormatLlmFailureTrigger(BackendLlmFailureStatus? failure)
    {
        if (failure is null || string.IsNullOrWhiteSpace(failure.Route))
        {
            return "n/a";
        }

        var summary = failure.DecisionSummary;

        if (summary is not null)
        {
            return FormatDecisionTrigger(summary);
        }

        if (!string.IsNullOrWhiteSpace(failure.MatchedPrefix))
        {
            return $"directive:{failure.MatchedPrefix}";
        }

        return "default";
    }

    private static string FormatLlmFailureCapability(BackendLlmFailureStatus? failure)
    {
        if (failure is null || string.IsNullOrWhiteSpace(failure.Route))
        {
            return "n/a";
        }

        return FormatDecisionReasonGroup(failure.DecisionSummary?.ReasonGroups?.CapabilityReasons, "default");
    }

    private static string FormatLlmFailureUpgrade(BackendLlmFailureStatus? failure)
    {
        if (failure is null || string.IsNullOrWhiteSpace(failure.Route))
        {
            return "n/a";
        }

        return FormatDecisionReasonGroup(failure.DecisionSummary?.ReasonGroups?.UpgradeReasons, "none");
    }

    private static string FormatLlmFailureError(BackendLlmFailureStatus? failure)
    {
        if (failure is null || string.IsNullOrWhiteSpace(failure.Route))
        {
            return "n/a";
        }

        return string.IsNullOrWhiteSpace(failure.Error) ? "unknown" : failure.Error;
    }

    private static string FormatLlmDecisionTrigger(BackendLlmRequestStatus? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route))
        {
            return "n/a";
        }

        var summary = request.DecisionSummary;

        if (summary is not null)
        {
            return FormatDecisionTrigger(summary);
        }

        if (!string.IsNullOrWhiteSpace(request.MatchedPrefix))
        {
            return $"directive:{request.MatchedPrefix}";
        }

        return "default";
    }

    private static string FormatLlmDecisionCapability(BackendLlmRequestStatus? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route))
        {
            return "n/a";
        }

        return FormatDecisionReasonGroup(request.DecisionSummary?.ReasonGroups?.CapabilityReasons, "default");
    }

    private static string FormatLlmDecisionUpgrade(BackendLlmRequestStatus? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route))
        {
            return "n/a";
        }

        return FormatDecisionReasonGroup(request.DecisionSummary?.ReasonGroups?.UpgradeReasons, "none");
    }

    private static string FormatLlmRequestedCapabilities(BackendLlmRequestStatus? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route))
        {
            return "n/a";
        }

        var requested = request.DecisionSummary?.RequestedCapabilities;

        if (requested is not null)
        {
            return $"reasoning={DefaultIfBlank(requested.ReasoningEffort, "none")} | verbosity={DefaultIfBlank(requested.TextVerbosity, "none")} | web={(requested.EnableWebSearch ? "on" : "off")} | code={(requested.EnableCodeInterpreter ? "on" : "off")}";
        }

        return $"reasoning={DefaultIfBlank(request.EffectiveReasoningEffort, "none")} | verbosity={DefaultIfBlank(request.EffectiveTextVerbosity, "none")} | web={(request.EffectiveTools?.Contains("web_search") == true ? "on" : "off")} | code={(request.EffectiveTools?.Contains("code_interpreter") == true ? "on" : "off")}";
    }

    private static string DefaultIfBlank(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string BuildEffectiveBotInstructions(string? botPersona)
    {
        var normalizedPersona = string.IsNullOrWhiteSpace(botPersona) ? string.Empty : botPersona.Trim();

        if (string.IsNullOrWhiteSpace(normalizedPersona))
        {
            return DefaultBotInstructionsTextValue;
        }

        return $"{DefaultBotInstructionsTextValue}{Environment.NewLine}{Environment.NewLine}附加人格设定:{Environment.NewLine}{normalizedPersona}";
    }

    private static void TrackRecentActivity(
        BackendLlmRequestStatus? request,
        BackendLlmFailureStatus? failure,
        ObservableCollection<BackendRecentActivityItem> recentActivityItems,
        ref string lastRequestEventKey,
        ref string lastFailureEventKey,
        Func<bool> getIsPinned,
        Func<BackendRecentActivityItem?> getSelectedItem,
        Action<BackendRecentActivityItem?> setSelectedItem)
    {
        var entries = new List<(DateTimeOffset? CapturedAt, string Key, BackendRecentActivityItem Item, bool IsRequest)>();
        var hadSelection = getSelectedItem() is not null;
        BackendRecentActivityItem? newestInserted = null;

        if (request is not null && !string.IsNullOrWhiteSpace(request.Route))
        {
            var key = BuildRequestEventKey(request);
            if (!string.Equals(key, lastRequestEventKey, StringComparison.Ordinal))
            {
                entries.Add((ParseCapturedAt(request.CapturedAt), key, BuildRequestActivityItem(request), true));
            }
        }

        if (failure is not null && !string.IsNullOrWhiteSpace(failure.Route))
        {
            var key = BuildFailureEventKey(failure);
            if (!string.Equals(key, lastFailureEventKey, StringComparison.Ordinal))
            {
                entries.Add((ParseCapturedAt(failure.CapturedAt), key, BuildFailureActivityItem(failure), false));
            }
        }

        foreach (var entry in entries.OrderBy(static entry => entry.CapturedAt ?? DateTimeOffset.MinValue))
        {
            InsertRecentActivity(recentActivityItems, entry.Item);
            newestInserted = entry.Item;

            if (entry.IsRequest)
            {
                lastRequestEventKey = entry.Key;
            }
            else
            {
                lastFailureEventKey = entry.Key;
            }
        }

        if (!hadSelection && newestInserted is not null)
        {
            setSelectedItem(newestInserted);
        }
        else if (hadSelection && newestInserted is not null && !getIsPinned())
        {
            setSelectedItem(newestInserted);
        }

        var selectedItem = getSelectedItem();

        if (selectedItem is not null && !recentActivityItems.Contains(selectedItem))
        {
            setSelectedItem(recentActivityItems.FirstOrDefault());
        }
    }

    private static void InsertRecentActivity(
        ObservableCollection<BackendRecentActivityItem> recentActivityItems,
        BackendRecentActivityItem item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Summary))
        {
            return;
        }

        recentActivityItems.Insert(0, item);

        while (recentActivityItems.Count > 6)
        {
            recentActivityItems.RemoveAt(recentActivityItems.Count - 1);
        }
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

    private static string BuildRequestEventKey(BackendLlmRequestStatus request)
    {
        return $"request|{request.CapturedAt}|{request.Route}|{request.ResponseId}|{request.Model}";
    }

    private static string BuildFailureEventKey(BackendLlmFailureStatus failure)
    {
        return $"failure|{failure.CapturedAt}|{failure.Route}|{failure.Error}";
    }

    private static BackendRecentActivityItem BuildRequestActivityItem(BackendLlmRequestStatus request)
    {
        return new BackendRecentActivityItem
        {
            EventKey = BuildRequestEventKey(request),
            CapturedAt = request.CapturedAt,
            EventType = "Request",
            Summary = $"{request.Route} / {DefaultIfBlank(request.Model, "unknown-model")} / {DefaultIfBlank(request.EffectiveApiStyle, "unknown-api")}",
            Meta = FormatCapturedAt(request.CapturedAt),
            Detail = FormatLlmRequestDetail(request),
            IsFailure = false
        };
    }

    private static BackendRecentActivityItem BuildFailureActivityItem(BackendLlmFailureStatus failure)
    {
        return new BackendRecentActivityItem
        {
            EventKey = BuildFailureEventKey(failure),
            CapturedAt = failure.CapturedAt,
            EventType = "Failure",
            Summary = $"{failure.Route} / {DefaultIfBlank(failure.Error, "unknown")}",
            Meta = FormatCapturedAt(failure.CapturedAt),
            Detail = $"At {FormatCapturedAt(failure.CapturedAt)} | trigger={FormatLlmFailureTrigger(failure)} | capability={FormatLlmFailureCapability(failure)} | upgrade={FormatLlmFailureUpgrade(failure)} | error={FormatLlmFailureError(failure)}",
            IsFailure = true
        };
    }

    private static DateTimeOffset? ParseCapturedAt(string? capturedAt)
    {
        if (DateTimeOffset.TryParse(capturedAt, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string FormatCapturedAt(string capturedAt)
    {
        if (DateTimeOffset.TryParse(capturedAt, out var parsed))
        {
            return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        return string.IsNullOrWhiteSpace(capturedAt) ? "unknown" : capturedAt;
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
