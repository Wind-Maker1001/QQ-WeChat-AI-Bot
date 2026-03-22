using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

using QQAIBot.Desktop.ViewModels;

namespace QQAIBot.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly bool _launchMinimizedToTray;
    private readonly bool _ensureRuntimeOnStartup;
    private bool _entranceAnimationPlayed;
    private bool _allowExit;
    private bool _balloonShown;

    public MainWindow(bool launchMinimizedToTray = false, bool ensureRuntimeOnStartup = false)
    {
        InitializeComponent();
        _launchMinimizedToTray = launchMinimizedToTray;
        _ensureRuntimeOnStartup = ensureRuntimeOnStartup;
        if (_launchMinimizedToTray)
        {
            ShowActivated = false;
            ShowInTaskbar = false;
            WindowState = WindowState.Minimized;
            Opacity = 0;
        }
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.NotificationRequested += OnNotificationRequested;
        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        Closed += OnClosed;

        _notifyIcon = CreateNotifyIcon();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        BeginEntranceAnimations();
        await _viewModel.InitializeAsync();

        if (_ensureRuntimeOnStartup && _viewModel.StartCommand.CanExecute(null))
        {
            _viewModel.StartCommand.Execute(null);
        }

        if (_launchMinimizedToTray)
        {
            _ = Dispatcher.BeginInvoke(() => HideToTray(showBalloon: false), DispatcherPriority.Background);
        }
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.NotificationRequested -= OnNotificationRequested;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        await _viewModel.DisposeAsync();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowExit)
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
        if (WindowState == WindowState.Minimized)
        {
            HideToTray(showBalloon: !_balloonShown);
        }
    }

    private Forms.NotifyIcon CreateNotifyIcon()
    {
        var contextMenu = new Forms.ContextMenuStrip();

        contextMenu.Items.Add("Open", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        contextMenu.Items.Add("Start Backend", null, (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_viewModel.StartCommand.CanExecute(null))
                {
                    _viewModel.StartCommand.Execute(null);
                }
            });
        });
        contextMenu.Items.Add("Stop Backend", null, (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_viewModel.StopCommand.CanExecute(null))
                {
                    _viewModel.StopCommand.Execute(null);
                }
            });
        });
        contextMenu.Items.Add(new Forms.ToolStripSeparator());
        contextMenu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(ExitFromTray));

        var notifyIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
            Text = "QQ AI Bot",
            Visible = true,
            ContextMenuStrip = contextMenu
        };

        notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        return notifyIcon;
    }

    private void HideToTray(bool showBalloon)
    {
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

    private void OnNotificationRequested(object? sender, QQAIBot.Desktop.Models.TrayNotification notification)
    {
        _notifyIcon.BalloonTipTitle = string.IsNullOrWhiteSpace(notification.Title) ? "QQ AI Bot" : notification.Title;
        _notifyIcon.BalloonTipText = notification.Message;
        _notifyIcon.BalloonTipIcon = notification.Icon;
        _notifyIcon.ShowBalloonTip(2500);
    }

    public void RestoreFromExternalActivation()
    {
        ShowFromTray();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void EnsureRuntimeFromExternalActivation()
    {
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
}
