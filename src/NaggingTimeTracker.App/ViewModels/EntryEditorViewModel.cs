using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Formatting;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Services;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.App.ViewModels;

/// <summary>Result of <see cref="EntryEditorViewModel.Save"/>.</summary>
public enum EditorSaveOutcome
{
    Saved,
    StayOpen,
    EntryGone,
}

/// <summary>Edit an existing entry or create a manual one. Built per dialog by <see cref="EntryEditorFactory"/>.</summary>
public sealed partial class EntryEditorViewModel : ObservableObject
{
    private const string TimeFormat = "HH:mm";

    private readonly IEntryRepository _entries;
    private readonly TrackingService _tracking;
    private readonly CatalogService _catalog;
    private readonly TimeProvider _time;
    private readonly Entry? _original;
    private readonly string _originalCustomer = "";
    private readonly string _originalTaskType = "";
    private bool _loading = true;

    public EntryEditorViewModel(
        Entry? original,
        IEntryRepository entries,
        TrackingService tracking,
        CatalogService catalog,
        TimeProvider time)
    {
        _original = original;
        _entries = entries;
        _tracking = tracking;
        _catalog = catalog;
        _time = time;

        CustomerNames = catalog.GetActiveCustomers().Select(c => c.Name).ToList();
        TaskTypeNames = catalog.GetActiveTaskTypes().Select(t => t.Name).ToList();
        var names = catalog.GetNames();
        var tz = time.LocalTimeZone;

        if (original is not null)
        {
            Title = "Edit entry";
            IsRunningEntry = original.StopUtc is null;
            SetLocal(original.StartUtc, tz, out var sd, out var st);
            StartDate = sd;
            StartTime = st;
            if (original.StopUtc is { } stop)
            {
                SetLocal(stop, tz, out var ed, out var et);
                StopDate = ed;
                StopTime = et;
            }
            else
            {
                StopDate = StartDate;
            }

            _originalCustomer = names.Customer(original.CustomerId);
            _originalTaskType = names.TaskType(original.TaskTypeId);
            CustomerText = _originalCustomer;
            TaskTypeText = _originalTaskType;
            ExternalLink = original.ExternalLink ?? "";
            Notes = original.Notes;
            OverlapReason = original.OverlapReason ?? "";

            if (original.ContinuedFromId is { } sourceId && entries.GetById(sourceId) is { } source)
            {
                ContinuedFromId = sourceId;
                var start = TimeZoneInfo.ConvertTime(source.StartUtc, tz);
                ContinuedFromText = string.Create(
                    CultureInfo.CurrentCulture,
                    $"Continued from: {start:d} {start:t} {names.Customer(source.CustomerId)} · {names.TaskType(source.TaskTypeId)}");
            }
        }
        else
        {
            Title = "New entry";
            var now = TimeZoneInfo.ConvertTime(time.NowUtc(), tz);
            var stopLocal = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute / 15 * 15, 0, DateTimeKind.Unspecified);
            var startLocal = stopLocal.AddHours(-1);
            StartDate = startLocal.Date;
            StartTime = startLocal.ToString(TimeFormat, CultureInfo.InvariantCulture);
            StopDate = stopLocal.Date;
            StopTime = stopLocal.ToString(TimeFormat, CultureInfo.InvariantCulture);
            if (entries.GetLatestStarted() is { } latest)
            {
                CustomerText = names.Customer(latest.CustomerId);
                TaskTypeText = names.TaskType(latest.TaskTypeId);
            }
        }

