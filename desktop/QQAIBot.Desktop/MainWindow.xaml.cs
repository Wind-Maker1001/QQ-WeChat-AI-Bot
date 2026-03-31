using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Diagnostics;
using System.Windows.Input;
using System.Reflection;

using Forms = System.Windows.Forms;

using QQAIBot.Desktop.ViewModels;
using QQAIBot.Desktop.Services;

namespace QQAIBot.Desktop;

public partial class MainWindow : Window
{
    private sealed record TrayMenuAction(string Label, Action Execute);
    private const string TrayExitMenuLabel = "退出控制台";

    private readonly MainViewModel _viewModel;
    private readonly bool _ownsViewModel;
    private readonly INotifyIconHost? _notifyIcon;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly bool _launchMinimizedToTray;
    private readonly bool _ensureRuntimeOnStartup;
    private readonly bool _enableNotifyIcon;
    private Task? _startupTask;
    private Task? _disposeTask;
    private bool _entranceAnimationPlayed;
    private bool _allowExit;
    private bool _balloonShown;
    private string _lastHealthActionTargetName = string.Empty;

    public MainWindow(
        bool launchMinimizedToTray = false,
        bool ensureRuntimeOnStartup = false,
        MainViewModel? viewModel = null,
        bool enableNotifyIcon = true,
        INotifyIconHost? notifyIconHost = null,
        IConfirmationDialogService? confirmationDialogService = null)
    {
        InitializeComponent();
        RegisterNamedElementsForAutomation();
        _launchMinimizedToTray = launchMinimizedToTray;
        _ensureRuntimeOnStartup = ensureRuntimeOnStartup;
        _enableNotifyIcon = enableNotifyIcon;
        _confirmationDialogService = confirmationDialogService ?? new ConfirmationDialogService();
        if (_launchMinimizedToTray)
        {
            ShowActivated = false;
            ShowInTaskbar = false;
            WindowState = WindowState.Minimized;
            Opacity = 0;
        }
        _ownsViewModel = viewModel is null;
        _viewModel = viewModel ?? new MainViewModel();
        DataContext = _viewModel;
        _viewModel.NotificationRequested += OnNotificationRequested;
        _viewModel.HealthActionRequested += OnHealthActionRequested;
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        Closed += OnClosed;

        if (_enableNotifyIcon)
        {
            _notifyIcon = notifyIconHost ?? CreateNotifyIcon();
            _notifyIcon.DoubleClick += OnNotifyIconDoubleClick;
        }

        EnsureStartupSequenceStarted();
    }

    private void RegisterNamedElementsForAutomation()
    {
        var fields = GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        foreach (var field in fields)
        {
            if (field.IsStatic ||
                field.Name.StartsWith("_", StringComparison.Ordinal) ||
                FindName(field.Name) is not null)
            {
                continue;
            }

            var value = field.GetValue(this);

            if (value is FrameworkElement or FrameworkContentElement)
            {
                try
                {
                    RegisterName(field.Name, value);
                }
                catch (ArgumentException)
                {
                    // Ignore names that already belong to another namescope.
                }
            }
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureStartupSequenceStarted();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        EnsureDisposeSequenceStarted();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowExit)
        {
            return;
        }

        if (!_enableNotifyIcon)
        {
            return;
        }

        e.Cancel = true;
        HideToTray(showBalloon: true);
    }

