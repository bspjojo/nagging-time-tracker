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
}
