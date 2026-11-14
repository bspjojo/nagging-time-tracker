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

    private static DateTimeOffset StartOfLocalDay(DateOnly day, TimeZoneInfo tz) =>
        LocalTime.ToUtcLenient(day, TimeOnly.MinValue, tz);
}