    private void LogTextBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        LogTextBox.ScrollToEnd();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_enableNotifyIcon && WindowState == WindowState.Minimized)
        {
            HideToTray(showBalloon: !_balloonShown);
        }
    }

    private INotifyIconHost CreateNotifyIcon()
    {
        var contextMenu = new Forms.ContextMenuStrip();

        foreach (var trayMenuAction in CreateTrayMenuActions())
        {
            contextMenu.Items.Add(trayMenuAction.Label, null, (_, _) => trayMenuAction.Execute());
        }

        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add(TrayExitMenuLabel, null, (_, _) => Dispatcher.Invoke(ExitFromTray));

        return new NotifyIconHost(contextMenu);
    }

    private IReadOnlyList<TrayMenuAction> CreateTrayMenuActions()
    {
        return
        [
            new TrayMenuAction("Open", () => Dispatcher.Invoke(ShowFromTray)),
            new TrayMenuAction("Start Backend", () => Dispatcher.Invoke(StartBackendFromTray)),
            new TrayMenuAction("Stop Backend", () => Dispatcher.Invoke(StopBackendFromTray))
        ];
    }

    private void StartBackendFromTray()
    {
        if (_viewModel.StartCommand.CanExecute(null))
        {
            _viewModel.StartCommand.Execute(null);
        }
    }

    private void StopBackendFromTray()
    {
        if (_viewModel.StopCommand.CanExecute(null))
        {
            _viewModel.StopCommand.Execute(null);
        }
    }

    private void HideToTray(bool showBalloon)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        Hide();
        ShowInTaskbar = false;

        if (showBalloon)
        {
            _balloonShown = true;
            _notifyIcon.BalloonTipTitle = "Local AI Runtime";
            _notifyIcon.BalloonTipText = BuildTrayBalloonText();
            _notifyIcon.ShowBalloonTip(2000);
        }
    }

    private void ShowFromTray()
    {
        Opacity = 1;
        ShowActivated = true;
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitDesktopButton_OnClick(object sender, RoutedEventArgs e)
    {
        RequestDesktopExit();
    }

    private void MoreActionsButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button ||
            button.ContextMenu is not { } contextMenu)
        {
            return;
        }

        contextMenu.PlacementTarget = button;
        contextMenu.IsOpen = true;
    }

    private void ExitFromTray()
    {
        RequestDesktopExit();
    }

    private void RequestDesktopExit()
    {
        if (!ConfirmTrayExit())
        {
            return;
        }

        _allowExit = true;
        ShowInTaskbar = false;
        Close();
    }

    public void ExitFromTrayForTests()
    {
        ForceExitFromTray();
    }

    public bool InvokeTrayExitForTests()
    {
        if (!ConfirmTrayExit())
        {
            return false;
        }

        ForceExitFromTray();
        return true;
    }

    public IReadOnlyList<string> GetTrayMenuLabelsForTests()
    {
        return CreateTrayMenuActions().Select(static action => action.Label).ToList();
    }

    public string GetTrayExitLabelForTests()
    {
        return TrayExitMenuLabel;
    }

    public void InvokeTrayMenuActionForTests(string label)
    {
        var trayMenuAction = CreateTrayMenuActions().SingleOrDefault((action) => action.Label == label)
            ?? throw new InvalidOperationException($"Unknown tray menu action: {label}");

        trayMenuAction.Execute();
    }

    public string GetLastHealthActionTargetNameForTests()
    {
        return _lastHealthActionTargetName;
    }

    private void OnNotificationRequested(object? sender, QQAIBot.Desktop.Models.TrayNotification notification)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = string.IsNullOrWhiteSpace(notification.Title) ? "Local AI Runtime" : notification.Title;
        _notifyIcon.BalloonTipText = notification.Message;
        _notifyIcon.BalloonTipIcon = notification.Icon;
        _notifyIcon.ShowBalloonTip(2500);
    }

    private void OnHealthActionRequested(object? sender, string actionKey)
    {
        Dispatcher.Invoke(() => HandleHealthAction(actionKey));
    }

    private void OnNotifyIconDoubleClick(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(ShowFromTray);
    }

    public void RestoreFromExternalActivation()
    {
        DesktopTestSignal.Emit("restore-from-external-activation");
        ShowFromTray();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void EnsureRuntimeFromExternalActivation()
    {
        DesktopTestSignal.Emit("ensure-runtime-from-external-activation");
        if (_viewModel.StartCommand.CanExecute(null))
        {
            _viewModel.StartCommand.Execute(null);
        }
    }

    private void BeginEntranceAnimations()
    {
        if (_entranceAnimationPlayed)
        {
            return;
        }

        _entranceAnimationPlayed = true;

        var animatedElements = new UIElement[]
        {
            HeroHeaderCard,
            OverviewCardsPanel,
            ToolbarShellCard,
            SuggestionsPanel,
            ConfigurationWorkspace,
            RuntimeInfoCard,
            LogCard
        };

        for (var index = 0; index < animatedElements.Length; index++)
        {
            AnimateEntrance(animatedElements[index], index);
        }
    }

    private static void AnimateEntrance(UIElement element, int index)
    {
        element.Opacity = 0;

        var translate = new TranslateTransform(0, index < 2 ? 16 : 24);
        element.RenderTransform = translate;

        var delay = TimeSpan.FromMilliseconds(index * 55);
        var duration = TimeSpan.FromMilliseconds(index < 2 ? 320 : 380);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var opacityAnimation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            BeginTime = delay,
            Duration = duration,
            EasingFunction = easing
        };

        var translateAnimation = new DoubleAnimation
        {
            From = translate.Y,
            To = 0,
            BeginTime = delay,
            Duration = duration,
            EasingFunction = easing
        };

        element.BeginAnimation(OpacityProperty, opacityAnimation);
        translate.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
    }

    private void EnsureStartupSequenceStarted()
    {
        if (_startupTask is not null)
        {
            return;
        }

        _startupTask = RunStartupSequenceAsync();
        ObserveBackgroundTask(_startupTask, "window startup");
    }

    private async Task RunStartupSequenceAsync()
    {
        await _viewModel.InitializeAsync();
        DesktopTestSignal.Emit("window-loaded");
        TryBeginEntranceAnimations();

        if ((_ensureRuntimeOnStartup || !_viewModel.IsControlApiReachable) &&
            _viewModel.StartCommand.CanExecute(null))
        {
            _viewModel.StartCommand.Execute(null);
        }

        if (_launchMinimizedToTray)
        {
            await Dispatcher.BeginInvoke(
                () => HideToTray(showBalloon: false),
                DispatcherPriority.Background
            ).Task;
        }
    }

    private void EnsureDisposeSequenceStarted()
    {
        if (_disposeTask is not null)
        {
            return;
        }

        _disposeTask = DisposeWindowAsync();
        ObserveBackgroundTask(_disposeTask, "window dispose");
    }

    private async Task DisposeWindowAsync()
    {
        _viewModel.NotificationRequested -= OnNotificationRequested;
        _viewModel.HealthActionRequested -= OnHealthActionRequested;

        if (_notifyIcon is not null)
        {
            _notifyIcon.DoubleClick -= OnNotifyIconDoubleClick;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        if (_ownsViewModel)
        {
            await _viewModel.DisposeAsync();
        }
    }

    private void HandleHealthAction(string actionKey)
    {
        if (string.IsNullOrWhiteSpace(actionKey))
        {
            return;
        }

        ShowFromTray();

        switch (actionKey)
        {
            case "focus_backend_root":
                FocusSettingsElement(StartupSyncTabItem, BackendRootPathTextBox);
                return;
            case "focus_control_api_token":
                FocusSettingsElement(StartupSyncTabItem, ControlApiTokenTextBox);
                return;
            case "focus_openai_default_key":
                FocusSettingsElement(ModelApiTabItem, OpenAiDefaultApiKeyTextBox);
                return;
            case "focus_openai_advanced_key":
                FocusSettingsElement(ModelApiTabItem, OpenAiApiKeyTextBox);
                return;
            case "focus_deepseek_api_key":
                FocusSettingsElement(ModelApiTabItem, DeepSeekApiKeyTextBox);
                return;
            case "focus_napcat_url":
                FocusSettingsElement(ChannelsTabItem, NapCatWsUrlTextBox);
                return;
            case "focus_napcat_token":
                FocusSettingsElement(ChannelsTabItem, NapCatTokenTextBox);
                return;
            case "focus_wechat_url":
                FocusSettingsElement(ChannelsTabItem, WechatBridgeUrlTextBox);
                return;
            case "focus_qq_failure":
                FocusLatestFailureActivity(isQq: true);
                return;
            case "focus_wechat_failure":
                FocusLatestFailureActivity(isQq: false);
                return;
            case "focus_latest_activity":
                FocusLatestActivity();
                return;
            case "show_logs":
                LogCard.BringIntoView();
                FocusElement(LogTextBox);
                LogTextBox.ScrollToEnd();
                return;
            default:
                return;
        }
    }

    private void FocusLatestFailureActivity(bool isQq)
    {
        if (isQq)
        {
            SelectActivityTab(QqActivityTab);
            var latestFailure = _viewModel.QqRecentActivities.FirstOrDefault(static item => item.IsFailure);
            if (latestFailure is not null)
            {
                _viewModel.SelectedQqRecentActivity = latestFailure;
                LatestQqRecentActivityListBox.ScrollIntoView(latestFailure);
            }

            FocusElement(LatestQqRecentActivityListBox);
            return;
        }

        SelectActivityTab(WechatActivityTab);
        var latestWechatFailure = _viewModel.WechatRecentActivities.FirstOrDefault(static item => item.IsFailure);
        if (latestWechatFailure is not null)
        {
            _viewModel.SelectedWechatRecentActivity = latestWechatFailure;
            LatestWechatRecentActivityListBox.ScrollIntoView(latestWechatFailure);
        }

        FocusElement(LatestWechatRecentActivityListBox);
    }

    private void FocusLatestActivity()
    {
        var latestQqItem = _viewModel.QqRecentActivities
            .OrderByDescending(static item => ParseCapturedAt(item.CapturedAt))
            .FirstOrDefault();
        var latestWechatItem = _viewModel.WechatRecentActivities
            .OrderByDescending(static item => ParseCapturedAt(item.CapturedAt))
            .FirstOrDefault();

        if (latestQqItem is null && latestWechatItem is null)
        {
            FocusElement(LogTextBox);
            return;
        }

        if (latestWechatItem is null ||
            (latestQqItem is not null && ParseCapturedAt(latestQqItem.CapturedAt) >= ParseCapturedAt(latestWechatItem.CapturedAt)))
        {
            SelectActivityTab(QqActivityTab);
            _viewModel.SelectedQqRecentActivity = latestQqItem;
            if (latestQqItem is not null)
            {
                LatestQqRecentActivityListBox.ScrollIntoView(latestQqItem);
            }

            FocusElement(LatestQqRecentActivityListBox);
            return;
        }

        SelectActivityTab(WechatActivityTab);
        _viewModel.SelectedWechatRecentActivity = latestWechatItem;
        LatestWechatRecentActivityListBox.ScrollIntoView(latestWechatItem);
        FocusElement(LatestWechatRecentActivityListBox);
    }

    private void FocusSettingsElement(System.Windows.Controls.TabItem tabItem, FrameworkElement element)
    {
        SelectSettingsTab(tabItem);
        FocusElement(element);
    }

    private void SelectSettingsTab(System.Windows.Controls.TabItem tabItem)
    {
        SettingsTabControl.SelectedItem = tabItem;
        SettingsTabControl.UpdateLayout();
    }

    private void SelectActivityTab(System.Windows.Controls.TabItem tabItem)
    {
        ChannelActivityTabControl.SelectedItem = tabItem;
        ChannelActivityTabControl.UpdateLayout();
    }

    private string BuildTrayBalloonText()
    {
        return _viewModel.IsProcessRunning
            ? "控制台已收进托盘。Backend 会继续运行，直到你主动停止它。"
            : "控制台已收进托盘。你可以随时重新打开，或者直接从托盘启动后端。";
    }

    private static DateTimeOffset ParseCapturedAt(string? capturedAt)
    {
        return DateTimeOffset.TryParse(capturedAt, out var parsedCapturedAt)
            ? parsedCapturedAt
            : DateTimeOffset.MinValue;
    }

    private bool ConfirmTrayExit()
    {
        if (!_viewModel.IsProcessRunning)
        {
            return true;
        }

        return _confirmationDialogService.Confirm(
            "退出控制台",
            string.Join(
                Environment.NewLine,
                [
                    "退出控制台只会关闭桌面壳和托盘图标。",
                    "Backend runtime 当前仍在运行，会继续保持在线，直到你主动选择停止后端。",
                    "之后你可以从桌面快捷方式或开始菜单重新打开 Local AI Runtime，而不会再启动第二个桌面壳。",
                    "是否继续？"
                ]));
    }

    private void ForceExitFromTray()
    {
        _allowExit = true;
        ShowInTaskbar = false;
        Close();
    }

    private void FocusElement(System.Windows.FrameworkElement element)
    {
        _lastHealthActionTargetName = element.Name;
        element.UpdateLayout();
        element.BringIntoView();
        element.Focus();
        Keyboard.Focus(element);

        if (element is System.Windows.Controls.TextBox textBox &&
            !textBox.IsReadOnly &&
            !string.IsNullOrEmpty(textBox.Text))
        {
            textBox.SelectAll();
        }

        AnimateFocusedElement(element);
    }

    private static void AnimateFocusedElement(System.Windows.FrameworkElement element)
    {
        if (element is not System.Windows.Controls.Control control)
        {
            return;
        }

        var originalBackground = control.Background;
        var targetColor = (originalBackground as SolidColorBrush)?.Color ?? Colors.White;
        var highlightColor = System.Windows.Media.Color.FromRgb(255, 244, 204);
        var animatedBrush = new SolidColorBrush(highlightColor);
        control.Background = animatedBrush;

        var backgroundAnimation = new ColorAnimation
        {
            From = highlightColor,
            To = targetColor,
            Duration = TimeSpan.FromMilliseconds(900),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            }
        };

        backgroundAnimation.Completed += (_, _) =>
        {
            control.Background = originalBackground;
        };

        animatedBrush.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
    }

    private void TryBeginEntranceAnimations()
    {
        try
        {
            BeginEntranceAnimations();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MainWindow entrance animation failed: {ex}");
        }
    }

    private static void ObserveBackgroundTask(Task task, string operationName)
    {
        _ = task.ContinueWith(
            t => Debug.WriteLine($"MainWindow {operationName} failed: {t.Exception}"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );
    }
}
