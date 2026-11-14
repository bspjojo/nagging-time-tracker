using System.Globalization;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.Core.Reminders;

/// <summary>
/// Decides which nags are due. Pure apart from its in-memory bookkeeping (last fires, snoozes,
/// check-ins), which resets on restart. It never touches storage: the host persists
/// last_fired_utc for scheduled nags.
/// </summary>
public sealed class ReminderEngine(DateTimeOffset appStartUtc)
{
    private static readonly TimeSpan MissedThreshold = TimeSpan.FromMinutes(2);

    private readonly Dictionary<long, DateTimeOffset> _lastCheckIn = [];
    private readonly List<(Nag Nag, DateTimeOffset Until)> _snoozedScheduled = [];
    private DateTimeOffset? _notTrackingLastFired;
    private DateTimeOffset? _notTrackingSnoozedUntil;

    public IReadOnlyList<Nag> Evaluate(ReminderState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var now = s.NowUtc;

        // Suppression, in order. None of these advance the bookkeeping, so nothing is lost.
        if (s.PausedUntilUtc > now || s.IsPromptOpen || s.IsUserIdle)
        {
            return [];
        }

        // Presentation mode doesn't suppress here: the queue holds nags until it ends.
        var runningIds = s.Running.Select(e => e.Id).ToHashSet();
        foreach (var id in _lastCheckIn.Keys.Where(id => !runningIds.Contains(id)).ToList())
        {
            _lastCheckIn.Remove(id);
        }

        var nags = new List<Nag>();
        for (var i = _snoozedScheduled.Count - 1; i >= 0; i--)
        {
            if (_snoozedScheduled[i].Until <= now)
            {
                nags.Insert(0, _snoozedScheduled[i].Nag);
                _snoozedScheduled.RemoveAt(i);
            }
        }

        nags.AddRange(Scheduled(s));
        if (NotTracking(s) is { } notTracking)
        {
            nags.Add(notTracking);
        }

        if (CheckIn(s) is { } checkIn)
        {
            nags.Add(checkIn);
        }

        return nags;
    }

    public void SnoozeNotTracking(DateTimeOffset untilUtc) => _notTrackingSnoozedUntil = untilUtc;

    /// <summary>"Still on it": the entry's check-in clock restarts now.</summary>
    public void CheckedIn(long entryId, DateTimeOffset nowUtc) => _lastCheckIn[entryId] = nowUtc;

    /// <summary>Each entry comes due again exactly <paramref name="snooze"/> from now.</summary>
    public void SnoozeCheckIns(IEnumerable<long> entryIds, TimeSpan snooze, DateTimeOffset nowUtc, TimeSpan interval)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        foreach (var id in entryIds)
        {
            _lastCheckIn[id] = nowUtc + snooze - interval;
        }
    }

    /// <summary>Re-shows the same scheduled nag at <paramref name="untilUtc"/>.</summary>
    public void SnoozeScheduled(Nag nag, DateTimeOffset untilUtc)
    {
        ArgumentNullException.ThrowIfNull(nag);
        _snoozedScheduled.Add((nag, untilUtc));
    }

    private static IEnumerable<Nag> Scheduled(ReminderState s)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.NowUtc, s.Tz).DateTime);
        var todayBit = 1 << (int)today.DayOfWeek;
        foreach (var r in s.Reminders)
        {
            if (!r.Enabled || (r.DaysMask & todayBit) == 0)
            {
                continue;
            }

            var occurrence = LocalTime.ToUtcLenient(today, r.TimeLocal, s.Tz);
            if (occurrence > s.NowUtc || (r.LastFiredUtc is { } last && last >= occurrence))
            {
                continue;
            }

            var title = r.Title;
            if (s.NowUtc - occurrence > MissedThreshold)
            {
                var local = TimeZoneInfo.ConvertTime(occurrence, s.Tz);
                title += string.Create(CultureInfo.CurrentCulture, $" (missed at {local:t})");
            }

            yield return new Nag(NagKind.Scheduled, title, r.Message, reminderId: r.Id);
        }
    }

    private Nag? NotTracking(ReminderState s)
    {
        var now = s.NowUtc;
        if (!s.Settings.NagNotTrackingEnabled
            || s.NotWorkingUntilUtc > now
            || _notTrackingSnoozedUntil > now
            || s.Running.Count > 0
            || WorkSchedule.CurrentShift(now, s.Settings, s.Tz) is not { } shift)
        {
            return null;
        }

        var since = Max(shift.StartUtc, Max(_notTrackingLastFired, s.LastStopUtc));
        if (now - since < TimeSpan.FromMinutes(s.Settings.NagNotTrackingIntervalMin))
        {
            return null;
        }

        _notTrackingLastFired = now;
        return new Nag(NagKind.NotTracking, "You're not tracking anything.", "", shift: shift);
    }

    private Nag? CheckIn(ReminderState s)
    {
        if (!s.Settings.CheckinEnabled || s.Running.Count == 0)
        {
            return null;
        }

        var interval = TimeSpan.FromMinutes(s.Settings.CheckinIntervalMin);
        var items = s.Running
            .Select(e => new CheckInItem(
                e,
                s.NowUtc - Max(e.StartUtc, _lastCheckIn.GetValueOrDefault(e.Id, appStartUtc)) >= interval))
            .ToList();
        if (!items.Any(i => i.Due))
        {
            return null;
        }

        var ordered = items.Where(i => i.Due).OrderBy(i => i.Entry.StartUtc)
            .Concat(items.Where(i => !i.Due).OrderBy(i => i.Entry.StartUtc))
            .ToList();
        var title = ordered.Count == 1 ? "Still working on this?" : "Still working on these?";
        return new Nag(NagKind.CheckIn, title, "", checkIns: ordered);
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset? b) => b is { } v && v > a ? v : a;

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : (a > b ? a : b);
}
