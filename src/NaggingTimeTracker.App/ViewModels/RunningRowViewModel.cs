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
    private string _label = "";

    [ObservableProperty]
    private string _elapsed = "";

    [ObservableProperty]
    private string? _overlapReason;

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private bool _showKeepOnly;

    public void Apply(Entry entry, CatalogNames names, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(names);
        StartUtc = entry.StartUtc;
        Label = $"{names.Customer(entry.CustomerId)} · {names.TaskType(entry.TaskTypeId)}";
        OverlapReason = entry.OverlapReason;

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
