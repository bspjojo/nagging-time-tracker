namespace NaggingTimeTracker.Core.Time;

/// <summary>
/// The single way Core reads "now": UTC, truncated to whole milliseconds so an in-memory
/// value equals its stored epoch-ms round-trip.
/// </summary>
public static class Clock
{
    public static DateTimeOffset NowUtc(this TimeProvider tp)
    {
        ArgumentNullException.ThrowIfNull(tp);
        var ticks = tp.GetUtcNow().UtcTicks;
        return new DateTimeOffset(ticks - ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
    }

    /// <summary>Today's date in the provider's local time zone.</summary>
    public static DateOnly TodayLocal(this TimeProvider tp)
    {
        ArgumentNullException.ThrowIfNull(tp);
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(tp.NowUtc(), tp.LocalTimeZone).DateTime);
    }
}
