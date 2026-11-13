namespace NaggingTimeTracker.Core.Time;

/// <summary>
/// Converts an inclusive local date range to a half-open UTC range on <c>start_utc</c>.
/// An entry belongs to the local day it started on.
/// </summary>
public static class LocalDateRange
{
    public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) ToUtcRange(
        DateOnly from, DateOnly toInclusive, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(tz);
        if (toInclusive < from)
        {
            throw new ArgumentException("The 'to' date must not be before the 'from' date.", nameof(toInclusive));
        }

        return (StartOfLocalDay(from, tz), StartOfLocalDay(toInclusive.AddDays(1), tz));
    }

    private static DateTimeOffset StartOfLocalDay(DateOnly day, TimeZoneInfo tz)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        // A DST gap that starts at midnight: the day begins at the first valid minute.
        var limit = local.AddHours(24);
        while (tz.IsInvalidTime(local) && local < limit)
        {
            local = local.AddMinutes(1);
        }

        TimeSpan offset;
        if (tz.IsAmbiguousTime(local))
        {
            // The larger offset is the earlier instant: the first time this midnight occurs.
            offset = tz.GetAmbiguousTimeOffsets(local).Max();
        }
        else
        {
            offset = tz.GetUtcOffset(local);
        }

        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
