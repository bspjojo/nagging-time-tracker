namespace NaggingTimeTracker.Core.Formatting;

/// <summary>Weekday masks (bit n = (DayOfWeek)n) as short text: "Mon–Fri", "Every day", "Sat, Sun".</summary>
public static class DaysMaskText
{
    // Monday-first, the order the UI shows the checkboxes in.
    private static readonly (DayOfWeek Day, string Name)[] Week =
    [
        (DayOfWeek.Monday, "Mon"), (DayOfWeek.Tuesday, "Tue"), (DayOfWeek.Wednesday, "Wed"),
        (DayOfWeek.Thursday, "Thu"), (DayOfWeek.Friday, "Fri"), (DayOfWeek.Saturday, "Sat"),
        (DayOfWeek.Sunday, "Sun"),
    ];

    public static string Format(int mask)
    {
        var on = Week.Select(d => (mask & (1 << (int)d.Day)) != 0).ToArray();
        var count = on.Count(x => x);
        if (count == 0)
        {
            return "Never";
        }

        if (count == 7)
        {
            return "Every day";
        }

        // A single Monday-first run of three or more days reads as a range.
        var first = Array.IndexOf(on, true);
        var last = Array.LastIndexOf(on, true);
        if (count >= 3 && last - first + 1 == count)
        {
            return $"{Week[first].Name}–{Week[last].Name}";
        }

        return string.Join(", ", Week.Where((_, i) => on[i]).Select(d => d.Name));
    }
}
