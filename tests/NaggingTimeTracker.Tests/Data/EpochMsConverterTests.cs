using Microsoft.EntityFrameworkCore;
using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.Tests.Data;

public sealed class EpochMsConverterTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Ms_truncated_value_round_trips_exactly()
    {
        var c = _db.AddCustomer("Acme");
        var start = new DateTimeOffset(2026, 3, 2, 9, 15, 30, 987, TimeSpan.Zero);

        var e = _db.AddEntry(c.Id, start, null);
        var read = _db.Entries.GetById(e.Id)!;

        Assert.Equal(start, read.StartUtc);
        Assert.Null(read.StopUtc);
    }

    [Fact]
    public void Non_utc_offset_is_stored_as_same_instant_and_read_back_as_utc()
    {
        var c = _db.AddCustomer("Acme");
        var start = new DateTimeOffset(2026, 3, 2, 11, 0, 0, TimeSpan.FromHours(2));

        var e = _db.AddEntry(c.Id, start, start.AddHours(1));
        var read = _db.Entries.GetById(e.Id)!;

        Assert.Equal(start.UtcDateTime, read.StartUtc.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, read.StartUtc.Offset);
        Assert.Equal(start.ToUnixTimeMilliseconds(),
            Convert.ToInt64(_db.Scalar($"SELECT start_utc FROM entries WHERE id = {e.Id}")));
    }

    [Fact]
    public void Timestamp_columns_are_integer()
    {
        var entries = _db.ColumnTypes("entries");
        foreach (var col in new[] { "start_utc", "stop_utc", "created_utc", "updated_utc" })
        {
            Assert.Equal("INTEGER", entries[col]);
        }

        Assert.Equal("INTEGER", _db.ColumnTypes("reminders")["last_fired_utc"]);
        Assert.Equal("INTEGER", _db.ColumnTypes("customers")["created_utc"]);
    }

    [Fact]
    public void Range_filter_and_order_by_translate_to_sql()
    {
        var c = _db.AddCustomer("Acme");
        var t0 = TestDb.T0;
        _db.AddEntry(c.Id, t0.AddHours(3), t0.AddHours(4));
        _db.AddEntry(c.Id, t0.AddHours(1), t0.AddHours(2));
        _db.AddEntry(c.Id, t0.AddHours(10), t0.AddHours(11)); // outside

        var a = t0;
        var b = t0.AddHours(5);
        using var ctx = _db.Factory.CreateDbContext();
        var query = ctx.Entries.Where(e => e.StartUtc >= a && e.StartUtc < b).OrderBy(e => e.StartUtc);

        var sql = query.ToQueryString();
        Assert.Contains("\"start_utc\" >=", sql);
        Assert.Contains("ORDER BY \"e\".\"start_utc\"", sql);

        var rows = query.ToList();
        Assert.Equal([t0.AddHours(1), t0.AddHours(3)], rows.Select(r => r.StartUtc));
    }
}
