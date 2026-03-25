using QQAIBot.Desktop.Services;

namespace QQAIBot.Desktop;

public partial class App : System.Windows.Application
{
    private const string MinimizedArgument = "--minimized";
    private const string EnsureRuntimeArgument = "--ensure-runtime";
    private const string SingleInstanceSuffixEnvKey = "QQ_AI_BOT_DESKTOP_SINGLE_INSTANCE_SUFFIX";

    private readonly SingleInstanceCoordinator _singleInstanceCoordinator;

    public App()
    {
        var scopeSuffix = Environment.GetEnvironmentVariable(SingleInstanceSuffixEnvKey)?.Trim();

        _singleInstanceCoordinator = string.IsNullOrWhiteSpace(scopeSuffix)
            ? new SingleInstanceCoordinator()
            : new SingleInstanceCoordinator(
                mutexName: $"QQAIBot.Desktop.SingleInstance.{scopeSuffix}",
                activateEventName: $"QQAIBot.Desktop.Activate.{scopeSuffix}",
                ensureRuntimeEventName: $"QQAIBot.Desktop.EnsureRuntime.{scopeSuffix}");
    }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        var launchMinimized = e.Args.Any(static arg =>
            string.Equals(arg, MinimizedArgument, StringComparison.OrdinalIgnoreCase)
        );
        var ensureRuntimeOnStartup = e.Args.Any(static arg =>
            string.Equals(arg, EnsureRuntimeArgument, StringComparison.OrdinalIgnoreCase)
        );
        if (!_singleInstanceCoordinator.TryAcquirePrimaryOwnership(
                () => Dispatcher.BeginInvoke(() =>
                {
                    if (MainWindow is MainWindow window)
                    {
                        window.RestoreFromExternalActivation();
                    }
                }),
                () => Dispatcher.BeginInvoke(() =>
                {
                    if (MainWindow is MainWindow window)
                    {
                        window.EnsureRuntimeFromExternalActivation();
                    }
                })))
        {
            _singleInstanceCoordinator.SignalPrimaryInstance(ensureRuntimeOnStartup);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var window = new MainWindow(launchMinimized, ensureRuntimeOnStartup);
        if (launchMinimized)
        {
            window.Visibility = System.Windows.Visibility.Hidden;
        }
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _singleInstanceCoordinator.Dispose();
        base.OnExit(e);
    }
}
