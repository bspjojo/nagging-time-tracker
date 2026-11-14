namespace NaggingTimeTracker.Core.Time;

public enum DatePreset
{
    Today,
    ThisWeek,
    LastWeek,
    ThisMonth,
    Custom,
}

/// <summary>Inclusive local date ranges for the filter presets. Weeks start on Monday.</summary>
public static class DatePresets
{
    public static (DateOnly From, DateOnly ToInclusive) Range(DatePreset preset, DateOnly today)
    {
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        return preset switch
        {
            DatePreset.Today => (today, today),
            DatePreset.ThisWeek => (monday, monday.AddDays(6)),
            DatePreset.LastWeek => (monday.AddDays(-7), monday.AddDays(-1)),
            DatePreset.ThisMonth => (
                new DateOnly(today.Year, today.Month, 1),
                new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month))),
            _ => throw new ArgumentException("A custom range comes from the date pickers.", nameof(preset)),
        };
    }
}
