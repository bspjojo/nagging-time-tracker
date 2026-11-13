using Microsoft.Extensions.Time.Testing;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Services;

namespace NaggingTimeTracker.Tests.Core;

public sealed class TrackingServiceTests : IDisposable
{
    private static readonly DateTimeOffset T0 = TestDb.T0;
    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _time = new(T0);
    private readonly TrackingService _tracking;
    private readonly long _acme;
    private readonly long _globex;
    private int _events;

    public TrackingServiceTests()
    {
        _tracking = new TrackingService(_db.Entries, _time);
        _tracking.RunningEntriesChanged += (_, _) => _events++;
        _acme = _db.AddCustomer("Acme").Id;
        _globex = _db.AddCustomer("Globex").Id;
    }

    public void Dispose() => _db.Dispose();

    private Entry Started(StartResult r) => Assert.IsType<StartResult.Started>(r).Entry;

    private Entry StartAcme(string? link = null, string? notes = null) =>
        Started(_tracking.Start(new StartRequest(_acme, 1, link, notes)));

    private void Advance(TimeSpan by) => _time.Advance(by);

    // ---- Start ----------------------------------------------------------------------------

    [Fact]
    public void Start_then_stop_uses_now()
    {
        var e = StartAcme(notes: " keep my spaces ");
        Assert.Equal(T0, e.StartUtc);
        Assert.Equal(T0, e.CreatedUtc);
        Assert.Null(e.StopUtc);
        Assert.Equal(" keep my spaces ", e.Notes);

        Advance(TimeSpan.FromMinutes(90));
        var stop = _tracking.Stop(e.Id)!;

        Assert.Equal(T0.AddMinutes(90), stop.Entry.StopUtc);
        Assert.False(stop.TooShort);
        Assert.Equal(stop.Entry, _db.Entries.GetById(e.Id));
        Assert.Empty(_tracking.GetRunning());
    }

