using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NaggingTimeTracker.Core.Entries;
using NaggingTimeTracker.Core.Services;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.App.ViewModels;

/// <summary>
/// The date-range / customer / task type / text filter shared by the Entries and Export windows.
/// Raises <see cref="Changed"/> once per effective change (search text after a 300 ms pause).
/// </summary>
public sealed partial class FilterBarViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _searchDebounce;
    private bool _suppress;

    public FilterBarViewModel(CatalogService catalog, TimeProvider time, string automationPrefix)
    {
        _catalog = catalog;
        _time = time;
        AutomationPrefix = automationPrefix;

        _searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchDebounce.Tick += (_, _) =>
        {
            _searchDebounce.Stop();
            Raise();
        };

        _suppress = true;
        _preset = Presets[1]; // This week
        ApplyPresetDates(_preset.Preset);
        RefreshOptions();
        _suppress = false;
    }

    /// <summary>Raised after any effective filter change.</summary>
    public event EventHandler? Changed;

    /// <summary>"EntriesWindow" or "ExportWindow": the stem of the controls' AutomationIds.</summary>
    public string AutomationPrefix { get; }

    public IReadOnlyList<PresetOption> Presets { get; } =
    [
        new(DatePreset.Today, "Today"),
        new(DatePreset.ThisWeek, "This week"),
        new(DatePreset.LastWeek, "Last week"),
        new(DatePreset.ThisMonth, "This month"),
        new(DatePreset.Custom, "Custom"),
    ];

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

    /// <summary>The current state, or null while the dates are missing or invalid (see <see cref="FilterError"/>).</summary>
    public FilterState? Current
    {
        get
        {
            if (FromDate is not { } from || ToDate is not { } to)
            {
                FilterError = "Choose both dates.";
                return null;
            }

            var state = new FilterState(
                Preset.Preset,
                DateOnly.FromDateTime(from),
                DateOnly.FromDateTime(to),
                CustomerFilter?.Id,
                TaskTypeFilter?.Id,
                SearchText);
            FilterError = state.Validate();
            return FilterError is null ? state : null;
        }
    }

    /// <summary>Sets every field from <paramref name="state"/>, then raises <see cref="Changed"/> once.</summary>
    public void Apply(FilterState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _suppress = true;
        try
        {
            RefreshOptions();
            Preset = Presets.First(p => p.Preset == state.Preset);
            FromDate = state.From.ToDateTime(TimeOnly.MinValue);
            ToDate = state.ToInclusive.ToDateTime(TimeOnly.MinValue);
            CustomerFilter = CustomerOptions.FirstOrDefault(o => o.Id == state.CustomerId) ?? CustomerOptions[0];
            TaskTypeFilter = TaskTypeOptions.FirstOrDefault(o => o.Id == state.TaskTypeId) ?? TaskTypeOptions[0];
            SearchText = state.Text;
            _searchDebounce.Stop();
        }
        finally
        {
            _suppress = false;
        }

        Raise();
    }

    /// <summary>Reloads the customer and task type lists (archived included), keeping the selection.</summary>
    public void RefreshOptions()
    {
        var wasSuppressed = _suppress;
        _suppress = true;
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
            _suppress = wasSuppressed;
        }
    }

    public void Detach() => _searchDebounce.Stop();

    partial void OnPresetChanged(PresetOption value)
    {
        IsCustom = value.Preset == DatePreset.Custom;
        if (!IsCustom && !_suppress)
        {
            _suppress = true;
            ApplyPresetDates(value.Preset);
            _suppress = false;
        }

        // Choosing Custom keeps the dates of the preset that was active.
        Raise();
    }

    partial void OnFromDateChanged(DateTime? value) => Raise();

    partial void OnToDateChanged(DateTime? value) => Raise();

    partial void OnCustomerFilterChanged(FilterOption? value) => Raise();

    partial void OnTaskTypeFilterChanged(FilterOption? value) => Raise();

    partial void OnSearchTextChanged(string value)
    {
        if (_suppress)
        {
            return;
        }

        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void Raise()
    {
        if (!_suppress)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ApplyPresetDates(DatePreset preset)
    {
        var (from, to) = DatePresets.Range(preset, _time.TodayLocal());
        FromDate = from.ToDateTime(TimeOnly.MinValue);
        ToDate = to.ToDateTime(TimeOnly.MinValue);
    }

    private static List<FilterOption> Options(string all, IEnumerable<(long Id, string Name, bool Archived)> items)
    {
        var list = new List<FilterOption> { new(null, all) };
        list.AddRange(items.Select(i => new FilterOption(i.Id, i.Archived ? $"{i.Name} (archived)" : i.Name)));
        return list;
    }
}
