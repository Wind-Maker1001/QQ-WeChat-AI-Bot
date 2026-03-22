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
    private readonly AutoStartService _autoStartService = new();
    private readonly EnvFileService _envFileService = new();
    private readonly BackendControlApiService _backendControlApiService = new();
    private readonly BotProcessService _botProcessService = new();
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
    private string _openAiDefaultModel = "deepseek-chat";
    private string _openAiModel = "gpt-5.4";
    private string _openAiBaseUrl = string.Empty;
    private string _openAiDefaultBaseUrl = "https://api.deepseek.com/v1";
    private string _openAiAdvancedTriggerPrefixes = "/5.4,/gpt,/vision,/高级,/多模态,/看图,/图片分析";
    private string _napCatWsUrl = "ws://127.0.0.1:3001";
    private string _napCatToken = string.Empty;
    private string _botPrefix = "/ai";
    private string _botPersona = string.Empty;
    private string _maxOutputChars = "800";
    private string _allowedGroupIds = string.Empty;
    private string _allowedUserIds = string.Empty;
    private string _lastLoadedAtText = "Not loaded";
    private string _lastSavedAtText = "Not saved";
    private string _logText = string.Empty;
    private bool _logDirty;
    private int? _lastWorkerProcessId;
    private bool? _lastRuntimeActive;
    private bool? _lastControlApiReachable;

    public event EventHandler<TrayNotification>? NotificationRequested;

    public MainViewModel()
    {
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

    public string AllowedGroupIds
    {
        get => _allowedGroupIds;
        set => SetTrackedProperty(ref _allowedGroupIds, value);
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
        _logFlushTimer.Stop();
        _statusPollTimer.Stop();

        if (_botProcessService.IsRunning)
        {
            await _botProcessService.StopAsync();
        }

        _backendControlApiService.Dispose();
        _botProcessService.Dispose();
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
            _envDocument = await _envFileService.LoadAsync(BackendRootPath);
            var apiConfig = await _backendControlApiService.TryGetConfigAsync();
            var apiStatus = await _backendControlApiService.TryGetStatusAsync();
            var effectiveConfig = apiConfig is not null ? (BotConfig)apiConfig : _envDocument.Config;

            _envDocument.Config = BuildConfigCopy(effectiveConfig);
            ApplyConfigToView(_envDocument.Config);
            IsProcessRunning = apiStatus?.RuntimeActive == true;
            _lastWorkerProcessId = apiStatus?.WorkerProcessId;
            _lastRuntimeActive = apiStatus?.RuntimeActive;
            _lastControlApiReachable = apiStatus is not null;

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

    private async Task SaveConfigAsync()
    {
        if (!IsBackendRootValid)
        {
            StatusText = "Backend root is invalid";
            AddLog("Cannot save config because backend root is invalid.");
            return;
        }

        try
        {
            StatusText = "Saving config...";
            _envDocument.Config = BuildConfig();
            var apiResult = await _backendControlApiService.TrySaveConfigAsync(_envDocument.Config);

            if (apiResult is not null)
            {
                _envDocument.Config = BuildConfigCopy(apiResult);
                ApplyConfigToView(_envDocument.Config);
                StatusText = apiResult.RestartRequired ? "Config saved (restart required)" : "Config saved";
                AddLog($"Saved config via control API: {apiResult.EnvPath}");
            }
            else
            {
                await _envFileService.SaveAsync(BackendRootPath, _envDocument);
                StatusText = "Config saved";
                AddLog($"Saved config to file: {EnvFilePath}");
            }

            LastSavedAtText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            HasUnsavedChanges = false;
        }
        catch (Exception ex)
        {
            StatusText = "Save failed";
            AddLog($"Save config failed: {ex.Message}");
            System.Windows.MessageBox.Show($"Save config failed:\n{ex.Message}", "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
            await SaveConfigAsync();
            StatusText = "Starting backend...";

            var existingStatus = await _backendControlApiService.TryGetStatusAsync();
            if (existingStatus is not null)
            {
                var attachStatus = await _backendControlApiService.TryStartAsync() ?? existingStatus;
                IsProcessRunning = attachStatus.RuntimeActive;
                _lastRuntimeActive = attachStatus.RuntimeActive;
                _lastControlApiReachable = true;
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
            IsProcessRunning = startedStatus?.RuntimeActive == true;
            _lastWorkerProcessId = startedStatus?.WorkerProcessId;
            _lastRuntimeActive = startedStatus?.RuntimeActive;
            _lastControlApiReachable = startedStatus is not null;
            StatusText = IsProcessRunning ? "Backend is running" : "Backend start command was ignored";

            if (startedStatus is not null)
            {
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
                IsProcessRunning = stoppedStatus.RuntimeActive;
                _lastWorkerProcessId = stoppedStatus.WorkerProcessId;
                _lastRuntimeActive = stoppedStatus.RuntimeActive;
                _lastControlApiReachable = true;
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
                IsProcessRunning = false;
                _lastWorkerProcessId = null;
                _lastRuntimeActive = false;
                _lastControlApiReachable = false;
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
                IsProcessRunning = false;
                _lastWorkerProcessId = null;
                _lastRuntimeActive = false;
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
        System.Windows.Application.Current.Dispatcher.BeginInvoke(
            () => AddLog(message),
            DispatcherPriority.Background
        );
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
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
            OpenAiAdvancedTriggerPrefixes = OpenAiAdvancedTriggerPrefixes.Trim(),
            NapCatWsUrl = NapCatWsUrl.Trim(),
            NapCatToken = NapCatToken.Trim(),
            BotPrefix = BotPrefix.Trim(),
            BotPersona = BotPersona.Trim(),
            MaxOutputChars = MaxOutputChars.Trim(),
            AllowedGroupIds = AllowedGroupIds.Trim(),
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
            OpenAiAdvancedTriggerPrefixes = config.OpenAiAdvancedTriggerPrefixes,
            NapCatWsUrl = config.NapCatWsUrl,
            NapCatToken = config.NapCatToken,
            BotPrefix = config.BotPrefix,
            BotPersona = config.BotPersona,
            MaxOutputChars = config.MaxOutputChars,
            AllowedGroupIds = config.AllowedGroupIds,
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
        OpenAiAdvancedTriggerPrefixes = config.OpenAiAdvancedTriggerPrefixes;
        NapCatWsUrl = config.NapCatWsUrl;
        NapCatToken = config.NapCatToken;
        BotPrefix = config.BotPrefix;
        BotPersona = config.BotPersona;
        MaxOutputChars = config.MaxOutputChars;
        AllowedGroupIds = config.AllowedGroupIds;
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
            if (_lastControlApiReachable != false)
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
            }

            _lastControlApiReachable = false;
            return;
        }

        if (_lastControlApiReachable == false)
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

        _lastRuntimeActive = status.RuntimeActive;
        _lastWorkerProcessId = status.WorkerProcessId;
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
}
