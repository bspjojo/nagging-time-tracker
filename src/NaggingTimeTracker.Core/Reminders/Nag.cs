using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.Core.Reminders;

public enum NagKind
{
    NotTracking,
    CheckIn,
    Scheduled,
}

/// <summary>A running entry listed on a check-in nag; <see cref="Due"/> ones need an answer.</summary>
public sealed record CheckInItem(Entry Entry, bool Due);

/// <summary>One nag to show. Equality is by reference (the queue tracks instances).</summary>
public sealed class Nag(
    NagKind kind,
    string title,
    string message,
    long? reminderId = null,
    IReadOnlyList<CheckInItem>? checkIns = null,
    Shift? shift = null)
{
    public NagKind Kind { get; } = kind;

    public string Title { get; } = title;

    public string Message { get; } = message;

    /// <summary>Scheduled nags: the reminder that fired.</summary>
    public long? ReminderId { get; } = reminderId;

    /// <summary>Check-in nags: due items first, then the other running entries.</summary>
    public IReadOnlyList<CheckInItem> CheckIns { get; internal set; } = checkIns ?? [];

    /// <summary>Not-tracking nags: the current shift (for "Not working this shift").</summary>
    public Shift? Shift { get; } = shift;
}
