using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NaggingTimeTracker.App.Tracking;
using NaggingTimeTracker.App.Windows;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Formatting;
using NaggingTimeTracker.Core.Services;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.App.ViewModels;

public sealed partial class EntriesViewModel : ObservableObject
{
    private readonly IEntryRepository _entries;
    private readonly TrackingService _tracking;
    private readonly TrackingActions _actions;
    private readonly EntryEditorFactory _editors;
    private readonly WindowManager _windows;
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _runningTicker;

    public EntriesViewModel(
        IEntryRepository entries,
        TrackingService tracking,
        CatalogService catalog,
        TrackingActions actions,
        EntryEditorFactory editors,
        WindowManager windows,
        TimeProvider time)
    {
        _entries = entries;
        _tracking = tracking;
        _actions = actions;
        _editors = editors;
        _windows = windows;
        _time = time;

        Filter = new FilterBarViewModel(catalog, time, "EntriesWindow");
        Filter.Changed += (_, _) => Reload();
        Filter.PropertyChanged += (_, e) =>
        {
            // An invalid range doesn't raise Changed, but it does change whether Export is possible.
            if (e.PropertyName == nameof(FilterBarViewModel.FilterError))
            {
                ExportCommand.NotifyCanExecuteChanged();
            }
        };

        _runningTicker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _runningTicker.Tick += (_, _) => TickRunning();

        _tracking.RunningEntriesChanged += OnRunningEntriesChanged;
        Reload();
    }

    /// <summary>The window, for owning dialogs.</summary>
    public Window? Owner { get; set; }

    public FilterBarViewModel Filter { get; }

    public ObservableCollection<EntryRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(ContinueCommand), nameof(DeleteCommand))]
    private EntryRowViewModel? _selected;

    [ObservableProperty]
    private string _summary = "";

    /// <summary>Unsubscribes and stops timers; called when the window closes.</summary>
    public void Detach()
    {
        _tracking.RunningEntriesChanged -= OnRunningEntriesChanged;
        _runningTicker.Stop();
        Filter.Detach();
    }

    public void Reload()
    {
        Filter.RefreshOptions();
        ExportCommand.NotifyCanExecuteChanged();
        if (Filter.Current is not { } state)
        {
            return;
        }

        var tz = _time.LocalTimeZone;
        var now = _time.NowUtc();
        var filter = state.ToEntryFilter(tz);

        var views = _entries.QueryViews(filter);
        var overlapIds = _entries.GetOverlappingIds(filter.FromUtc, filter.ToUtc, now);
        var parallel = _tracking.GetRunning().Count >= 2;
        var selectedId = Selected?.Id;

        Rows.Clear();
        for (var i = views.Count - 1; i >= 0; i--) // newest first
        {
            var v = views[i];
            Rows.Add(new EntryRowViewModel(v, now, tz, overlapIds.Contains(v.Entry.Id), parallel));
        }

        Selected = selectedId is { } id ? Rows.FirstOrDefault(r => r.Id == id) : null;

        UpdateSummary(now);

        // Running rows' durations and the total move with the clock; TickRunning advances them in place.
        if (Rows.Any(r => r.IsRunning))
        {
            _runningTicker.Start();
        }
        else
        {
            _runningTicker.Stop();
        }
    }

    /// <summary>
    /// Advances the running rows' durations and the total without re-querying: only the clock moved.
    /// Any real change (a start or stop anywhere, an edit, a delete, a filter change) reloads instead.
    /// </summary>
    private void TickRunning()
    {
        var now = _time.NowUtc();
        foreach (var row in Rows)
        {
            row.Tick(now);
        }

        UpdateSummary(now);
    }

    private void UpdateSummary(DateTimeOffset now)
    {
        var total = TimeSpan.FromTicks(Rows.Sum(r => r.Length(now).Ticks));
        Summary = string.Create(
            CultureInfo.CurrentCulture,
            $"{Rows.Count} {(Rows.Count == 1 ? "entry" : "entries")} · Total {DurationFormat.HoursMinutes(total)}");
    }

    [RelayCommand]
    private void NewEntry()
    {
        _editors.Show(entryId: null, Owner);
        Reload();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Edit()
    {
        _editors.Show(Selected!.Id, Owner);
        Reload();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Continue()
    {
        _actions.Continue(Selected!.Id, Owner);
        Reload();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        var row = Selected!;
        var when = row.IsRunning ? $"{row.Date} {row.Start} (running)" : $"{row.Date} {row.Start}–{row.Stop}";
        var text = $"Delete the entry {when}, {row.Customer} · {row.TaskType} ({row.Duration})?";
        var answer = Owner is null
            ? MessageBox.Show(text, "Delete entry", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            : MessageBox.Show(Owner, text, "Delete entry", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes)
        {
            _tracking.Delete(row.Id);
            Reload();
        }
    }

    /// <summary>Opens the Export window pre-filled with the filters on screen.</summary>
    [RelayCommand(CanExecute = nameof(HasValidFilter))]
    private void Export() => _windows.ShowExport(Filter.Current);

    private bool HasSelection() => Selected is not null;

    private bool HasValidFilter() => Filter.FilterError is null && Filter.FromDate is not null && Filter.ToDate is not null;

    private void OnRunningEntriesChanged(object? sender, EventArgs e) => Reload();
}
