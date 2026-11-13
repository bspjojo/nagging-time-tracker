namespace NaggingTimeTracker.Core.Model;

public sealed record Entry
{
    public long Id { get; init; }
    public long CustomerId { get; init; }
    public long TaskTypeId { get; init; }
    public DateTimeOffset StartUtc { get; init; }

    /// <summary>Null while the entry is running.</summary>
    public DateTimeOffset? StopUtc { get; init; }

    public string? ExternalLink { get; init; }
    public string Notes { get; init; } = "";
    public long? ContinuedFromId { get; init; }

    /// <summary>Optional justification when the entry ran alongside another.</summary>
    public string? OverlapReason { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
}

/// <summary>An entry plus its customer and task type names (archived ones included).</summary>
public sealed record EntryView(Entry Entry, string CustomerName, string TaskTypeName);

/// <summary>
/// Filter for entry queries. <see cref="FromUtc"/>/<see cref="ToUtc"/> is a half-open range on
/// <c>start_utc</c>; <see cref="Text"/> matches notes or external link.
/// </summary>
public sealed record EntryFilter(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    long? CustomerId = null,
    long? TaskTypeId = null,
    string? Text = null);
