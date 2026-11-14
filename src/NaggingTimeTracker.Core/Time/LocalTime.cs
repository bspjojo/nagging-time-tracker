using System.Globalization;

namespace NaggingTimeTracker.Core.Time;

public sealed record LocalToUtcResult(DateTimeOffset? Utc, string? Error);

/// <summary>Typed local times (the editor's 24-hour time boxes) to UTC.</summary>
public static class LocalTime
{
    /// <summary>Accepts H:mm / HH:mm with ':' or '.', hours 0–23, minutes 0–59.</summary>
    public static bool TryParseTime(string? text, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split(':', '.');
        if (parts.Length != 2 || parts[0].Length is < 1 or > 2 || parts[1].Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            || h > 23 || m > 59)
        {
            return false;
        }

        time = new TimeOnly(h, m);
        return true;
    }

    /// <summary>
    /// A local date and time to UTC. A time skipped by a clock change is an error; an ambiguous
    /// one (clocks went back) resolves to the earlier instant.
    /// </summary>
    public static LocalToUtcResult ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(tz);
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (tz.IsInvalidTime(local))
        {
            return new LocalToUtcResult(null, "That time doesn't exist on this date (clocks went forward).");
        }

        var offset = tz.IsAmbiguousTime(local) ? tz.GetAmbiguousTimeOffsets(local).Max() : tz.GetUtcOffset(local);
        return new LocalToUtcResult(new DateTimeOffset(local, offset).ToUniversalTime(), null);
    }

    /// <summary>
    /// Like <see cref="ToUtc"/>, but for an edit box that was filled from <paramref name="original"/>:
    /// when the date and minute still show that instant's local date and HH:mm, the original
    /// instant is returned unchanged (seconds, milliseconds and the clocks-went-back half included).
    /// Only a changed field is converted, at minute precision.
    /// </summary>
    public static LocalToUtcResult ToUtcKeepingOriginal(DateOnly date, TimeOnly time, TimeZoneInfo tz, DateTimeOffset? original)
    {
        ArgumentNullException.ThrowIfNull(tz);
        if (original is { } kept)
        {
            var local = TimeZoneInfo.ConvertTime(kept, tz);
            if (DateOnly.FromDateTime(local.DateTime) == date && new TimeOnly(local.Hour, local.Minute) == time)
            {
                return new LocalToUtcResult(kept, null);
            }
        }

        return ToUtc(date, time, tz);
    }

    /// <summary>
    /// A local date and time to UTC that never fails (shift times, reminder occurrences, day
    /// starts): a time inside a DST gap moves forward to the first valid minute; an ambiguous
    /// time resolves to the earlier instant.
    /// </summary>
    public static DateTimeOffset ToUtcLenient(DateOnly date, TimeOnly time, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(tz);
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);

        var limit = local.AddHours(24);
        while (tz.IsInvalidTime(local) && local < limit)
        {
            local = local.AddMinutes(1);
        }

        var offset = tz.IsAmbiguousTime(local) ? tz.GetAmbiguousTimeOffsets(local).Max() : tz.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
