namespace NaggingTimeTracker.Core.Model;

/// <summary>A user-defined reminder at a local time on chosen weekdays.</summary>
public sealed record ScheduledReminder
{
    public long Id { get; init; }
    public required string Title { get; init; }
    public string Message { get; init; } = "";

    /// <summary>Bit n = (DayOfWeek)n, i.e. bit 0 = Sunday … bit 6 = Saturday.</summary>
    public int DaysMask { get; init; }

    /// <summary>Local wall-clock time, stored as 'HH:mm'.</summary>
    public TimeOnly TimeLocal { get; init; }

    public bool Enabled { get; init; } = true;
    public DateTimeOffset? LastFiredUtc { get; init; }
}
