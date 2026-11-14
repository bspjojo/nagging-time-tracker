using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Hosting;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Reminders;
using NaggingTimeTracker.Core.Services;
using NaggingTimeTracker.Core.Settings;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.App.Reminders;

/// <summary>
/// Evaluates reminders every 30 s and writes the heartbeat once a minute. Each tick runs on the
/// UI thread, so the engine, the queue and the windows are only ever touched from one thread.
/// Not registered under --no-background.
/// </summary>
public sealed class ReminderHost(
    ReminderEngine engine,
    NagWindowManager nags,
    TrackingService tracking,
    IEntryRepository entries,
    IReminderRepository reminders,
    SettingsService settings,
    IIdleState idle,
    PresentationState presentation,
    TimeProvider time) : IHostedService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(60);

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private DateTimeOffset? _lastHeartbeat;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var dispatcher = Application.Current.Dispatcher;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(dispatcher, _cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null || _loop is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }

    private async Task RunAsync(Dispatcher dispatcher, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                if (dispatcher.HasShutdownStarted)
                {
                    return;
                }

                // Awaiting means ticks never overlap.
                await dispatcher.InvokeAsync(Tick, DispatcherPriority.Background, ct);
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Tick()
    {
        try
        {
            var now = time.NowUtc();
            var state = new ReminderState(
                now,
                time.LocalTimeZone,
                settings.Load(),
                tracking.GetRunning(),
                entries.GetRecentStopped(1).FirstOrDefault()?.StopUtc,
                reminders.GetAll(),
                settings.GetInstant(SettingKeys.RemindersPausedUntilUtc),
                settings.GetInstant(SettingKeys.NotWorkingUntilUtc),
                idle.IsUserIdle,
                presentation.IsBusy(),
                nags.HasPending);

            var due = engine.Evaluate(state);
            foreach (var nag in due)
            {
                if (nag.ReminderId is { } id)
                {
                    reminders.SetLastFired(id, now);
                }
            }

            if (due.Count > 0)
            {
                nags.Enqueue(due);
            }

            if (_lastHeartbeat is null || now - _lastHeartbeat >= HeartbeatInterval)
            {
                settings.SetInstant(SettingKeys.HeartbeatUtc, now);
                _lastHeartbeat = now;
            }
        }
        catch (Exception ex)
        {
            // A transient failure (e.g. a locked database) must not stop reminders; the next tick retries.
            Debug.WriteLine($"Reminder tick failed: {ex}");
        }
    }
}
