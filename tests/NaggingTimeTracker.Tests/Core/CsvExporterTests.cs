using System.Globalization;
using System.Text;
using CsvHelper;
using NaggingTimeTracker.Core.Export;
using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.Tests.Core;

public sealed class CsvExporterTests
{
    private const string Header =
        "entry_id,date,start,stop,duration_hours,duration_minutes,customer,task_type,external_link,notes,continued_from_id,overlap_reason";

    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly DateTimeOffset T0 = new(2026, 3, 2, 14, 0, 0, TimeSpan.Zero); // 09:00 New York

    private static EntryView View(
        long id,
        DateTimeOffset start,
        DateTimeOffset? stop,
        string notes = "",
        string? link = null,
        long? continuedFrom = null,
        string? reason = null,
        string customer = "Acme",
        string taskType = "Dev Work") =>
        new(new Entry
        {
            Id = id,
            CustomerId = 1,
            TaskTypeId = 3,
            StartUtc = start,
            StopUtc = stop,
            Notes = notes,
            ExternalLink = link,
            ContinuedFromId = continuedFrom,
            OverlapReason = reason,
        }, customer, taskType);

    private static byte[] Export(IEnumerable<EntryView> rows, DateTimeOffset? now = null, TimeZoneInfo? tz = null)
    {
        using var ms = new MemoryStream();
        CsvExporter.Write(ms, rows, now ?? T0.AddDays(1), tz ?? NewYork);
        return ms.ToArray();
    }

    /// <summary>Data records (header skipped) as string arrays, read back with CsvHelper.</summary>
    private static List<string[]> Read(byte[] bytes)
    {
        using var reader = CsvReader.Create(new MemoryStream(bytes), new CsvReaderOptions { HasHeader = false }, leaveOpen: false);
        var records = new List<string[]>();
        while (reader.Read())
        {
            records.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader[i].ToString()).ToArray());
        }

        return records.Skip(1).ToList();
    }

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

    [Fact]
    public void Starts_with_bom_and_exact_header_with_crlf()
    {
        var bytes = Export([View(1, T0, T0.AddHours(1))]);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        var text = Text(bytes);
        Assert.StartsWith(Header + "\r\n", text, StringComparison.Ordinal);
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        Assert.Equal(CsvExporter.Columns, Header.Split(','));
    }

    [Fact]
    public void Empty_input_writes_header_only()
    {
        Assert.Equal(Header + "\r\n", Text(Export([])));
    }

    [Fact]
    public void Writes_every_column_in_local_time()
    {
        var r = Read(Export([View(7, T0, T0.AddMinutes(75), notes: "n", link: "https://x.test/1", continuedFrom: 3, reason: "pairing")]))[0];

        Assert.Equal(
            ["7", "2026-03-02", "2026-03-02 09:00", "2026-03-02 10:15", "1.25", "75", "Acme", "Dev Work", "https://x.test/1", "n", "3", "pairing"],
            r);
    }

    [Fact]
    public void Optional_columns_are_blank_when_unset()
    {
        var r = Read(Export([View(1, T0, T0.AddHours(1))]))[0];

        Assert.Equal("", r[8]);
        Assert.Equal("", r[10]);
        Assert.Equal("", r[11]);
    }

    [Fact]
    public void Notes_with_commas_quotes_and_newlines_round_trip()
    {
        const string notes = "a, \"b\"\nsecond line\r\nthird";
        var r = Read(Export([View(1, T0, T0.AddHours(1), notes: notes)]))[0];

        Assert.Equal(notes, r[9]);
    }

    [Fact]
    public void Numbers_use_invariant_culture_under_de_DE()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var r = Read(Export([View(1, T0, T0.AddMinutes(75))]))[0];
            Assert.Equal("1.25", r[4]);
            Assert.Equal("75", r[5]);
            Assert.Equal("2026-03-02 09:00", r[2]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(90, "0.03", "2")]
    [InlineData(89, "0.02", "1")]
    [InlineData(30, "0.01", "1")]
    [InlineData(29, "0.01", "0")]
    public void Durations_round_half_away_from_zero_independently(int seconds, string hours, string minutes)
    {
        var r = Read(Export([View(1, T0, T0.AddSeconds(seconds))]))[0];

        Assert.Equal(hours, r[4]);
        Assert.Equal(minutes, r[5]);
    }

    [Fact]
    public void Entry_spanning_spring_forward_uses_local_wall_times_and_real_duration()
    {
        // 2026-03-08 01:30 EST (06:30Z) to 03:30 EDT (07:30Z): one real hour.
        var start = new DateTimeOffset(2026, 3, 8, 6, 30, 0, TimeSpan.Zero);
        var r = Read(Export([View(1, start, start.AddHours(1))]))[0];

        Assert.Equal("2026-03-08 01:30", r[2]);
        Assert.Equal("2026-03-08 03:30", r[3]);
        Assert.Equal("1.00", r[4]);
        Assert.Equal("60", r[5]);
    }

    [Fact]
    public void Running_entry_has_blank_stop_and_duration_to_now()
    {
        var r = Read(Export([View(1, T0, null)], now: T0.AddMinutes(30)))[0];

        Assert.Equal("", r[3]);
        Assert.Equal("0.50", r[4]);
        Assert.Equal("30", r[5]);
    }

    [Fact]
    public void Rows_are_sorted_by_start()
    {
        var records = Read(Export([View(3, T0.AddHours(5), T0.AddHours(6)), View(1, T0, T0.AddHours(1)), View(2, T0.AddHours(2), T0.AddHours(3))]));

        Assert.Equal(["1", "2", "3"], records.Select(r => r[0]));
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+1", "'+1")]
    [InlineData("-x", "'-x")]
    [InlineData("@x", "'@x")]
    [InlineData("a=b", "a=b")]
    [InlineData("plain", "plain")]
    public void Formula_like_text_is_escaped(string notes, string expected)
    {
        var r = Read(Export([View(1, T0, T0.AddHours(1), notes: notes)]))[0];

        Assert.Equal(expected, r[9]);
    }

    [Fact]
    public void Formula_like_links_and_names_are_escaped_too()
    {
        var r = Read(Export([View(1, T0, T0.AddHours(1), link: "=HYPERLINK(\"x\")", customer: "-Acme")]))[0];

        Assert.Equal("'=HYPERLINK(\"x\")", r[8]);
        Assert.Equal("'-Acme", r[6]);
    }
}