    [Theory]
    [InlineData("  https://jira.test/A-1  ", "https://jira.test/A-1")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Start_trims_link_and_stores_blank_as_null(string? link, string? expected)
    {
        Assert.Equal(expected, StartAcme(link: link).ExternalLink);
    }

    [Fact]
    public void Start_with_another_running_needs_confirmation_and_inserts_nothing()
    {
        var first = StartAcme();
        _events = 0;

        var result = _tracking.Start(new StartRequest(_globex, 2, null, null));

        var needs = Assert.IsType<StartResult.NeedsConfirmation>(result);
        Assert.Equal([first.Id], needs.Running.Select(e => e.Id));
        Assert.Single(_tracking.GetRunning());
        Assert.Equal(0, _events);
    }

    [Fact]
    public void Confirmed_start_stores_trimmed_overlap_reason()
    {
        StartAcme();

        var second = Started(_tracking.Start(new StartRequest(_globex, 2, null, null, true, "  pairing  ")));

        Assert.Equal("pairing", second.OverlapReason);
        Assert.Equal(2, _tracking.GetRunning().Count);
    }

    [Fact]
    public void Confirmed_start_with_nothing_running_stores_no_reason()
    {
        var e = Started(_tracking.Start(new StartRequest(_acme, 1, null, null, true, "pairing")));

        Assert.Null(e.OverlapReason);
    }

    // ---- Stop -----------------------------------------------------------------------------

    [Theory]
    [InlineData(59, true)]
    [InlineData(60, false)]
    public void TooShort_under_one_minute(int seconds, bool tooShort)
    {
        var e = StartAcme();
        Advance(TimeSpan.FromSeconds(seconds));

        Assert.Equal(tooShort, _tracking.Stop(e.Id)!.TooShort);
    }

    [Fact]
    public void Stop_clamps_to_start_plus_one_second_when_clock_went_back()
    {
        // Started 5 minutes "in the future", as if the system clock was set back since.
        var e = _db.AddEntry(_acme, T0.AddMinutes(5), null);

        var stop = _tracking.Stop(e.Id)!;

        Assert.Equal(T0.AddMinutes(5).AddSeconds(1), stop.Entry.StopUtc);
        Assert.True(stop.TooShort);
    }

    [Fact]
    public void Stop_of_stopped_entry_is_a_no_op()
    {
        var e = StartAcme();
        Advance(TimeSpan.FromMinutes(5));
        var first = _tracking.Stop(e.Id)!.Entry;
        _events = 0;
        Advance(TimeSpan.FromMinutes(5));

        var again = _tracking.Stop(e.Id)!;

        Assert.Equal(first, again.Entry);
        Assert.False(again.TooShort);
        Assert.Equal(first, _db.Entries.GetById(e.Id));
        Assert.Equal(0, _events);
    }

    [Fact]
    public void Stop_of_missing_id_returns_null()
    {
        Assert.Null(_tracking.Stop(999));
        Assert.Equal(0, _events);
    }

    [Fact]
    public void Stop_keeps_notes_saved_meanwhile()
    {
        var e = StartAcme(notes: "old");
        _tracking.UpdateNotes(e.Id, "new");
        Advance(TimeSpan.FromMinutes(5));

        Assert.Equal("new", _tracking.Stop(e.Id)!.Entry.Notes);
        Assert.Equal("new", _db.Entries.GetById(e.Id)!.Notes);
    }

    [Fact]
    public void StopAll_stops_everything_and_raises_once()
    {
        StartAcme();
        _tracking.Start(new StartRequest(_globex, 2, null, null, true));
        Advance(TimeSpan.FromMinutes(5));
        _events = 0;

        var results = _tracking.StopAll();

        Assert.Equal(2, results.Count);
        Assert.Empty(_tracking.GetRunning());
        Assert.Equal(1, _events);
        Assert.Empty(_tracking.StopAll());
        Assert.Equal(1, _events);
    }

    [Fact]
    public void StopAllExcept_leaves_only_that_entry_running()
    {
        StartAcme();
        var keep = Started(_tracking.Start(new StartRequest(_globex, 2, null, null, true)));
        _tracking.Start(new StartRequest(_globex, 3, null, null, true));
        _events = 0;

        var results = _tracking.StopAllExcept(keep.Id);

        Assert.Equal(2, results.Count);
        Assert.Equal([keep.Id], _tracking.GetRunning().Select(e => e.Id));
        Assert.Equal(1, _events);
    }

    [Fact]
    public void StopAt_sets_the_given_stop()
    {
        var e = StartAcme();
        Advance(TimeSpan.FromHours(1));

        var stopped = _tracking.StopAt(e.Id, T0.AddMinutes(20))!;

        Assert.Equal(T0.AddMinutes(20), stopped.StopUtc);
        Assert.Equal(T0.AddHours(1), stopped.UpdatedUtc);
    }

    [Fact]
    public void StopAt_rejects_stop_not_after_start_and_ignores_stopped_entries()
    {
        var e = StartAcme();

        Assert.Throws<ArgumentOutOfRangeException>(() => _tracking.StopAt(e.Id, T0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _tracking.StopAt(e.Id, T0.AddMinutes(-1)));

        Advance(TimeSpan.FromMinutes(5));
        _tracking.Stop(e.Id);
        Assert.Null(_tracking.StopAt(e.Id, T0.AddMinutes(2)));
        Assert.Null(_tracking.StopAt(999, T0.AddMinutes(2)));
    }

    // ---- Continue -------------------------------------------------------------------------

    [Fact]
    public void Continue_copies_customer_type_link_with_empty_notes()
    {
        var source = StartAcme(link: "L", notes: "did things");
        Advance(TimeSpan.FromMinutes(30));
        _tracking.Stop(source.Id);
        Advance(TimeSpan.FromMinutes(10));

        var cont = Started(_tracking.Continue(source.Id));

        Assert.Equal((_acme, 1L, "L", "", source.Id), (cont.CustomerId, cont.TaskTypeId, cont.ExternalLink, cont.Notes, cont.ContinuedFromId));
        Assert.Equal(T0.AddMinutes(40), cont.StartUtc);
        Assert.Null(cont.OverlapReason);
    }

    [Fact]
    public void Continuing_a_stopped_entry_while_another_runs_needs_confirmation()
    {
        var source = StartAcme();
        Advance(TimeSpan.FromMinutes(5));
        _tracking.Stop(source.Id);
        _tracking.Start(new StartRequest(_globex, 2, null, null));

        Assert.IsType<StartResult.NeedsConfirmation>(_tracking.Continue(source.Id));

        var cont = Started(_tracking.Continue(source.Id, confirmedConcurrent: true, overlapReason: " review "));
        Assert.Equal("review", cont.OverlapReason);
        Assert.Equal(2, _tracking.GetRunning().Count);
    }

    [Fact]
    public void Continuing_a_running_entry_stops_it_needs_no_confirmation_and_copies_reason()
    {
        StartAcme();
        var source = Started(_tracking.Start(new StartRequest(_globex, 2, null, null, true, "pairing")));
        Advance(TimeSpan.FromMinutes(5));
        _events = 0;

        var cont = Started(_tracking.Continue(source.Id));

        Assert.Equal("pairing", cont.OverlapReason);
        Assert.Equal(T0.AddMinutes(5), _db.Entries.GetById(source.Id)!.StopUtc);
        Assert.Equal(2, _tracking.GetRunning().Count);
        Assert.Equal(1, _events);
    }

    [Fact]
    public void Explicit_reason_on_continue_overrides_the_copied_one()
    {
        StartAcme();
        var source = Started(_tracking.Start(new StartRequest(_globex, 2, null, null, true, "pairing")));

        var cont = Started(_tracking.Continue(source.Id, overlapReason: "handover"));

        Assert.Equal("handover", cont.OverlapReason);
    }

    [Fact]
    public void Continue_of_missing_id_is_NothingToContinue()
    {
        Assert.IsType<StartResult.NothingToContinue>(_tracking.Continue(999));
    }

    [Fact]
    public void ContinueLast_on_empty_db_is_NothingToContinue()
    {
        Assert.IsType<StartResult.NothingToContinue>(_tracking.ContinueLast());
        Assert.False(_tracking.HasStoppedEntries());
    }

    [Fact]
    public void ContinueLast_picks_latest_stopped_not_latest_started()
    {
        var a = StartAcme();
        var b = Started(_tracking.Start(new StartRequest(_globex, 2, null, null, true)));
        Advance(TimeSpan.FromMinutes(5));
        _tracking.Stop(b.Id);
        Advance(TimeSpan.FromMinutes(5));
        _tracking.Stop(a.Id); // a started first but stopped last

        var cont = Started(_tracking.ContinueLast());

        Assert.Equal(a.Id, cont.ContinuedFromId);
        Assert.True(_tracking.HasStoppedEntries());
    }

    // ---- Notes / delete / save ------------------------------------------------------------

    [Fact]
    public void UpdateNotes_on_an_entry_stopped_meanwhile_keeps_it_stopped()
    {
        var e = StartAcme();
        Advance(TimeSpan.FromMinutes(5));
        _tracking.Stop(e.Id);

        Assert.True(_tracking.UpdateNotes(e.Id, "late note"));

        var stored = _db.Entries.GetById(e.Id)!;
        Assert.Equal("late note", stored.Notes);
        Assert.Equal(T0.AddMinutes(5), stored.StopUtc);
    }

    [Fact]
    public void UpdateNotes_and_Delete_of_missing_id_return_false_without_event()
    {
        Assert.False(_tracking.UpdateNotes(999, "x"));
        Assert.False(_tracking.Delete(999));
        Assert.Equal(0, _events);
    }

    [Fact]
    public void Save_new_entry_sets_created_and_updated_to_now()
    {
        Advance(TimeSpan.FromHours(5));

        var saved = _tracking.Save(new Entry
        {
            CustomerId = _acme,
            TaskTypeId = 1,
            StartUtc = T0,
            StopUtc = T0.AddHours(1),
            ExternalLink = "  ",
            CreatedUtc = T0.AddYears(-1),
        });

        Assert.NotEqual(0, saved.Id);
        Assert.Null(saved.ExternalLink);
        Assert.Equal(T0.AddHours(5), saved.CreatedUtc);
        Assert.Equal(T0.AddHours(5), saved.UpdatedUtc);
        Assert.Equal(saved, _db.Entries.GetById(saved.Id));
    }

    [Fact]
    public void Save_existing_entry_keeps_CreatedUtc()
    {
        var e = StartAcme();
        Advance(TimeSpan.FromHours(2));

        var saved = _tracking.Save(e with { Notes = "edited", StopUtc = T0.AddHours(1), CreatedUtc = T0.AddYears(-1) });

        Assert.Equal(T0, saved.CreatedUtc);
        Assert.Equal(T0.AddHours(2), saved.UpdatedUtc);
        Assert.Equal(saved, _db.Entries.GetById(e.Id));
    }

    [Fact]
    public void Save_guards()
    {
        var running = StartAcme();
        Assert.Throws<ArgumentException>(() => _tracking.Save(running with { StopUtc = T0 }));
        Assert.Throws<InvalidOperationException>(() =>
            _tracking.Save(new Entry { CustomerId = _acme, TaskTypeId = 1, StartUtc = T0 }));
        Assert.Throws<InvalidOperationException>(() =>
            _tracking.Save(running with { Id = 999, StopUtc = T0.AddHours(1) }));

        // Re-opening an entry that was stopped elsewhere while the editor was open.
        Advance(TimeSpan.FromMinutes(5));
        _tracking.Stop(running.Id);
        Assert.Throws<InvalidOperationException>(() => _tracking.Save(running with { Notes = "stale" }));
    }

    // ---- Events ---------------------------------------------------------------------------

    [Fact]
    public void Every_change_raises_the_event()
    {
        var e = StartAcme();
        Assert.Equal(1, _events);
        _tracking.UpdateNotes(e.Id, "n");
        Assert.Equal(2, _events);
        Advance(TimeSpan.FromMinutes(5));
        _tracking.Stop(e.Id);
        Assert.Equal(3, _events);
        var cont = Started(_tracking.Continue(e.Id));
        Assert.Equal(4, _events);
        _tracking.Delete(cont.Id);
        Assert.Equal(5, _events);
        _tracking.Save(_db.Entries.GetById(e.Id)! with { Notes = "x" });
        Assert.Equal(6, _events);
    }
}
