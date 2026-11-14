using System.Windows.Threading;
using NaggingTimeTracker.App.Tracking;
using NaggingTimeTracker.App.Tray;
using NaggingTimeTracker.App.ViewModels;
using NaggingTimeTracker.App.Windows;
using NaggingTimeTracker.Core.Reminders;
using NaggingTimeTracker.Core.Services;
using NaggingTimeTracker.Core.Settings;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.App.Reminders;

/// <summary>
/// Shows nags one at a time (holding them during presentations), keeps the tray's attention
/// icon in step, prunes nags that no longer apply, and carries out the nag buttons' actions.
/// </summary>
public sealed class NagWindowManager
{
    private static readonly TimeSpan[] Snoozes = [TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(60)];

    private readonly NagQueue _queue = new();
    private readonly ReminderEngine _engine;
    private readonly TrackingService _tracking;
    private readonly TrackingActions _actions;
    private readonly EntryEditorFactory _editors;
    private readonly CatalogService _catalog;
    private readonly SettingsService _settings;
    private readonly PresentationState _presentation;
    private readonly TrayHost _tray;
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _presentationRecheck;
    private NagWindow? _window;

    public NagWindowManager(
        ReminderEngine engine,
        TrackingService tracking,
        TrackingActions actions,
        EntryEditorFactory editors,
        CatalogService catalog,
        SettingsService settings,
        PresentationState presentation,
        TrayHost tray,
        TimeProvider time)
    {
        _engine = engine;
        _tracking = tracking;
        _actions = actions;
        _editors = editors;
        _catalog = catalog;
        _settings = settings;
        _presentation = presentation;
        _tray = tray;
        _time = time;

        _presentationRecheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _presentationRecheck.Tick += (_, _) => ShowNext();
        _tracking.RunningEntriesChanged += (_, _) => OnRunningEntriesChanged();
    }

    /// <summary>A nag is shown or queued ("prompt open" for the engine).</summary>
    public bool HasPending => _queue.HasPending;

    public static IReadOnlyList<TimeSpan> SnoozeChoices => Snoozes;

    public void Enqueue(IEnumerable<Nag> nags)
    {
        _queue.Enqueue(nags);
        ShowNext();
    }

    // ---- Actions (called by NagViewModel) --------------------------------------------------------

    public void Start()
    {
        Close();
        _tray.ShowQuickPanel();
    }

    public void ContinueLast()
    {
        Close();
        _actions.ContinueLast(owner: null);
    }

    public void SnoozeNotTracking(TimeSpan by)
    {
        Close();
        _engine.SnoozeNotTracking(_time.NowUtc() + by);
    }

    public void NotWorkingThisShift(Nag nag)
    {
        Close();
        if (nag.Shift is { } shift)
        {
            _settings.SetInstant(SettingKeys.NotWorkingUntilUtc, shift.EndUtc);
        }
    }

    public void StillOnIt(long entryId)
    {
        _engine.CheckedIn(entryId, _time.NowUtc());
        Resolve(entryId);
    }

    public void StopEntry(long entryId)
    {
        // No owner: the stop prunes the nag, which could close an owning window under the prompt.
        _actions.StopSingle(entryId, owner: null);
        Resolve(entryId);
    }

    public void StopAndAddNote(long entryId)
    {
        Close();
        _tracking.Stop(entryId);
        _editors.Show(entryId, owner: null);
    }

    public void Switch(long entryId)
    {
        Close();
        _tracking.Stop(entryId);
        _tray.ShowQuickPanel();
    }

    public void SnoozeCheckIns(Nag nag, TimeSpan by)
    {
        // Read the rows before closing: closing clears the handled set. Answered and not-due rows
        // keep their own clock.
        var ids = ReferenceEquals(_queue.Current, nag) ? _queue.UnansweredDueCheckIns() : [];
        Close();
        var interval = TimeSpan.FromMinutes(_settings.Load().CheckinIntervalMin);
        _engine.SnoozeCheckIns(ids, by, _time.NowUtc(), interval);
    }

    public void Dismiss() => Close();

    public void SnoozeScheduled(Nag nag, TimeSpan by)
    {
        Close();
        _engine.SnoozeScheduled(nag, _time.NowUtc() + by);
    }

    /// <summary>The window's ✕: the kind's 15-minute snooze.</summary>
    public void CloseBox(Nag nag)
    {
        switch (nag.Kind)
        {
            case NagKind.NotTracking:
                SnoozeNotTracking(Snoozes[0]);
                break;
            case NagKind.CheckIn:
                SnoozeCheckIns(nag, Snoozes[0]);
                break;
            default:
                SnoozeScheduled(nag, Snoozes[0]);
                break;
        }
    }

    /// <summary>"Continue {customer · type}" for the not-tracking nag, or null when nothing has stopped yet.</summary>
    public string? ContinueLabel()
    {
        if (_tracking.LastStopped() is not { } last)
        {
            return null;
        }

        var names = _catalog.GetNames();
        return $"Continue {names.Customer(last.CustomerId)} · {names.TaskType(last.TaskTypeId)}";
    }

    public CatalogNames Names() => _catalog.GetNames();

    public DateTimeOffset Now() => _time.NowUtc();

    public IReadOnlySet<long> HandledCheckIns => _queue.HandledCheckIns;

    // ---- Queue and window ------------------------------------------------------------------------

    private void Resolve(long entryId)
    {
        _queue.ResolveCheckIn(entryId);
        Sync();
    }

    private void Close()
    {
        _queue.CloseCurrent();
        Sync();
    }

    private void OnRunningEntriesChanged()
    {
        var running = _tracking.GetRunning().Select(e => e.Id).ToHashSet();
        _queue.Prune(running);
        Sync();
    }

    private void ShowNext()
    {
        _queue.ShowNextIfIdle(_presentation.IsBusy());
        Sync();
    }

    /// <summary>Brings the window, the presentation re-check and the tray icon in line with the queue.</summary>
    private void Sync()
    {
        if (_queue.Current is null)
        {
            _queue.ShowNextIfIdle(_presentation.IsBusy());
        }

        var current = _queue.Current;
        if (current is null)
        {
            if (_window is not null)
            {
                var w = _window;
                _window = null;
                w.CloseQuietly();
            }
        }
        else if (_window is null || !ReferenceEquals(_window.Nag, current))
        {
            _window?.CloseQuietly();
            _window = new NagWindow(new NagViewModel(current, this));
            _window.Show();
        }
        else
        {
            _window.Refresh();
        }

        // Nags waiting only because of a presentation: re-check every 30 s.
        if (_queue.HasPending && _queue.Current is null)
        {
            _presentationRecheck.Start();
        }
        else
        {
            _presentationRecheck.Stop();
        }

        _tray.SetAttention(_queue.HasPending);
    }
}
