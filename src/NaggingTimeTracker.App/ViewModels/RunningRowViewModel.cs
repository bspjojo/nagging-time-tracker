using CommunityToolkit.Mvvm.ComponentModel;
using NaggingTimeTracker.Core.Formatting;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Services;

namespace NaggingTimeTracker.App.ViewModels;

/// <summary>A running entry in the quick panel. Updated in place on refresh so focus survives.</summary>
public sealed partial class RunningRowViewModel(long id, TrackingService tracking) : ObservableObject
{
    private string _savedNotes = "";

    public long Id { get; } = id;

    public DateTimeOffset StartUtc { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private string _label = "";

    [ObservableProperty]
    private string _elapsed = "";

    [ObservableProperty]
    private string? _overlapReason;

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private bool _showKeepOnly;

    /// <summary>The external link as stored, or null; shown under the label (as in Recent rows).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlainLink))]
    [NotifyPropertyChangedFor(nameof(AccessibleName))]
    private string? _link;

    /// <summary>Set only for an http(s) URL, which is shown as a hyperlink (<see cref="EntryText.IsWebUrl"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWebLink))]
    [NotifyPropertyChangedFor(nameof(HasPlainLink))]
    private Uri? _linkUri;

    public bool HasWebLink => LinkUri is not null;

    public bool HasPlainLink => !string.IsNullOrWhiteSpace(Link) && LinkUri is null;

    /// <summary>The row's name for screen readers and UI tests: the label plus the link.</summary>
    public string AccessibleName => string.IsNullOrWhiteSpace(Link) ? Label : $"{Label} · {Link}";

    public void Apply(Entry entry, CatalogNames names, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(names);
        StartUtc = entry.StartUtc;
        Label = $"{names.Customer(entry.CustomerId)} · {names.TaskType(entry.TaskTypeId)}";
        OverlapReason = entry.OverlapReason;
        Link = entry.ExternalLink;
        LinkUri = EntryText.IsWebUrl(entry.ExternalLink, out var uri) ? uri : null;

        // Don't clobber text being edited: only take the stored notes when there are no local edits.
        if (Notes == _savedNotes)
        {
            Notes = entry.Notes;
        }

        _savedNotes = entry.Notes;
        Tick(nowUtc);
    }

    public void Tick(DateTimeOffset nowUtc) => Elapsed = DurationFormat.Clock(nowUtc - StartUtc);

    /// <summary>Saves the notes if they changed since they were last saved or loaded.</summary>
    public void FlushNotes()
    {
        if (Notes == _savedNotes)
        {
            return;
        }

        _savedNotes = Notes;
        tracking.UpdateNotes(Id, Notes);
    }
}
