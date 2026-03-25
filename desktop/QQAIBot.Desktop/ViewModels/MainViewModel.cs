using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using QQAIBot.Desktop.Infrastructure;
using QQAIBot.Desktop.Models;
using QQAIBot.Desktop.Services;

namespace QQAIBot.Desktop.ViewModels;

public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private const int ControlApiRecoveryAttemptThreshold = 2;
    private const int ControlApiOutageNotificationThreshold = 3;

    private readonly IAutoStartService _autoStartService;
    private readonly ILocalConfigFallbackReader _localConfigFallbackReader;
    private readonly IBackendControlApiService _backendControlApiService;
    private readonly IBotProcessService _botProcessService;
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
    private bool _controlApiRecoveryInProgress;
    private bool _disposed;
    private BackendLlmRequestStatus? _lastQqLlmRequest;
    private BackendLlmRequestStatus? _lastWechatLlmRequest;

    public event EventHandler<TrayNotification>? NotificationRequested;

    public MainViewModel(
        IAutoStartService? autoStartService = null,
        ILocalConfigFallbackReader? localConfigFallbackReader = null,
        IBackendControlApiService? backendControlApiService = null,
        IBotProcessService? botProcessService = null)
    {
        _autoStartService = autoStartService ?? new AutoStartService();
        _localConfigFallbackReader = localConfigFallbackReader ?? new LocalEnvConfigFallbackReader();
        _backendControlApiService = backendControlApiService ?? new BackendControlApiService();
        _botProcessService = botProcessService ?? new BotProcessService();
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
        set => SetTrackedProperty(ref _botPersona, value);
    }

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

    public string LatestWechatLlmSummaryText =>
        FormatLlmRequestSummary(_lastWechatLlmRequest, "No Wechat requests captured yet");

    public string LatestWechatLlmDetailText =>
        FormatLlmRequestDetail(_lastWechatLlmRequest);

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
            var loadResult = await LoadConfigFromAuthoritativeSourceAsync();
            var apiConfig = loadResult.ApiConfig;
            var apiStatus = loadResult.ApiStatus;

            if (apiConfig is not null)
            {
                _envDocument = new EnvDocument
                {
                    Config = BuildConfigCopy(apiConfig)
                };
            }
            else
            {
                _envDocument = await _localConfigFallbackReader.LoadAsync(BackendRootPath);
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

        if (configFailure.Kind == BackendControlApiFailureKind.Rejected ||
            configFailure.Kind == BackendControlApiFailureKind.Unknown)
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

            _botProcessService.Start(BackendRootPath);

            var status = await WaitForBackendControlStatusAsync();
            if (status is null)
            {
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
        var status = await _backendControlApiService.TryGetStatusAsync();

        if (status is null)
        {
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
        OnPropertyChanged(nameof(LatestQqLlmSummaryText));
        OnPropertyChanged(nameof(LatestQqLlmDetailText));
        OnPropertyChanged(nameof(LatestWechatLlmSummaryText));
        OnPropertyChanged(nameof(LatestWechatLlmDetailText));
    }

    private void ApplyBackendRuntimeStatus(BackendRuntimeStatus? status, bool controlApiReachable)
    {
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
        _lastWechatLlmRequest = status?.LastWechatLlmRequest;
        _lastControlApiReachable = controlApiReachable;
        OnPropertyChanged(nameof(IsControlApiReachable));
        NotifyRuntimeSnapshotChanged();
    }

    private void ResetControlApiFailureState()
    {
        _consecutiveControlApiFailures = 0;
        _controlApiOutageNotified = false;
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
            AddLog($"Control API unreachable; attempting backend recovery ({reason}).");

            var existingStatus = await _backendControlApiService.TryGetStatusAsync();
            if (existingStatus is not null)
            {
                ApplyBackendRuntimeStatus(existingStatus, true);
                ResetControlApiFailureState();
                AddLog("Control API recovered before local restart was needed.");
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
        var apiResult = await _backendControlApiService.TrySaveConfigAsync(config);

        if (apiResult is not null)
        {
            return apiResult;
        }

        var initialFailure = _backendControlApiService.LastFailure;

        if (initialFailure.Kind == BackendControlApiFailureKind.Rejected)
        {
            throw new InvalidOperationException(initialFailure.Message);
        }

        var currentStatus = await _backendControlApiService.TryGetStatusAsync();

        if (currentStatus is null)
        {
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

    private static string FormatLlmRequestSummary(BackendLlmRequestStatus? request, string emptyText)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route) || string.IsNullOrWhiteSpace(request.Model))
        {
            return emptyText;
        }

        var apiStyle = string.IsNullOrWhiteSpace(request.EffectiveApiStyle) ? "unknown" : request.EffectiveApiStyle;
        return $"{request.Route} / {request.Model} / {apiStyle}";
    }

    private static string FormatLlmRequestDetail(BackendLlmRequestStatus? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Route))
        {
            return "No completed requests captured yet.";
        }

        var capturedAt = FormatCapturedAt(request.CapturedAt);
        var routeReason = string.IsNullOrWhiteSpace(request.RouteReason) ? "default" : request.RouteReason;
        var reasoning = string.IsNullOrWhiteSpace(request.EffectiveReasoningEffort) ? "none" : request.EffectiveReasoningEffort;
        var verbosity = string.IsNullOrWhiteSpace(request.EffectiveTextVerbosity) ? "none" : request.EffectiveTextVerbosity;
        var tools = request.EffectiveTools is { Length: > 0 }
            ? string.Join("+", request.EffectiveTools)
            : "none";

        return $"At {capturedAt} | reason={routeReason} | reasoning={reasoning} | verbosity={verbosity} | tools={tools} | images={request.ImageCount}";
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
