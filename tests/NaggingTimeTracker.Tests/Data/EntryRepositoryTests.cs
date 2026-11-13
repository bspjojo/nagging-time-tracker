using Microsoft.EntityFrameworkCore;
using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.Tests.Data;

public sealed class EntryRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset T0 = TestDb.T0;
    private readonly TestDb _db = new();
    private readonly long _acme;
    private readonly long _globex;

    public EntryRepositoryTests()
    {
        _acme = _db.AddCustomer("Acme").Id;
        _globex = _db.AddCustomer("Globex").Id;
    }

    public void Dispose() => _db.Dispose();

    private static DateTimeOffset H(double hours) => T0.AddHours(hours);

    private EntryFilter Range(double fromH, double toH, long? customer = null, long? type = null, string? text = null) =>
        new(H(fromH), H(toH), customer, type, text);

    // ---- CRUD -----------------------------------------------------------------------------

    [Fact]
    public void Insert_then_GetById_round_trips_every_field()
    {
        var source = _db.AddEntry(_acme, H(0), H(1));
        var e = _db.Entries.Insert(new Entry
        {
            CustomerId = _acme,
            TaskTypeId = 3,
            StartUtc = H(2),
            StopUtc = H(3),
            ExternalLink = "https://example.test/T-1",
            Notes = "line 1\nline 2, \"quoted\"",
            ContinuedFromId = source.Id,
            OverlapReason = "pairing",
            CreatedUtc = H(2),
            UpdatedUtc = H(2.5),
        });

        Assert.NotEqual(0, e.Id);
        Assert.Equal(e, _db.Entries.GetById(e.Id));
    }

    [Fact]
    public void Insert_with_nonzero_id_throws()
    {
        Assert.Throws<ArgumentException>(() => _db.Entries.Insert(new Entry { Id = 42, CustomerId = _acme, TaskTypeId = 1 }));
    }

    [Fact]
    public void GetById_of_missing_id_returns_null()
    {
        Assert.Null(_db.Entries.GetById(999));
    }

    [Fact]
    public void Update_changes_every_field_but_CreatedUtc()
    {
        var e = _db.AddEntry(_acme, H(0), null);
        var changed = e with
        {
            CustomerId = _globex,
            TaskTypeId = 2,
            StartUtc = H(1),
            StopUtc = H(2),
            ExternalLink = "x",
            Notes = "n",
            OverlapReason = "r",
            CreatedUtc = H(99),
            UpdatedUtc = H(5),
        };

        Assert.True(_db.Entries.Update(changed));

        Assert.Equal(changed with { CreatedUtc = e.CreatedUtc }, _db.Entries.GetById(e.Id));
    }

    [Fact]
    public void UpdateNotes_writes_only_notes_and_updated_utc()
    {
        var e = _db.AddEntry(_acme, H(0), H(1), link: "L", notes: "old");

        Assert.True(_db.Entries.UpdateNotes(e.Id, "new", H(7)));

        Assert.Equal(e with { Notes = "new", UpdatedUtc = H(7) }, _db.Entries.GetById(e.Id));
    }

    [Fact]
    public void Update_UpdateNotes_and_Delete_of_missing_id_return_false()
    {
        Assert.False(_db.Entries.Update(new Entry { Id = 999, CustomerId = _acme, TaskTypeId = 1, StartUtc = H(0) }));
        Assert.False(_db.Entries.UpdateNotes(999, "x", H(0)));
        Assert.False(_db.Entries.Delete(999));
    }

    [Fact]
    public void Delete_removes_the_entry_and_nulls_continuations()
    {
        var source = _db.AddEntry(_acme, H(0), H(1));
        var continuation = _db.AddEntry(_acme, H(2), H(3), continuedFromId: source.Id);

        Assert.True(_db.Entries.Delete(source.Id));

        Assert.Null(_db.Entries.GetById(source.Id));
        Assert.Null(_db.Entries.GetById(continuation.Id)!.ContinuedFromId);
    }

    [Theory]
    [InlineData(0)]   // stop == start
    [InlineData(-1)]  // stop < start
    public void Check_constraint_rejects_stop_not_after_start(double stopOffsetHours)
    {
        Assert.Throws<DbUpdateException>(() => _db.AddEntry(_acme, H(1), H(1 + stopOffsetHours)));
    }

    [Fact]
    public void Foreign_keys_are_enforced()
    {
        Assert.Throws<DbUpdateException>(() => _db.AddEntry(customerId: 999, H(0), H(1)));
    }

    // ---- Running / recent -----------------------------------------------------------------

    [Fact]
    public void GetRunning_returns_only_running_entries_ordered_by_start()
    {
        var later = _db.AddEntry(_acme, H(2), null);
        _db.AddEntry(_acme, H(0), H(1));
        var earlier = _db.AddEntry(_globex, H(1), null);

        Assert.Equal([earlier.Id, later.Id], _db.Entries.GetRunning().Select(e => e.Id));
    }

    [Fact]
    public void GetRecentStopped_is_newest_stop_first_and_respects_n()
    {
        var a = _db.AddEntry(_acme, H(0), H(5));    // starts first, stops last
        var b = _db.AddEntry(_acme, H(1), H(2));
        var c = _db.AddEntry(_acme, H(2), H(3));
        _db.AddEntry(_acme, H(4), null);           // running: excluded

        Assert.Equal([a.Id, c.Id, b.Id], _db.Entries.GetRecentStopped(10).Select(e => e.Id));
        Assert.Equal([a.Id, c.Id], _db.Entries.GetRecentStopped(2).Select(e => e.Id));
    }

    [Fact]
    public void GetRecentDistinct_collapses_same_customer_type_and_link()
    {
        _db.AddEntry(_acme, H(0), H(1), link: "L");
        _db.AddEntry(_acme, H(1), H(2), link: "L");
        var newestL = _db.AddEntry(_acme, H(2), H(3), link: "L");
        var otherLink = _db.AddEntry(_acme, H(3), H(4), link: "M");
        var noLink = _db.AddEntry(_acme, H(4), H(5));
        var lowerX = _db.AddEntry(_acme, H(5), H(6), link: "x");
        var otherType = _db.AddEntry(_acme, H(6), H(7), taskTypeId: 2, link: "L");

        var recent = _db.Entries.GetRecentDistinct(10).Select(e => e.Id).ToList();

        Assert.Equal([otherType.Id, lowerX.Id, noLink.Id, otherLink.Id, newestL.Id], recent);
        Assert.Equal([otherType.Id, lowerX.Id], _db.Entries.GetRecentDistinct(2).Select(e => e.Id));
    }

    // ---- Query / QueryViews ---------------------------------------------------------------

    [Fact]
    public void Query_range_is_half_open_on_start()
    {
        var atFrom = _db.AddEntry(_acme, H(0), H(1));
        var inside = _db.AddEntry(_acme, H(2), null);
        _db.AddEntry(_acme, H(4), H(5));            // starts exactly at ToUtc: excluded
        _db.AddEntry(_acme, H(-1), H(0.5));         // starts before FromUtc: excluded

        Assert.Equal([atFrom.Id, inside.Id], _db.Entries.Query(Range(0, 4)).Select(e => e.Id));
    }

    [Fact]
    public void Query_filters_by_customer_type_and_text()
    {
        var a = _db.AddEntry(_acme, H(0), H(1), taskTypeId: 1, notes: "Fixed the Login bug");
        var b = _db.AddEntry(_globex, H(1), H(2), taskTypeId: 2, link: "https://jira.test/LOGIN-7");
        var c = _db.AddEntry(_acme, H(2), H(3), taskTypeId: 2, notes: "standup");

        Assert.Equal([a.Id, c.Id], _db.Entries.Query(Range(0, 9, customer: _acme)).Select(e => e.Id));
        Assert.Equal([b.Id, c.Id], _db.Entries.Query(Range(0, 9, type: 2)).Select(e => e.Id));
        Assert.Equal([c.Id], _db.Entries.Query(Range(0, 9, customer: _acme, type: 2)).Select(e => e.Id));
        Assert.Equal([a.Id, b.Id], _db.Entries.Query(Range(0, 9, text: " login ")).Select(e => e.Id));
        Assert.Equal(3, _db.Entries.Query(Range(0, 9, text: "   ")).Count);
    }

    [Fact]
    public void Query_text_treats_like_wildcards_literally()
    {
        var pct = _db.AddEntry(_acme, H(0), H(1), notes: "100% done");
        _db.AddEntry(_acme, H(1), H(2), notes: "1000 done");
        var underscore = _db.AddEntry(_acme, H(2), H(3), notes: "a_b");
        _db.AddEntry(_acme, H(3), H(4), notes: "axb");
        var backslash = _db.AddEntry(_acme, H(4), H(5), notes: @"C:\temp");

        Assert.Equal([pct.Id], _db.Entries.Query(Range(0, 9, text: "0%")).Select(e => e.Id));
        Assert.Equal([underscore.Id], _db.Entries.Query(Range(0, 9, text: "a_b")).Select(e => e.Id));
        Assert.Equal([backslash.Id], _db.Entries.Query(Range(0, 9, text: @"C:\t")).Select(e => e.Id));
    }

    [Fact]
    public void QueryViews_includes_names_and_same_filter()
    {
        _db.AddEntry(_acme, H(0), H(1), taskTypeId: 3);
        _db.AddEntry(_globex, H(1), H(2), taskTypeId: 4);

        var views = _db.Entries.QueryViews(Range(0, 9));

        Assert.Equal([("Acme", "Dev Work"), ("Globex", "Meetings")],
            views.Select(v => (v.CustomerName, v.TaskTypeName)));
        Assert.Single(_db.Entries.QueryViews(Range(0, 9, customer: _globex)));
    }

    // ---- Overlap --------------------------------------------------------------------------

    [Fact]
    public void GetOverlapping_finds_overlaps_but_not_touching_entries()
    {
        var overlap = _db.AddEntry(_acme, H(1), H(3));
        _db.AddEntry(_acme, H(0), H(2));            // ends exactly at start: touching
        _db.AddEntry(_acme, H(4), H(5));            // starts exactly at stop: touching

        var found = _db.Entries.GetOverlapping(H(2), H(4), excludeId: null, nowUtc: H(10));

        Assert.Equal([overlap.Id], found.Select(e => e.Id));
    }

    [Fact]
    public void GetOverlapping_treats_running_entries_as_ending_now()
    {
        var running = _db.AddEntry(_acme, H(0), null);

        Assert.Single(_db.Entries.GetOverlapping(H(1), H(2), null, nowUtc: H(1.5)));
        Assert.Empty(_db.Entries.GetOverlapping(H(1), H(2), null, nowUtc: H(1)));
        Assert.Equal(running.Id, _db.Entries.GetOverlapping(H(1), H(2), null, H(5))[0].Id);
    }

    [Fact]
    public void GetOverlapping_treats_null_stop_as_now()
    {
        var later = _db.AddEntry(_acme, H(3), H(4));

        Assert.Empty(_db.Entries.GetOverlapping(H(1), null, null, nowUtc: H(3)));
        Assert.Equal([later.Id], _db.Entries.GetOverlapping(H(1), null, null, nowUtc: H(3.5)).Select(e => e.Id));
    }

    [Fact]
    public void GetOverlapping_honours_excludeId()
    {
        var self = _db.AddEntry(_acme, H(0), H(2));
        var other = _db.AddEntry(_acme, H(1), H(3));

        Assert.Equal([other.Id], _db.Entries.GetOverlapping(H(0), H(2), self.Id, H(10)).Select(e => e.Id));
    }

    [Fact]
    public void GetOverlappingIds_reports_both_sides_and_ignores_touching()
    {
        var a = _db.AddEntry(_acme, H(0), H(2));
        var b = _db.AddEntry(_acme, H(1), H(3));
        _db.AddEntry(_acme, H(5), H(6));
        _db.AddEntry(_acme, H(6), H(7));            // touching the previous one

        Assert.Equal(new HashSet<long> { a.Id, b.Id }, _db.Entries.GetOverlappingIds(H(0), H(24), H(30)));
    }

    [Fact]
    public void GetOverlappingIds_only_reports_entries_starting_in_range()
    {
        var outside = _db.AddEntry(_acme, H(-2), H(1));
        var inside = _db.AddEntry(_acme, H(0.5), H(2));

        var ids = _db.Entries.GetOverlappingIds(H(0), H(24), H(30));

        Assert.Equal(new HashSet<long> { inside.Id }, ids);
        Assert.DoesNotContain(outside.Id, ids);
    }

    [Fact]
    public void GetOverlappingIds_treats_running_entries_as_ending_now()
    {
        var running = _db.AddEntry(_acme, H(0), null);
        var later = _db.AddEntry(_acme, H(2), H(3));

        Assert.Empty(_db.Entries.GetOverlappingIds(H(0), H(24), nowUtc: H(2)));
        Assert.Equal(new HashSet<long> { running.Id, later.Id }, _db.Entries.GetOverlappingIds(H(0), H(24), H(2.5)));
    }
}
