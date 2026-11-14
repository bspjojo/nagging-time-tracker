using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NaggingTimeTracker.App.Tracking;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Formatting;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Services;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.App.ViewModels;

public sealed partial class EntriesViewModel : ObservableObject
{
    private readonly IEntryRepository _entries;
    private readonly TrackingService _tracking;
    private readonly CatalogService _catalog;
    private readonly TrackingActions _actions;
    private readonly EntryEditorFactory _editors;
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _searchDebounce;
    private readonly DispatcherTimer _runningTicker;
    private bool _suppressReload;

    public EntriesViewModel(
        IEntryRepository entries,
        TrackingService tracking,
        CatalogService catalog,
        TrackingActions actions,
        EntryEditorFactory editors,
        TimeProvider time)
    {
        _entries = entries;
        _tracking = tracking;
        _catalog = catalog;
        _actions = actions;
        _editors = editors;
        _time = time;

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            Reload();
        };
        _runningTicker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _runningTicker.Tick += (_, _) => Reload();

        _tracking.RunningEntriesChanged += OnRunningEntriesChanged;

        _suppressReload = true;
        _preset = Presets[1]; // This week
        ApplyPresetDates(_preset.Preset);
        _suppressReload = false;
        Reload();
    }

    /// <summary>The window, for owning dialogs.</summary>
    public Window? Owner { get; set; }

    public IReadOnlyList<PresetOption> Presets { get; } =
    [
        new(DatePreset.Today, "Today"),
        new(DatePreset.ThisWeek, "This week"),
        new(DatePreset.LastWeek, "Last week"),
        new(DatePreset.ThisMonth, "This month"),
        new(DatePreset.Custom, "Custom"),
    ];

    public ObservableCollection<EntryRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private PresetOption _preset;

    [ObservableProperty]
    private DateTime? _fromDate;

    [ObservableProperty]
    private DateTime? _toDate;

    [ObservableProperty]
    private bool _isCustom;

    [ObservableProperty]
    private IReadOnlyList<FilterOption> _customerOptions = [];

    [ObservableProperty]
    private FilterOption? _customerFilter;

    [ObservableProperty]
    private IReadOnlyList<FilterOption> _taskTypeOptions = [];

    [ObservableProperty]
    private FilterOption? _taskTypeFilter;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string? _filterError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(ContinueCommand), nameof(DeleteCommand))]
    private EntryRowViewModel? _selected;

    [ObservableProperty]
    private string _summary = "";

    /// <summary>Unsubscribes and stops timers; called when the window closes.</summary>
    public void Detach()
    {
        _tracking.RunningEntriesChanged -= OnRunningEntriesChanged;
        _searchDebounce.Stop();
        _runningTicker.Stop();
    }

    public void Reload()
    {
        if (_suppressReload)
        {
            return;
        }

        RefreshFilterOptions();

        if (FromDate is not { } from || ToDate is not { } to)
        {
            FilterError = "Choose both dates.";
            return;
        }

        var fromDate = DateOnly.FromDateTime(from);
        var toDate = DateOnly.FromDateTime(to);
        if (toDate < fromDate)
        {
            FilterError = "The 'to' date is before the 'from' date.";
            return;
        }

        FilterError = null;
        var tz = _time.LocalTimeZone;
        var now = _time.NowUtc();
        var (fromUtc, toUtc) = LocalDateRange.ToUtcRange(fromDate, toDate, tz);
        var filter = new EntryFilter(fromUtc, toUtc, CustomerFilter?.Id, TaskTypeFilter?.Id, SearchText);

        var views = _entries.QueryViews(filter);
        var overlapIds = _entries.GetOverlappingIds(fromUtc, toUtc, now);
        var parallel = _tracking.GetRunning().Count >= 2;
        var selectedId = Selected?.Id;

        Rows.Clear();
        for (var i = views.Count - 1; i >= 0; i--) // newest first
        {
            var v = views[i];
            Rows.Add(new EntryRowViewModel(v, now, tz, overlapIds.Contains(v.Entry.Id), parallel));
        }

        Selected = selectedId is { } id ? Rows.FirstOrDefault(r => r.Id == id) : null;

        var total = TimeSpan.FromTicks(Rows.Sum(r => r.Length(now).Ticks));
        Summary = string.Create(
            CultureInfo.CurrentCulture,
            $"{Rows.Count} {(Rows.Count == 1 ? "entry" : "entries")} · Total {DurationFormat.HoursMinutes(total)}");

        // Running rows' durations and the total move with the clock.
        if (Rows.Any(r => r.IsRunning))
        {
            _runningTicker.Start();
        }
        else
        {
            _runningTicker.Stop();
        }
    }

    partial void OnPresetChanged(PresetOption value)
    {
        IsCustom = value.Preset == DatePreset.Custom;
        if (!IsCustom)
        {
            _suppressReload = true;
            ApplyPresetDates(value.Preset);
            _suppressReload = false;
        }

        // Choosing Custom keeps the dates of the preset that was active.
        Reload();
    }

    partial void OnFromDateChanged(DateTime? value) => Reload();

    partial void OnToDateChanged(DateTime? value) => Reload();

    partial void OnCustomerFilterChanged(FilterOption? value) => Reload();

    partial void OnTaskTypeFilterChanged(FilterOption? value) => Reload();

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
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

    private bool HasSelection() => Selected is not null;

    private void OnRunningEntriesChanged(object? sender, EventArgs e) => Reload();

    private void ApplyPresetDates(DatePreset preset)
    {
        var (from, to) = DatePresets.Range(preset, _time.TodayLocal());
        FromDate = from.ToDateTime(TimeOnly.MinValue);
        ToDate = to.ToDateTime(TimeOnly.MinValue);
    }

    private void RefreshFilterOptions()
    {
        var wasSuppressed = _suppressReload;
        _suppressReload = true;
        try
        {
            var customerId = CustomerFilter?.Id;
            var taskTypeId = TaskTypeFilter?.Id;

            // Replace a list only when it changed: a new ItemsSource makes the ComboBox reset its selection.
            var customers = Options("All customers", _catalog.GetAllCustomers().Select(c => (c.Id, c.Name, c.Archived)));
            if (!customers.SequenceEqual(CustomerOptions))
            {
                CustomerOptions = customers;
            }

            var taskTypes = Options("All task types", _catalog.GetAllTaskTypes().Select(t => (t.Id, t.Name, t.Archived)));
            if (!taskTypes.SequenceEqual(TaskTypeOptions))
            {
                TaskTypeOptions = taskTypes;
            }

            CustomerFilter = CustomerOptions.FirstOrDefault(o => o.Id == customerId) ?? CustomerOptions[0];
            TaskTypeFilter = TaskTypeOptions.FirstOrDefault(o => o.Id == taskTypeId) ?? TaskTypeOptions[0];
        }
        finally
        {
            _suppressReload = wasSuppressed;
        }
    }

    private static List<FilterOption> Options(string all, IEnumerable<(long Id, string Name, bool Archived)> items)
    {
        var list = new List<FilterOption> { new(null, all) };
        list.AddRange(items.Select(i => new FilterOption(i.Id, i.Archived ? $"{i.Name} (archived)" : i.Name)));
        return list;
    }
}
