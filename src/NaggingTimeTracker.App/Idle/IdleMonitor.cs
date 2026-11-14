using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using NaggingTimeTracker.App.Reminders;
using NaggingTimeTracker.App.Windows;
using NaggingTimeTracker.Core.Idle;
using NaggingTimeTracker.Core.Services;
using NaggingTimeTracker.Core.Settings;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.App.Idle;

/// <summary>
/// The OS shell around <see cref="IdleTracker"/>: polls the last input every 15 s, forwards lock,
/// unlock, suspend and resume, and shows the idle prompt on return. Everything runs on the UI
/// thread. Not registered under --no-background.
/// </summary>
public sealed class IdleMonitor(IdleResolver resolver, SettingsService settings, CatalogService catalog, TimeProvider time)
    : IIdleState, IHostedService, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly IdleTracker _tracker = new();
    private Dispatcher? _dispatcher;
    private DispatcherTimer? _timer;
    private bool _subscribed;

    public bool IsUserIdle => _tracker.IsUserIdle;

    public bool IsPromptOpen { get; private set; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _dispatcher = Application.Current.Dispatcher;
        _dispatcher.Invoke(() =>
        {
            _timer = new DispatcherTimer(PollInterval, DispatcherPriority.Background, (_, _) => Poll(), _dispatcher);
            _timer.Start();
        });

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _subscribed = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Shutdown();
        return Task.CompletedTask;
    }

    public void Dispose() => Shutdown();

    private void Shutdown()
    {
        if (_subscribed)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _subscribed = false;
        }

        if (_timer is { } timer)
        {
            _timer = null;
            if (timer.Dispatcher.CheckAccess())
            {
                timer.Stop();
            }
            else if (!timer.Dispatcher.HasShutdownStarted)
            {
                timer.Dispatcher.Invoke(timer.Stop);
            }
        }
    }

    // SystemEvents may raise on its own thread: hop to the UI thread before touching the tracker.
    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e) =>
        _dispatcher?.BeginInvoke(() => Guard(() =>
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    _tracker.Lock(time.NowUtc(), NativeMethods.SinceLastInput() ?? TimeSpan.Zero, Options());
                    break;
                case SessionSwitchReason.SessionUnlock:
                    OnReturned(_tracker.Unlock(time.NowUtc(), Options()));
                    break;
            }
        }));

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e) =>
        _dispatcher?.BeginInvoke(() => Guard(() =>
        {
            switch (e.Mode)
            {
                case PowerModes.Suspend:
                    _tracker.Suspend(time.NowUtc(), NativeMethods.SinceLastInput() ?? TimeSpan.Zero, Options());
                    break;
                case PowerModes.Resume:
                    _tracker.Resume();
                    break;
            }
        }));

    private void Poll() => Guard(() =>
    {
        if (NativeMethods.SinceLastInput() is { } idle)
        {
            OnReturned(_tracker.Poll(time.NowUtc(), idle, Options()));
        }
    });

    /// <summary>Ignores input while the prompt is open; logs and swallows failures so polling continues.</summary>
    private void Guard(Action action)
    {
        if (IsPromptOpen)
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Idle check failed: {ex}");
        }
    }

    private IdleOptions Options() => IdleOptions.From(settings.Load());

    private void OnReturned(IdleSpan? span)
    {
        if (span is not { } s)
        {
            return;
        }

        IsPromptOpen = true;
        try
        {
            IdlePromptLabels.Ask(IdlePromptKind.Away, s, resolver, catalog, time);
        }
        finally
        {
            IsPromptOpen = false;
        }
    }
}
