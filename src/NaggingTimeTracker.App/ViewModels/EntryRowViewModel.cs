using System.Globalization;
using NaggingTimeTracker.Core.Formatting;
using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.App.ViewModels;

/// <summary>One row of the Entries grid. Immutable; the grid rebuilds rows on reload.</summary>
public sealed class EntryRowViewModel
{
    public EntryRowViewModel(EntryView view, DateTimeOffset nowUtc, TimeZoneInfo tz, bool overlaps, bool parallel)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(tz);
        var e = view.Entry;
        Entry = e;

        var start = TimeZoneInfo.ConvertTime(e.StartUtc, tz);
        Date = start.ToString("d", CultureInfo.CurrentCulture);
        Start = start.ToString("t", CultureInfo.CurrentCulture);
        if (e.StopUtc is { } stopUtc)
        {
            var stop = TimeZoneInfo.ConvertTime(stopUtc, tz);
            Stop = stop.ToString("t", CultureInfo.CurrentCulture)
                + EntryText.StopSuffix(DateOnly.FromDateTime(start.DateTime), DateOnly.FromDateTime(stop.DateTime));
        }

        Customer = view.CustomerName;
        TaskType = view.TaskTypeName;
        Link = e.ExternalLink ?? "";
        LinkUri = EntryText.IsWebUrl(e.ExternalLink, out var uri) ? uri : null;
        Notes = e.Notes;
        NotesFirstLine = EntryText.FirstLine(e.Notes);
        Overlaps = overlaps;
        OverlapToolTip = string.IsNullOrWhiteSpace(e.OverlapReason) ? "Overlaps another entry" : e.OverlapReason;
        HighlightParallel = IsRunning && parallel;
        Duration = DurationFormat.HoursMinutes(Length(nowUtc));
    }

    public Entry Entry { get; }

    public long Id => Entry.Id;

    public string Date { get; }

    public string Start { get; }

    public string Stop { get; } = "";

    public bool IsRunning => Entry.StopUtc is null;

    public string Duration { get; }

    public string Customer { get; }

    public string TaskType { get; }

    public string Link { get; }

    public Uri? LinkUri { get; }

    public bool HasWebLink => LinkUri is not null;

    public string Notes { get; }

    public string NotesFirstLine { get; }

    public bool IsContinuation => Entry.ContinuedFromId is not null;

    public bool Overlaps { get; }

    public string OverlapToolTip { get; }

    public bool HighlightParallel { get; }

    /// <summary>What screen readers and UI tests see for the row.</summary>
    public string AccessibleName =>
        $"{Date} {Start}–{(IsRunning ? "running" : Stop)}, {Customer} · {TaskType}, {Duration}";

    /// <summary>Running entries are measured to <paramref name="nowUtc"/>.</summary>
    public TimeSpan Length(DateTimeOffset nowUtc) => (Entry.StopUtc ?? nowUtc) - Entry.StartUtc;
}
