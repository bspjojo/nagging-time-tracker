using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Reminders;

namespace NaggingTimeTracker.Tests.Core;

public sealed class NagQueueTests
{
    private readonly NagQueue _queue = new();

    private static Nag Scheduled(string title) => new(NagKind.Scheduled, title, "");

    private static Nag NotTracking() => new(NagKind.NotTracking, "You're not tracking anything.", "");

    private static Nag CheckIn(params (long Id, bool Due)[] items) =>
        new(NagKind.CheckIn, "Still working?", "", checkIns: items.Select(i => new CheckInItem(new Entry { Id = i.Id }, i.Due)).ToList());

    private static HashSet<long> Running(params long[] ids) => [.. ids];

    [Fact]
    public void Shows_one_at_a_time_in_order()
    {
        Nag a = Scheduled("a"), b = Scheduled("b");
        _queue.Enqueue([a, b]);

        Assert.Same(a, _queue.ShowNextIfIdle(false));
        Assert.Null(_queue.ShowNextIfIdle(false)); // one already shown
        _queue.CloseCurrent();
        Assert.Same(b, _queue.ShowNextIfIdle(false));
        _queue.CloseCurrent();
        Assert.False(_queue.HasPending);
    }

    [Fact]
    public void HasPending_covers_shown_and_queued()
    {
        Assert.False(_queue.HasPending);
        _queue.Enqueue([Scheduled("a")]);
        Assert.True(_queue.HasPending);   // queued
        _queue.ShowNextIfIdle(false);
        Assert.True(_queue.HasPending);   // shown
    }

    [Fact]
    public void Presentation_mode_holds_the_queue_then_releases_it()
    {
        var a = Scheduled("a");
        _queue.Enqueue([a]);

        Assert.Null(_queue.ShowNextIfIdle(presentationBusy: true));
        Assert.True(_queue.HasPending);
        Assert.Same(a, _queue.ShowNextIfIdle(presentationBusy: false));
    }

    [Fact]
    public void Prune_drops_not_tracking_nags_once_something_runs()
    {
        _queue.Enqueue([NotTracking(), NotTracking()]);
        _queue.ShowNextIfIdle(false);

        Assert.True(_queue.Prune(Running(5)));
        Assert.Null(_queue.Current);
        Assert.False(_queue.HasPending);
    }

    [Fact]
    public void Prune_removes_stopped_entries_and_drops_a_check_in_without_due_rows()
    {
        var nag = CheckIn((1, true), (2, false));
        _queue.Enqueue([nag]);
        _queue.ShowNextIfIdle(false);

        Assert.False(_queue.Prune(Running(1, 2)));
        Assert.True(_queue.Prune(Running(1)));
        Assert.Equal([1L], nag.CheckIns.Select(i => i.Entry.Id));
        Assert.Same(nag, _queue.Current);

        Assert.True(_queue.Prune(Running()));
        Assert.Null(_queue.Current);
    }

    [Fact]
    public void Prune_drops_queued_check_ins_whose_due_entries_stopped()
    {
        _queue.Enqueue([Scheduled("shown")]);
        _queue.ShowNextIfIdle(false);
        _queue.Enqueue([CheckIn((1, true))]);

        Assert.False(_queue.Prune(Running()));
        _queue.CloseCurrent();
        Assert.False(_queue.HasPending);
    }

    [Fact]
    public void Check_in_closes_after_the_last_due_row_is_answered()
    {
        _queue.Enqueue([CheckIn((1, true), (2, true), (3, false))]);
        _queue.ShowNextIfIdle(false);

        _queue.ResolveCheckIn(1);
        Assert.NotNull(_queue.Current);
        Assert.Contains(1L, _queue.HandledCheckIns);
        _queue.ResolveCheckIn(2);
        Assert.Null(_queue.Current); // the not-due row doesn't keep it open
    }

    [Fact]
    public void A_stopped_row_can_complete_the_check_in()
    {
        _queue.Enqueue([CheckIn((1, true), (2, true))]);
        _queue.ShowNextIfIdle(false);
        _queue.ResolveCheckIn(1);

        Assert.True(_queue.Prune(Running(1)));
        Assert.Null(_queue.Current);
    }

    [Fact]
    public void Snooze_rows_are_the_due_rows_not_yet_answered()
    {
        _queue.Enqueue([CheckIn((1, true), (2, true), (3, false))]);
        _queue.ShowNextIfIdle(false);
        Assert.Equal([1L, 2L], _queue.UnansweredDueCheckIns());

        _queue.ResolveCheckIn(1);
        Assert.Equal([2L], _queue.UnansweredDueCheckIns()); // answered and not-due rows keep their clock
    }

    [Fact]
    public void Snooze_rows_are_empty_without_a_check_in_on_screen()
    {
        Assert.Empty(_queue.UnansweredDueCheckIns());
        _queue.Enqueue([Scheduled("a")]);
        _queue.ShowNextIfIdle(false);
        Assert.Empty(_queue.UnansweredDueCheckIns());
    }

    [Fact]
    public void Answered_rows_do_not_carry_over_to_the_next_check_in()
    {
        _queue.Enqueue([CheckIn((1, true), (2, true)), CheckIn((1, true), (2, true))]);
        _queue.ShowNextIfIdle(false);
        _queue.ResolveCheckIn(1);
        _queue.CloseCurrent(); // e.g. snoozed with row 1 answered

        _queue.ShowNextIfIdle(false);
        Assert.Empty(_queue.HandledCheckIns);
        Assert.Equal([1L, 2L], _queue.UnansweredDueCheckIns());
    }
}
