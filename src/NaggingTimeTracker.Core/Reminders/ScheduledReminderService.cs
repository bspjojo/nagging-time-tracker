using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.Core.Reminders;

/// <summary>
/// The only save path for custom reminders. Saving never makes a reminder fire at once: if
/// today's occurrence has already passed, it's marked as fired, so the first fire is the next
/// occurrence.
/// </summary>
public sealed class ScheduledReminderService(IReminderRepository repo, TimeProvider time)
{
    public IReadOnlyList<ScheduledReminder> GetAll() => repo.GetAll();

    public ScheduledReminder Save(ScheduledReminder r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (string.IsNullOrWhiteSpace(r.Title))
        {
            throw new ArgumentException("A title is required.", nameof(r));
        }

        if (r.DaysMask is < 1 or > 127)
        {
            throw new ArgumentException("Pick at least one day.", nameof(r));
        }

        var now = time.NowUtc();
        var tz = time.LocalTimeZone;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, tz).DateTime);
        var firesToday = (r.DaysMask & (1 << (int)today.DayOfWeek)) != 0;
        var passed = LocalTime.ToUtcLenient(today, r.TimeLocal, tz) <= now;

        // Otherwise LastFiredUtc stays as given (null for a new reminder).
        var toSave = r with
        {
            Title = r.Title.Trim(),
            Message = r.Message ?? "",
            LastFiredUtc = firesToday && passed ? now : r.LastFiredUtc,
        };

        if (toSave.Id == 0)
        {
            return repo.Insert(toSave);
        }

        repo.Update(toSave);
        return toSave;
    }

    public bool Delete(long id) => repo.Delete(id);

    public bool SetEnabled(long id, bool enabled)
    {
        if (repo.GetById(id) is not { } r)
        {
            return false;
        }

        Save(r with { Enabled = enabled });
        return true;
    }
}
