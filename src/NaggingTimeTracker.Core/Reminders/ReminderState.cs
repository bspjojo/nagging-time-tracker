using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Settings;

namespace NaggingTimeTracker.Core.Reminders;

/// <summary>The inputs for one evaluation, built fresh each tick.</summary>
public sealed record ReminderState(
    DateTimeOffset NowUtc,
    TimeZoneInfo Tz,
    AppSettings Settings,
    IReadOnlyList<Entry> Running,
    DateTimeOffset? LastStopUtc,
    IReadOnlyList<ScheduledReminder> Reminders,
    DateTimeOffset? PausedUntilUtc,
    DateTimeOffset? NotWorkingUntilUtc,
    bool IsUserIdle,
    bool IsPresentationBusy,
    bool IsPromptOpen);
