using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NaggingTimeTracker.App.Tracking;
using NaggingTimeTracker.App.Windows;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Services;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.App.ViewModels;

public sealed partial class QuickPanelViewModel : ObservableObject
{
    private const int RecentCount = 5;

    private readonly TrackingService _tracking;
    private readonly CatalogService _catalog;
    private readonly TrackingActions _actions;
    private readonly IEntryRepository _entries;
    private readonly WindowManager _windows;
    private readonly TimeProvider _time;

    public QuickPanelViewModel(
        TrackingService tracking,
        CatalogService catalog,
        TrackingActions actions,
        IEntryRepository entries,
        WindowManager windows,
        TimeProvider time)
    {
        _tracking = tracking;
        _catalog = catalog;
        _actions = actions;
        _entries = entries;
        _windows = windows;
        _time = time;
        _tracking.RunningEntriesChanged += (_, _) => Refresh();
    }

    /// <summary>Set by the window; dialogs are owned by it and run inside its modal guard.</summary>
    public IModalHost? Host { get; set; }

    public ObservableCollection<RunningRowViewModel> Running { get; } = [];

    public ObservableCollection<RecentRowViewModel> Recent { get; } = [];

    [ObservableProperty]
    private bool _showParallelBanner;

    [ObservableProperty]
    private string _parallelBannerText = "";

    [ObservableProperty]
    private bool _hasRunning;

    [ObservableProperty]
    private bool _hasRecent;

    [ObservableProperty]
    private IReadOnlyList<string> _customerNames = [];

    [ObservableProperty]
    private IReadOnlyList<string> _taskTypeNames = [];

    [ObservableProperty]
    private string _customerText = "";

    [ObservableProperty]
    private string _taskTypeText = "";

    [ObservableProperty]
    private string _externalLinkText = "";

    [ObservableProperty]
    private string _notesText = "";

    [ObservableProperty]
    private string? _requiredError;

    public void Refresh()
    {
        var names = _catalog.GetNames();
        var now = _time.NowUtc();

        CustomerNames = _catalog.GetActiveCustomers().Select(c => c.Name).ToList();
        TaskTypeNames = _catalog.GetActiveTaskTypes().Select(t => t.Name).ToList();

        ReconcileRunning(names, now);

        Recent.Clear();
        foreach (var e in _entries.GetRecentDistinct(RecentCount))
        {
            var label = $"{names.Customer(e.CustomerId)} · {names.TaskType(e.TaskTypeId)}";
            Recent.Add(new RecentRowViewModel(e.Id, e.ExternalLink is null ? label : $"{label} · {e.ExternalLink}"));
        }

        HasRecent = Recent.Count > 0;

        if (CustomerText.Length == 0 && TaskTypeText.Length == 0 && _entries.GetLatestStarted() is { } latest)
        {
            CustomerText = names.Customer(latest.CustomerId);
            TaskTypeText = names.TaskType(latest.TaskTypeId);
        }
    }

    /// <summary>Advances every row's elapsed time without touching the database.</summary>
    public void Tick()
    {
        var now = _time.NowUtc();
        foreach (var row in Running)
        {
            row.Tick(now);
        }
    }

    public void FlushAllNotes()
    {
        foreach (var row in Running.ToList())
        {
            row.FlushNotes();
        }
    }

    [RelayCommand]
    private void Start()
    {
        var customer = CustomerText.Trim();
        var taskType = TaskTypeText.Trim();
        if (customer.Length == 0 || taskType.Length == 0)
        {
            RequiredError = "Customer and task type are required.";
            return;
        }

        RequiredError = null;
        using (BeginModal())
        {
            var c = _catalog.GetOrCreateCustomer(customer);
            var t = _catalog.GetOrCreateTaskType(taskType);
            var result = _actions.Start(new StartRequest(c.Id, t.Id, ExternalLinkText, NotesText), Host?.Owner);
            if (result is StartResult.Started)
            {
                ExternalLinkText = "";
                NotesText = "";
                CustomerText = c.Name;
                TaskTypeText = t.Name;
            }
        }

        Refresh();
    }

    [RelayCommand]
    private void Stop(RunningRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        row.FlushNotes();
        using (BeginModal())
        {
            _actions.StopSingle(row.Id, Host?.Owner);
        }

        Refresh();
    }

    [RelayCommand]
    private void KeepOnly(RunningRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        FlushAllNotes();
        _tracking.StopAllExcept(row.Id);
        Refresh();
    }

    [RelayCommand]
    private void Continue(RecentRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        FlushAllNotes();
        using (BeginModal())
        {
            _actions.Continue(row.Id, Host?.Owner);
        }

        Refresh();
    }

    [RelayCommand]
    private void OpenEntries() => _windows.ShowEntries();

    [RelayCommand]
    private void OpenSettings() => _windows.ShowSettings();

    private IDisposable? BeginModal() => Host?.BeginModal();

    private void ReconcileRunning(CatalogNames names, DateTimeOffset now)
    {
        var running = _tracking.GetRunning();
        var ids = running.Select(e => e.Id).ToHashSet();

        for (var i = Running.Count - 1; i >= 0; i--)
        {
            if (!ids.Contains(Running[i].Id))
            {
                Running.RemoveAt(i);
            }
        }

        for (var i = 0; i < running.Count; i++)
        {
            var entry = running[i];
            var row = Running.FirstOrDefault(r => r.Id == entry.Id);
            if (row is null)
            {
                row = new RunningRowViewModel(entry.Id, _tracking);
                Running.Insert(Math.Min(i, Running.Count), row);
            }
            else if (Running.IndexOf(row) != i)
            {
                Running.Move(Running.IndexOf(row), i);
            }

            row.Apply(entry, names, now);
        }

        var parallel = Running.Count >= 2;
        foreach (var row in Running)
        {
            row.ShowKeepOnly = parallel;
        }

        HasRunning = Running.Count > 0;
        ShowParallelBanner = parallel;
        ParallelBannerText = $"⚠ {Running.Count} timers running in parallel";
    }
}
