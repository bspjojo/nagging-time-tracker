using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Reminders;
using NaggingTimeTracker.Core.Settings;

namespace NaggingTimeTracker.Tests.Core;

public sealed class ReminderEngineTests
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    // Mon 2026-03-02 09:00 New York (14:00Z). Work hours default to Mon–Fri 09:00–17:00.
    private static readonly DateTimeOffset ShiftStart = new(2026, 3, 2, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset AppStart = ShiftStart.AddHours(-2);

    private readonly ReminderEngine _engine = new(AppStart);

    private static DateTimeOffset At(double minutesAfterShiftStart) => ShiftStart.AddMinutes(minutesAfterShiftStart);

    private static Entry Running(long id, DateTimeOffset start) => new() { Id = id, CustomerId = 1, TaskTypeId = 1, StartUtc = start };

    private static ReminderState State(
        DateTimeOffset now,
        IReadOnlyList<Entry>? running = null,
        AppSettings? settings = null,
        DateTimeOffset? lastStop = null,
        IReadOnlyList<ScheduledReminder>? reminders = null,
        DateTimeOffset? paused = null,
        DateTimeOffset? notWorking = null,
        bool idle = false,
        bool busy = false,
        bool promptOpen = false) =>
        new(now, NewYork, settings ?? new AppSettings(), running ?? [], lastStop, reminders ?? [], paused, notWorking, idle, busy, promptOpen);

    private static AppSettings NoCheckIns => new() { CheckinEnabled = false };

    private static AppSettings NoNotTracking => new() { NagNotTrackingEnabled = false };

    private IReadOnlyList<Nag> Kind(ReminderState s, NagKind kind) => _engine.Evaluate(s).Where(n => n.Kind == kind).ToList();

    // ---- Not tracking -------------------------------------------------------------------------

    [Fact]
    public void Not_tracking_fires_only_inside_a_shift_after_the_interval_from_shift_start()
    {
        Assert.Empty(Kind(State(At(-30)), NagKind.NotTracking));   // before the shift
        Assert.Empty(Kind(State(At(14)), NagKind.NotTracking));    // 14 min into it
        var nag = Assert.Single(Kind(State(At(15)), NagKind.NotTracking));
        Assert.Equal("You're not tracking anything.", nag.Title);
        Assert.Equal(new Shift(ShiftStart, ShiftStart.AddHours(8)), nag.Shift);
        Assert.Empty(Kind(State(At(8 * 60 + 30)), NagKind.NotTracking)); // after the shift
    }

    [Fact]
    public void Not_tracking_interval_is_measured_from_the_last_fire_and_the_last_stop()
    {
        Assert.Single(Kind(State(At(15)), NagKind.NotTracking));
        Assert.Empty(Kind(State(At(29)), NagKind.NotTracking));
        Assert.Single(Kind(State(At(30)), NagKind.NotTracking));

        // A stop at +40 restarts the clock: nothing until +55.
        Assert.Empty(Kind(State(At(50), lastStop: At(40)), NagKind.NotTracking));
        Assert.Single(Kind(State(At(55), lastStop: At(40)), NagKind.NotTracking));
    }

    [Fact]
    public void Not_tracking_is_silent_while_anything_runs_or_when_disabled()
    {
        Assert.Empty(Kind(State(At(60), running: [Running(1, At(0))], settings: NoCheckIns), NagKind.NotTracking));
        Assert.Empty(Kind(State(At(60), settings: NoNotTracking), NagKind.NotTracking));
    }

    [Fact]
    public void Not_tracking_honours_snooze()
    {
        _engine.SnoozeNotTracking(At(45));
        Assert.Empty(Kind(State(At(30)), NagKind.NotTracking));
        Assert.Single(Kind(State(At(45)), NagKind.NotTracking));
    }

    [Fact]
    public void Not_working_this_shift_lasts_until_the_shift_end_including_overnight()
    {
        Assert.Empty(Kind(State(At(60), notWorking: At(8 * 60)), NagKind.NotTracking));

        // Overnight Mon 22:00 → Tue 06:00 (03:00Z → 11:00Z); "not working" until 11:00Z.
        var overnight = new AppSettings { WorkStart = new TimeOnly(22, 0), WorkEnd = new TimeOnly(6, 0), CheckinEnabled = false };
        var end = new DateTimeOffset(2026, 3, 3, 11, 0, 0, TimeSpan.Zero);
        Assert.Empty(Kind(State(end.AddHours(-2), settings: overnight, notWorking: end), NagKind.NotTracking));

        // Tue 22:30: the next shift, after "not working" has expired.
        var nextShift = new DateTimeOffset(2026, 3, 4, 3, 30, 0, TimeSpan.Zero);
        Assert.Single(Kind(State(nextShift, settings: overnight, notWorking: end), NagKind.NotTracking));
    }

    // ---- Check-in -----------------------------------------------------------------------------

    [Fact]
    public void Check_in_is_due_an_interval_after_the_entry_started()
    {
        var e = Running(1, At(0));
        Assert.Empty(Kind(State(At(59), running: [e]), NagKind.CheckIn));
        var nag = Assert.Single(Kind(State(At(60), running: [e]), NagKind.CheckIn));
        Assert.Equal("Still working on this?", nag.Title);
        Assert.True(Assert.Single(nag.CheckIns).Due);
    }

    [Fact]
    public void Entries_due_on_the_same_tick_share_one_nag()
    {
        var nag = Assert.Single(Kind(State(At(70), running: [Running(1, At(0)), Running(2, At(5))]), NagKind.CheckIn));

        Assert.Equal("Still working on these?", nag.Title);
        Assert.Equal([true, true], nag.CheckIns.Select(i => i.Due));
    }

    [Fact]
    public void With_two_running_and_one_due_the_nag_lists_both_due_first()
    {
        var nag = Assert.Single(Kind(State(At(65), running: [Running(2, At(30)), Running(1, At(0))]), NagKind.CheckIn));

        Assert.Equal([(1L, true), (2L, false)], nag.CheckIns.Select(i => (i.Entry.Id, i.Due)));
    }

    [Fact]
    public void Still_on_it_resets_the_entry_clock()
    {
        var e = Running(1, At(0));
        _engine.CheckedIn(1, At(60));

        Assert.Empty(Kind(State(At(119), running: [e]), NagKind.CheckIn));
        Assert.Single(Kind(State(At(120), running: [e]), NagKind.CheckIn));
    }

    [Fact]
    public void Snooze_brings_the_check_in_back_exactly_that_much_later()
    {
        var e = Running(1, At(0));
        _engine.SnoozeCheckIns([1], TimeSpan.FromMinutes(15), At(60), TimeSpan.FromMinutes(60));

        Assert.Empty(Kind(State(At(74), running: [e]), NagKind.CheckIn));
        Assert.Single(Kind(State(At(75), running: [e]), NagKind.CheckIn));
    }

    [Fact]
    public void No_burst_after_restart_for_entries_started_long_before()
    {
        var old = Running(1, AppStart.AddHours(-5));

        Assert.Empty(Kind(State(AppStart.AddMinutes(30), running: [old]), NagKind.CheckIn));
        Assert.Single(Kind(State(AppStart.AddMinutes(60), running: [old]), NagKind.CheckIn));
    }

    [Fact]
    public void Stopped_entries_are_pruned_from_the_bookkeeping()
    {
        _engine.CheckedIn(1, At(60));
        _engine.Evaluate(State(At(61), running: [Running(2, At(0))]));

        // Entry 1 runs again (same id can't really recur, but the clock must not survive a prune).
        Assert.Single(Kind(State(At(62), running: [Running(1, At(0))]), NagKind.CheckIn));
    }

    [Fact]
    public void Check_in_is_silent_when_disabled()
    {
        Assert.Empty(Kind(State(At(120), running: [Running(1, At(0))], settings: NoCheckIns), NagKind.CheckIn));
    }

    // ---- Scheduled ----------------------------------------------------------------------------

    private static ScheduledReminder Reminder(int hour, int minute, DateTimeOffset? lastFired = null, int days = 127, bool enabled = true) =>
        new() { Id = 7, Title = "Timesheet", Message = "Submit it", DaysMask = days, TimeLocal = new TimeOnly(hour, minute), Enabled = enabled, LastFiredUtc = lastFired };

    [Fact]
    public void Scheduled_fires_once_per_occurrence()
    {
        // 10:00 local = ShiftStart + 60 min.
        Assert.Empty(Kind(State(At(59), reminders: [Reminder(10, 0)], settings: NoNotTracking), NagKind.Scheduled));
        var nag = Assert.Single(Kind(State(At(60), reminders: [Reminder(10, 0)], settings: NoNotTracking), NagKind.Scheduled));
        Assert.Equal("Timesheet", nag.Title);
        Assert.Equal("Submit it", nag.Message);
        Assert.Equal(7, nag.ReminderId);

        // The host records the fire; the next tick doesn't repeat it.
        Assert.Empty(Kind(State(At(61), reminders: [Reminder(10, 0, lastFired: At(60))], settings: NoNotTracking), NagKind.Scheduled));
    }

    [Fact]
    public void A_missed_occurrence_the_same_day_fires_with_a_suffix()
    {
        var nag = Assert.Single(Kind(State(At(180), reminders: [Reminder(10, 0)], settings: NoNotTracking), NagKind.Scheduled));

        Assert.StartsWith("Timesheet (missed at ", nag.Title, StringComparison.Ordinal);
        Assert.Equal("Timesheet", Assert.Single(Kind(State(At(61.5), reminders: [Reminder(10, 0) with { Id = 8 }], settings: NoNotTracking), NagKind.Scheduled)).Title);
    }

    [Fact]
    public void Yesterdays_occurrence_never_fires()
    {
        // Last fired two days ago; 08:00 today hasn't come yet at 07:30 local; yesterday's is ignored.
        Assert.Empty(Kind(State(At(-90), reminders: [Reminder(8, 0, lastFired: At(-48 * 60))], settings: NoNotTracking), NagKind.Scheduled));
    }

    [Fact]
    public void Disabled_reminders_and_unticked_weekdays_are_skipped()
    {
        Assert.Empty(Kind(State(At(60), reminders: [Reminder(10, 0, enabled: false)], settings: NoNotTracking), NagKind.Scheduled));
        Assert.Empty(Kind(State(At(60), reminders: [Reminder(10, 0, days: 1 << (int)DayOfWeek.Tuesday)], settings: NoNotTracking), NagKind.Scheduled));
    }

    [Fact]
    public void Snoozed_scheduled_nag_comes_back_unchanged()
    {
        var nag = Assert.Single(Kind(State(At(60), reminders: [Reminder(10, 0)], settings: NoNotTracking), NagKind.Scheduled));
        _engine.SnoozeScheduled(nag, At(75));

        Assert.Empty(Kind(State(At(74), settings: NoNotTracking), NagKind.Scheduled));
        Assert.Same(nag, Assert.Single(Kind(State(At(75), settings: NoNotTracking), NagKind.Scheduled)));
        Assert.Empty(Kind(State(At(90), settings: NoNotTracking), NagKind.Scheduled));
    }

    // ---- Suppression --------------------------------------------------------------------------

    [Theory]
    [InlineData("paused")]
    [InlineData("idle")]
    [InlineData("prompt")]
    public void Suppression_returns_nothing_and_loses_nothing(string why)
    {
        var due = State(At(60), running: [], reminders: [Reminder(10, 0)]);
        var suppressed = why switch
        {
            "paused" => due with { PausedUntilUtc = At(61) },
            "idle" => due with { IsUserIdle = true },
            _ => due with { IsPromptOpen = true },
        };

        Assert.Empty(_engine.Evaluate(suppressed));
        Assert.Equal(2, _engine.Evaluate(due).Count); // scheduled + not tracking, both still due
    }

    [Fact]
    public void Presentation_mode_still_returns_nags()
    {
        Assert.NotEmpty(_engine.Evaluate(State(At(15), busy: true)));
    }
}
