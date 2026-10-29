using System.Runtime.InteropServices;

namespace GitPersona.App.Platform;

/// <summary>
/// Keeps one GitPersona per Windows session. A second launch signals the first one to show its window and exits.
/// </summary>
internal sealed partial class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\GitPersona.SingleInstance";
    private const string ActivateEventName = @"Local\GitPersona.Activate";
    private const int AsfwAny = -1;

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle activate)
    {
        _mutex = mutex;
        _activate = activate;
    }

    /// <returns>The instance lock, or <see langword="null"/> when another instance already runs (it has been asked to show itself).</returns>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            return new SingleInstance(mutex, new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName));
        }

        mutex.Dispose();
        if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var activate))
        {
            // We were started by the user, so we may hand the foreground right to the running instance.
            AllowSetForegroundWindow(AsfwAny);
            activate.Set();
            activate.Dispose();
        }

        return null;
    }

    /// <summary>Invokes <paramref name="onActivate"/> on a thread-pool thread whenever another launch is attempted.</summary>
    public void ListenForActivation(Action onActivate)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activate,
            (_, _) => onActivate(),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _activate.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
