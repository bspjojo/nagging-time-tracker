using System.Globalization;
using CsvHelper;
using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.Core.Export;

/// <summary>
/// Writes the timesheet CSV: UTF-8 with BOM (so Excel detects the encoding), comma-delimited,
/// RFC 4180 quoting, CRLF line ends, fixed invariant formats in local time. Text that a
/// spreadsheet would run as a formula (=, +, -, @, tab, CR first) is prefixed with '.
/// </summary>
public static class CsvExporter
{
    public static readonly IReadOnlyList<string> Columns =
    [
        "entry_id", "date", "start", "stop", "duration_hours", "duration_minutes",
        "customer", "task_type", "external_link", "notes", "continued_from_id", "overlap_reason",
    ];

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Write(Stream output, IEnumerable<EntryView> rows, DateTimeOffset nowUtc, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(tz);

        var options = new CsvWriterOptions
        {
            FormatProvider = Inv,
            NewLine = "\r\n",
            Injection = CsvInjection.Escape,
            WriteByteOrderMark = true,
        };

        using var csv = CsvWriter.Create(output, options, leaveOpen: true);
        csv.WriteHeader([.. Columns]);

        foreach (var view in rows.OrderBy(v => v.Entry.StartUtc).ThenBy(v => v.Entry.Id))
        {
            var e = view.Entry;
            var start = TimeZoneInfo.ConvertTime(e.StartUtc, tz);
            var duration = (e.StopUtc ?? nowUtc) - e.StartUtc;

            csv.WriteField(e.Id.ToString(Inv));
            csv.WriteField(start.ToString("yyyy-MM-dd", Inv));
            csv.WriteField(start.ToString("yyyy-MM-dd HH:mm", Inv));
            csv.WriteField(e.StopUtc is { } stop ? TimeZoneInfo.ConvertTime(stop, tz).ToString("yyyy-MM-dd HH:mm", Inv) : "");
            csv.WriteField(Math.Round(duration.TotalHours, 2, MidpointRounding.AwayFromZero).ToString("0.00", Inv));
            csv.WriteField(((long)Math.Round(duration.TotalMinutes, 0, MidpointRounding.AwayFromZero)).ToString(Inv));
            csv.WriteField(view.CustomerName);
            csv.WriteField(view.TaskTypeName);
            csv.WriteField(e.ExternalLink ?? "");
            csv.WriteField(e.Notes);
            csv.WriteField(e.ContinuedFromId?.ToString(Inv) ?? "");
            csv.WriteField(e.OverlapReason ?? "");
            csv.EndRecord();
        }

        csv.Flush();
    }
}
