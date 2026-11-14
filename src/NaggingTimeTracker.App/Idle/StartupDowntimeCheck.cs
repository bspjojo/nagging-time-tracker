using NaggingTimeTracker.App.Windows;
using NaggingTimeTracker.Core.Idle;
using NaggingTimeTracker.Core.Services;
using NaggingTimeTracker.Core.Settings;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.App.Idle;

/// <summary>
/// At startup, before the first heartbeat write: if the app was down past the idle threshold with
/// timers running, asks what to do with that time (§7.3). Not run under --no-background.
/// </summary>
public sealed class StartupDowntimeCheck(
    IdleResolver resolver,
    SettingsService settings,
    TrackingService tracking,
    CatalogService catalog,
    TimeProvider time)
{
    public void Run()
    {
        var span = Downtime.Find(
            settings.GetInstant(SettingKeys.HeartbeatUtc), time.NowUtc(), settings.Load(), tracking.GetRunning());
        if (span is { } s)
        {
            IdlePromptLabels.Ask(IdlePromptKind.AppNotRunning, s, resolver, catalog, time);
        }
    }
}
