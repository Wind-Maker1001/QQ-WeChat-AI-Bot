using System.Threading;

namespace QQAIBot.Desktop.Services;

public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly string _mutexName;
    private readonly string _activateEventName;
    private readonly string _ensureRuntimeEventName;

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activateEvent;
    private EventWaitHandle? _ensureRuntimeEvent;
    private RegisteredWaitHandle? _activateWaitHandle;
    private RegisteredWaitHandle? _ensureRuntimeWaitHandle;
    private bool _ownsMutex;

    public SingleInstanceCoordinator(
        string mutexName = "QQAIBot.Desktop.SingleInstance",
        string activateEventName = "QQAIBot.Desktop.Activate",
        string ensureRuntimeEventName = "QQAIBot.Desktop.EnsureRuntime")
    {
        _mutexName = mutexName;
        _activateEventName = activateEventName;
        _ensureRuntimeEventName = ensureRuntimeEventName;
    }

    public bool IsPrimaryInstance => _ownsMutex;

    public bool TryAcquirePrimaryOwnership(Action onActivate, Action onEnsureRuntime)
    {
        var createdNew = false;
        var mutex = new Mutex(initiallyOwned: false, _mutexName, out createdNew);

        var acquiredOwnership = false;

        try
        {
            acquiredOwnership = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            acquiredOwnership = true;
        }

        if (!acquiredOwnership)
        {
            mutex.Dispose();
            return false;
        }

        _singleInstanceMutex = mutex;
        _ownsMutex = true;
        _activateEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: _activateEventName
        );
        _activateWaitHandle = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) => onActivate(),
            state: null,
            millisecondsTimeOutInterval: -1,
            executeOnlyOnce: false
        );

        _ensureRuntimeEvent = new EventWaitHandle(
            initialState: false,
            mode: EventResetMode.AutoReset,
            name: _ensureRuntimeEventName
        );
        _ensureRuntimeWaitHandle = ThreadPool.RegisterWaitForSingleObject(
            _ensureRuntimeEvent,
            (_, _) => onEnsureRuntime(),
            state: null,
            millisecondsTimeOutInterval: -1,
            executeOnlyOnce: false
        );

        return true;
    }

    public void SignalPrimaryInstance(bool ensureRuntime)
    {
        var eventName = ensureRuntime ? _ensureRuntimeEventName : _activateEventName;

        try
        {
            using var targetEvent = EventWaitHandle.OpenExisting(eventName);
            targetEvent.Set();
        }
        catch
        {
            // Existing instance may still be starting up; ignore.
        }
    }

    public void Dispose()
    {
        _activateWaitHandle?.Unregister(waitObject: null);
        _ensureRuntimeWaitHandle?.Unregister(waitObject: null);
        _activateEvent?.Dispose();
        _ensureRuntimeEvent?.Dispose();

        if (_ownsMutex && _singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Best-effort cleanup on shutdown/test teardown.
            }
        }

        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        _activateEvent = null;
        _ensureRuntimeEvent = null;
        _activateWaitHandle = null;
        _ensureRuntimeWaitHandle = null;
        _ownsMutex = false;
    }
}
