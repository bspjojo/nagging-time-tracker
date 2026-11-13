using System.Globalization;

namespace NaggingTimeTracker.Core.Formatting;

public static class DurationFormat
{
    /// <summary>Whole minutes, truncated: "1h 23m", "23m", "0m". Negative clamps to zero.</summary>
    public static string Short(TimeSpan d)
    {
        var minutes = (long)Math.Max(0, Math.Floor(d.TotalMinutes));
        var h = minutes / 60;
        var m = minutes % 60;
        return h > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{h}h {m}m")
            : string.Create(CultureInfo.InvariantCulture, $"{m}m");
    }

    /// <summary>"1:23:45", "0:00:07", unbounded hours ("26:00:00"). Negative clamps to zero.</summary>
    public static string Clock(TimeSpan d)
    {
        var seconds = (long)Math.Max(0, Math.Floor(d.TotalSeconds));
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}");
    }
}
