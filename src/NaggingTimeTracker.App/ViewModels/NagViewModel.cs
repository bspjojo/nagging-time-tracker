using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NaggingTimeTracker.App.Reminders;
using NaggingTimeTracker.Core.Formatting;
using NaggingTimeTracker.Core.Reminders;

namespace NaggingTimeTracker.App.ViewModels;

/// <summary>One row of a check-in nag.</summary>
public sealed partial class NagCheckInRowViewModel(long id, string label, bool due) : ObservableObject
{
    public long Id { get; } = id;

    public string Label { get; } = label;

    public bool Due { get; } = due;

    [ObservableProperty]
    private bool _isHandled;
}

/// <summary>What the nag window shows; every button calls back into <see cref="NagWindowManager"/>.</summary>
public sealed partial class NagViewModel : ObservableObject
{
    private readonly NagWindowManager _manager;

    public NagViewModel(Nag nag, NagWindowManager manager)
    {
        ArgumentNullException.ThrowIfNull(nag);
        Nag = nag;
        _manager = manager;
        ContinueLabel = nag.Kind == NagKind.NotTracking ? manager.ContinueLabel() : null;
        Refresh();
    }

    public Nag Nag { get; }

    public string Title => Nag.Title;

    public string Message => Nag.Message;

    public bool HasMessage => Nag.Message.Length > 0;

    public bool IsNotTracking => Nag.Kind == NagKind.NotTracking;

    public bool IsCheckIn => Nag.Kind == NagKind.CheckIn;

    public bool IsScheduled => Nag.Kind == NagKind.Scheduled;

    /// <summary>Snooze 15 / 30 / 60 rows apply to not-tracking and check-in nags.</summary>
    public bool HasSnoozeChoices => IsNotTracking || IsCheckIn;

    public string? ContinueLabel { get; }

    /// <summary>"Not working" lasts until the shift ends: midnight when work hours are always on.</summary>
    public string NotWorkingLabel => Nag.Shift is { AllDay: true } ? "Not working today" : "Not working this shift";

    public bool HasContinue => ContinueLabel is not null;

    public ObservableCollection<NagCheckInRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private bool _showParallelHeader;

    [ObservableProperty]
    private string _parallelHeader = "";

    /// <summary>Re-reads the check-in rows (pruned entries, answered rows).</summary>
    public void Refresh()
    {
        if (!IsCheckIn)
        {
            return;
        }

        var names = _manager.Names();
        var now = _manager.Now();
        var handled = _manager.HandledCheckIns;
        var ids = Nag.CheckIns.Select(i => i.Entry.Id).ToHashSet();

        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (!ids.Contains(Rows[i].Id))
            {
                Rows.RemoveAt(i);
            }
        }

        foreach (var item in Nag.CheckIns)
        {
            var row = Rows.FirstOrDefault(r => r.Id == item.Entry.Id);
            if (row is null)
            {
                var e = item.Entry;
                row = new NagCheckInRowViewModel(
                    e.Id,
                    $"{names.Customer(e.CustomerId)} · {names.TaskType(e.TaskTypeId)} — {DurationFormat.Short(now - e.StartUtc)}",
                    item.Due);
                Rows.Add(row);
            }

            row.IsHandled = handled.Contains(row.Id);
        }

        ShowParallelHeader = Rows.Count >= 2;
        ParallelHeader = $"⚠ {Rows.Count} timers running in parallel";
    }

    [RelayCommand]
    private void Start() => _manager.Start();

    [RelayCommand]
    private void ContinueLast() => _manager.ContinueLast();

    [RelayCommand]
    private void NotWorking() => _manager.NotWorkingThisShift(Nag);

    [RelayCommand]
    private void Snooze(string minutes)
    {
        var by = TimeSpan.FromMinutes(int.Parse(minutes, System.Globalization.CultureInfo.InvariantCulture));
        if (IsNotTracking)
        {
            _manager.SnoozeNotTracking(by);
        }
        else if (IsCheckIn)
        {
            _manager.SnoozeCheckIns(Nag, by);
        }
        else
        {
            _manager.SnoozeScheduled(Nag, by);
        }
    }

    [RelayCommand]
    private void Dismiss() => _manager.Dismiss();

    [RelayCommand]
    private void CloseBox() => _manager.CloseBox(Nag);

    [RelayCommand]
    private void StillOnIt(NagCheckInRowViewModel row) => _manager.StillOnIt(row.Id);

    [RelayCommand]
    private void Stop(NagCheckInRowViewModel row) => _manager.StopEntry(row.Id);

    [RelayCommand]
    private void StopAndNote(NagCheckInRowViewModel row) => _manager.StopAndAddNote(row.Id);

    [RelayCommand]
    private void Switch(NagCheckInRowViewModel row) => _manager.Switch(row.Id);
}