        _loading = false;
        Revalidate();
    }

    public string Title { get; }

    public bool IsRunningEntry { get; }

    public IReadOnlyList<string> CustomerNames { get; }

    public IReadOnlyList<string> TaskTypeNames { get; }

    public long? ContinuedFromId { get; }

    public string? ContinuedFromText { get; }

    public bool HasContinuedFrom => ContinuedFromText is not null;

    [ObservableProperty]
    private DateTime? _startDate;

    [ObservableProperty]
    private string _startTime = "";

    [ObservableProperty]
    private DateTime? _stopDate;

    [ObservableProperty]
    private string _stopTime = "";

    [ObservableProperty]
    private string _customerText = "";

    [ObservableProperty]
    private string _taskTypeText = "";

    [ObservableProperty]
    private string _externalLink = "";

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    private string _overlapReason = "";

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private string? _linkHint;

    [ObservableProperty]
    private bool _hasOverlaps;

    [ObservableProperty]
    private string? _overlapWarning;

    public string? RunningHint => IsRunningEntry ? "Running — leave Stop empty to keep it running." : null;

    partial void OnStartDateChanged(DateTime? value) => Revalidate();

    partial void OnStartTimeChanged(string value) => Revalidate();

    partial void OnStopDateChanged(DateTime? value) => Revalidate();

    partial void OnStopTimeChanged(string value) => Revalidate();

    partial void OnCustomerTextChanged(string value) => Revalidate();

    partial void OnTaskTypeTextChanged(string value) => Revalidate();

    partial void OnOverlapReasonChanged(string value) => Revalidate();

    partial void OnExternalLinkChanged(string value) =>
        LinkHint = !string.IsNullOrWhiteSpace(value) && !EntryText.IsWebUrl(value, out _)
            ? "Not a URL — saved as plain text"
            : null;

    /// <summary>Re-runs validation and the overlap query (e.g. after a nested editor closed).</summary>
    public void Revalidate()
    {
        if (_loading)
        {
            return;
        }

        Error = Validate(out var start, out var stop);
        if (start is { } s && Error is null)
        {
            var overlaps = _entries.GetOverlapping(s, stop, _original?.Id, _time.NowUtc());
            HasOverlaps = overlaps.Count > 0;
            OverlapWarning = HasOverlaps && string.IsNullOrWhiteSpace(OverlapReason)
                ? string.Create(CultureInfo.CurrentCulture, $"Overlaps {overlaps.Count} other {(overlaps.Count == 1 ? "entry" : "entries")}.")
                : null;
        }
    }

    public EditorSaveOutcome Save()
    {
        Revalidate(); // refreshes HasOverlaps for the current times
        var error = Validate(out var start, out var stop);
        if (error is not null || start is null)
        {
            Error = error;
            return EditorSaveOutcome.StayOpen;
        }

        var customerId = ResolveCustomer();
        var taskTypeId = ResolveTaskType();
        var reason = string.IsNullOrWhiteSpace(OverlapReason) ? null : OverlapReason.Trim();
        var entry = (_original ?? new Entry()) with
        {
            CustomerId = customerId,
            TaskTypeId = taskTypeId,
            StartUtc = start.Value,
            StopUtc = stop,
            ExternalLink = ExternalLink,
            Notes = Notes,

            // A reason is only editable while there are overlaps; otherwise the stored one is kept.
            OverlapReason = HasOverlaps ? reason : _original?.OverlapReason,
        };

        try
        {
            _tracking.Save(entry);
            return EditorSaveOutcome.Saved;
        }
        catch (InvalidOperationException) when (_original is not null)
        {
            var stored = _entries.GetById(_original.Id);
            if (stored is null)
            {
                return EditorSaveOutcome.EntryGone;
            }

            if (stop is null && stored.StopUtc is { } storedStop)
            {
                // Stopped elsewhere (tray, nag) while this dialog was open.
                SetLocal(storedStop, _time.LocalTimeZone, out var d, out var t);
                StopDate = d;
                StopTime = t;
                var local = TimeZoneInfo.ConvertTime(storedStop, _time.LocalTimeZone);
                Error = string.Create(
                    CultureInfo.CurrentCulture,
                    $"This entry was stopped at {local:t} while you were editing. Check the stop time and save again.");
                return EditorSaveOutcome.StayOpen;
            }

            throw;
        }
        catch (ArgumentException ex)
        {
            Error = ex.Message;
            return EditorSaveOutcome.StayOpen;
        }
    }

    private string? Validate(out DateTimeOffset? startUtc, out DateTimeOffset? stopUtc)
    {
        startUtc = null;
        stopUtc = null;
        var tz = _time.LocalTimeZone;

        if (StartDate is not { } sd || !LocalTime.TryParseTime(StartTime, out var st))
        {
            return "Start time must be like 09:30.";
        }

        var start = LocalTime.ToUtc(DateOnly.FromDateTime(sd), st, tz);
        if (start.Error is not null)
        {
            return "Start: " + start.Error;
        }

        startUtc = start.Utc;

        if (string.IsNullOrWhiteSpace(StopTime))
        {
            if (_original is null)
            {
                return "A new entry needs a stop time.";
            }

            if (!IsRunningEntry)
            {
                return "Stop can only be empty while the entry is running.";
            }
        }
        else
        {
            if (StopDate is not { } ed || !LocalTime.TryParseTime(StopTime, out var et))
            {
                return "Stop time must be like 17:30.";
            }

            var stop = LocalTime.ToUtc(DateOnly.FromDateTime(ed), et, tz);
            if (stop.Error is not null)
            {
                return "Stop: " + stop.Error;
            }

            stopUtc = stop.Utc;
            if (stopUtc <= startUtc)
            {
                return "Stop must be after start.";
            }
        }

        if (string.IsNullOrWhiteSpace(CustomerText) || string.IsNullOrWhiteSpace(TaskTypeText))
        {
            return "Customer and task type are required.";
        }

        return null;
    }

    // Keeping the current id when the text still names it avoids GetOrCreate un-archiving it.
    private long ResolveCustomer() =>
        _original is not null && SameName(CustomerText, _originalCustomer)
            ? _original.CustomerId
            : _catalog.GetOrCreateCustomer(CustomerText).Id;

    private long ResolveTaskType() =>
        _original is not null && SameName(TaskTypeText, _originalTaskType)
            ? _original.TaskTypeId
            : _catalog.GetOrCreateTaskType(TaskTypeText).Id;

    private static bool SameName(string typed, string current) =>
        string.Equals(typed.Trim(), current, StringComparison.OrdinalIgnoreCase);

    private static void SetLocal(DateTimeOffset utc, TimeZoneInfo tz, out DateTime? date, out string time)
    {
        var local = TimeZoneInfo.ConvertTime(utc, tz);
        date = local.Date;
        time = local.ToString(TimeFormat, CultureInfo.InvariantCulture);
    }
}
