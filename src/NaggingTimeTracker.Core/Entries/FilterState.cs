using System.Globalization;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.Core.Entries;

/// <summary>
/// The Entries/Export filter bar's state: an inclusive local date range plus optional customer,
/// task type and text. Passed from the Entries window to pre-fill the Export window.
/// </summary>
public sealed record FilterState(
    DatePreset Preset,
    DateOnly From,
    DateOnly ToInclusive,
    long? CustomerId,
    long? TaskTypeId,
    string Text)
{
    /// <summary>An error message, or null when the state is usable.</summary>
    public string? Validate() =>
        ToInclusive < From ? "The 'to' date is before the 'from' date." : null;

    /// <summary>The half-open UTC filter on start_utc. Throws when <see cref="Validate"/> reports an error.</summary>
    public EntryFilter ToEntryFilter(TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(tz);
        if (Validate() is { } error)
        {
            throw new InvalidOperationException(error);
        }

        var (fromUtc, toUtc) = LocalDateRange.ToUtcRange(From, ToInclusive, tz);
        return new EntryFilter(fromUtc, toUtc, CustomerId, TaskTypeId, Text);
    }

    /// <summary>"timesheet-2026-03-02_to_2026-03-08.csv", using the inclusive "to" date.</summary>
    public string DefaultFileName() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"timesheet-{From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}_to_{ToInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.csv");
}
