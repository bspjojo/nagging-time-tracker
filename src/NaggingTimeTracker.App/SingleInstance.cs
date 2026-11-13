using System.Windows;
using NaggingTimeTracker.Core.Startup;

namespace NaggingTimeTracker.App;

/// <summary>
/// One app instance per data directory. A second launch signals the first (which opens its
/// quick panel) and exits. Acquire and dispose on the UI thread: a mutex must be released on
/// the thread that acquired it.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly ManualResetEvent _stopEvent = new(false);
    private Thread? _listener;

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent)
    {
        _mutex = mutex;
        _showEvent = showEvent;
    }

    /// <summary>The instance when this process is the first; null after signalling an existing one.</summary>
    public static SingleInstance? TryAcquire(InstanceNames names)
    {
        ArgumentNullException.ThrowIfNull(names);
        var mutex = new Mutex(initiallyOwned: true, names.Mutex, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            if (EventWaitHandle.TryOpenExisting(names.ShowEvent, out var existing))
            {
                using (existing)
                {
                    existing.Set();
                }
            }

            return null;
        }

        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, names.ShowEvent);
        return new SingleInstance(mutex, showEvent);
    }

    /// <summary>Calls <paramref name="onShow"/> on the UI thread whenever another launch signals.</summary>
    public void ListenForShow(Action onShow)
    {
        ArgumentNullException.ThrowIfNull(onShow);
        var dispatcher = Application.Current.Dispatcher;
        _listener = new Thread(() =>
        {
            WaitHandle[] handles = [_showEvent, _stopEvent];
            while (WaitHandle.WaitAny(handles) == 0)
            {
                dispatcher.BeginInvoke(onShow);
            }
        })
        {
            IsBackground = true,
            Name = "SingleInstance.ShowListener",
        };
        _listener.Start();
    }

    public void Dispose()
    {
        _stopEvent.Set();
        _listener?.Join(TimeSpan.FromSeconds(1));
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _showEvent.Dispose();
        _stopEvent.Dispose();
    }
}
