using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Diagnostics;

using Forms = System.Windows.Forms;

using QQAIBot.Desktop.ViewModels;
using QQAIBot.Desktop.Services;

namespace QQAIBot.Desktop;

public partial class MainWindow : Window
{
    private sealed record TrayMenuAction(string Label, Action Execute);

    private readonly MainViewModel _viewModel;
    private readonly bool _ownsViewModel;
    private readonly INotifyIconHost? _notifyIcon;
    private readonly bool _launchMinimizedToTray;
    private readonly bool _ensureRuntimeOnStartup;
    private readonly bool _enableNotifyIcon;
    private Task? _startupTask;
    private Task? _disposeTask;
    private bool _entranceAnimationPlayed;
    private bool _allowExit;
    private bool _balloonShown;

    public MainWindow(
        bool launchMinimizedToTray = false,
        bool ensureRuntimeOnStartup = false,
        MainViewModel? viewModel = null,
        bool enableNotifyIcon = true,
        INotifyIconHost? notifyIconHost = null)
    {
        InitializeComponent();
        _launchMinimizedToTray = launchMinimizedToTray;
        _ensureRuntimeOnStartup = ensureRuntimeOnStartup;
        _enableNotifyIcon = enableNotifyIcon;
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
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        Closed += OnClosed;

        if (_enableNotifyIcon)
        {
            _notifyIcon = notifyIconHost ?? CreateNotifyIcon();
            _notifyIcon.DoubleClick += OnNotifyIconDoubleClick;
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
        contextMenu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitFromTray));

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
            _notifyIcon.BalloonTipTitle = "QQ AI Bot";
            _notifyIcon.BalloonTipText = "The console is still running in the tray.";
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

    private void ExitFromTray()
    {
        _allowExit = true;
        ShowInTaskbar = false;
        Close();
    }

    public void ExitFromTrayForTests()
    {
        ExitFromTray();
    }

    public IReadOnlyList<string> GetTrayMenuLabelsForTests()
    {
        return CreateTrayMenuActions().Select(static action => action.Label).ToList();
    }

    public void InvokeTrayMenuActionForTests(string label)
    {
        var trayMenuAction = CreateTrayMenuActions().SingleOrDefault((action) => action.Label == label)
            ?? throw new InvalidOperationException($"Unknown tray menu action: {label}");

        trayMenuAction.Execute();
    }

    private void OnNotificationRequested(object? sender, QQAIBot.Desktop.Models.TrayNotification notification)
    {
        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.BalloonTipTitle = string.IsNullOrWhiteSpace(notification.Title) ? "QQ AI Bot" : notification.Title;
        _notifyIcon.BalloonTipText = notification.Message;
        _notifyIcon.BalloonTipIcon = notification.Icon;
        _notifyIcon.ShowBalloonTip(2500);
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
            ToolbarShellCard,
            OpenAiCard,
            NapCatCard,
            AccessCard,
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
