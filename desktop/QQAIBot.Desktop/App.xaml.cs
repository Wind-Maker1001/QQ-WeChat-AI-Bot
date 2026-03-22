using System.Threading;

namespace QQAIBot.Desktop;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "QQAIBot.Desktop.SingleInstance";
    private const string ActivateEventName = "QQAIBot.Desktop.Activate";
    private const string EnsureRuntimeEventName = "QQAIBot.Desktop.EnsureRuntime";
    private const string MinimizedArgument = "--minimized";
    private const string EnsureRuntimeArgument = "--ensure-runtime";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activateEvent;
    private EventWaitHandle? _ensureRuntimeEvent;
    private RegisteredWaitHandle? _activateWaitHandle;
    private RegisteredWaitHandle? _ensureRuntimeWaitHandle;
    private bool _ownsMutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        var launchMinimized = e.Args.Any(static arg =>
            string.Equals(arg, MinimizedArgument, StringComparison.OrdinalIgnoreCase)
        );
        var ensureRuntimeOnStartup = e.Args.Any(static arg =>
            string.Equals(arg, EnsureRuntimeArgument, StringComparison.OrdinalIgnoreCase)
        );
        var createdNew = false;
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out createdNew);
        _ownsMutex = createdNew;

        if (!createdNew)
        {
            SignalExistingInstance(ensureRuntimeOnStartup);
            Shutdown();
            return;
        }

        _activateEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: ActivateEventName
        );
        _activateWaitHandle = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is MainWindow window)
                {
                    window.RestoreFromExternalActivation();
                }
            }),
            state: null,
            millisecondsTimeOutInterval: -1,
            executeOnlyOnce: false
        );
        _ensureRuntimeEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: EnsureRuntimeEventName
        );
        _ensureRuntimeWaitHandle = ThreadPool.RegisterWaitForSingleObject(
            _ensureRuntimeEvent,
            (_, _) => Dispatcher.BeginInvoke(() =>
            {
                if (MainWindow is MainWindow window)
                {
                    window.EnsureRuntimeFromExternalActivation();
                }
            }),
            state: null,
            millisecondsTimeOutInterval: -1,
            executeOnlyOnce: false
        );

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
        _activateWaitHandle?.Unregister(waitObject: null);
        _ensureRuntimeWaitHandle?.Unregister(waitObject: null);
        _activateEvent?.Dispose();
        _ensureRuntimeEvent?.Dispose();

        if (_ownsMutex && _singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void SignalExistingInstance(bool ensureRuntime)
    {
        try
        {
            var eventName = ensureRuntime ? EnsureRuntimeEventName : ActivateEventName;
            using var targetEvent = EventWaitHandle.OpenExisting(eventName);
            targetEvent.Set();
        }
        catch
        {
            // Existing instance may still be starting up; ignore and exit.
        }
    }
}
