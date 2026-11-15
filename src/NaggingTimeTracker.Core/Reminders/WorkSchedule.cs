using NaggingTimeTracker.Core.Settings;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.Core.Reminders;

/// <summary>One work shift, [StartUtc, EndUtc). <paramref name="AllDay"/>: an always-on day, midnight to midnight.</summary>
public sealed record Shift(DateTimeOffset StartUtc, DateTimeOffset EndUtc, bool AllDay = false);

/// <summary>
/// Work shifts from the work-hours settings. A shift starts on each ticked weekday at the start
/// time and ends at the end time the same day, or the next day when end is before start
/// (overnight). The weekday ticks refer to the day a shift starts. With "always on", every
/// local day is one shift, midnight to midnight, whatever the days and times say.
/// </summary>
public static class WorkSchedule
{
    public static string? Validate(TimeOnly start, TimeOnly end) =>
        start == end ? "Start and end can't be the same." : null;

    public static bool IsOvernight(TimeOnly start, TimeOnly end) => end < start;

    /// <summary>The shift containing <paramref name="nowUtc"/>, or null outside work hours.</summary>
    public static Shift? CurrentShift(DateTimeOffset nowUtc, AppSettings s, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(tz);
        if (s.WorkAlwaysOn)
        {
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, tz).DateTime);
            return new Shift(
                LocalTime.ToUtcLenient(day, TimeOnly.MinValue, tz),
                LocalTime.ToUtcLenient(day.AddDays(1), TimeOnly.MinValue, tz),
                AllDay: true);
        }

        if (Validate(s.WorkStart, s.WorkEnd) is not null)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, tz).DateTime);
        foreach (var day in new[] { today.AddDays(-1), today })
        {
            if ((s.WorkDaysMask & (1 << (int)day.DayOfWeek)) == 0)
            {
                continue;
            }

            var start = LocalTime.ToUtcLenient(day, s.WorkStart, tz);
            var endDay = IsOvernight(s.WorkStart, s.WorkEnd) ? day.AddDays(1) : day;
            var end = LocalTime.ToUtcLenient(endDay, s.WorkEnd, tz);
            if (start <= nowUtc && nowUtc < end)
            {
                return new Shift(start, end);
            }
        }

        return null;
    }
}
