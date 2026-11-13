using System.Globalization;
using System.Text;

namespace NaggingTimeTracker.Core.Tray;

/// <summary>
/// The plain-text tray tooltip (the native Windows tooltip, limited to 127 characters). It shows
/// the same lines as the rich tooltip, because Windows 11 often shows the native one instead.
/// </summary>
public static class TrayToolTipText
{
    /// <summary>NOTIFYICONDATA.szTip holds 128 UTF-16 chars including the terminator.</summary>
    public const int MaxLength = 127;

    /// <param name="lines">One "customer · type — elapsed" line per running entry.</param>
    public static string Build(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        switch (lines.Count)
        {
            case 0:
                return "Not tracking";
            case 1:
                return Fit(lines[0]);
        }

        var sb = new StringBuilder(Header(lines.Count));
        for (var i = 0; i < lines.Count; i++)
        {
            var remaining = lines.Count - i - 1;
            var more = remaining > 0 ? "\n" + MoreText(remaining) : "";
            var candidate = "\n" + lines[i];

            // Keep the line only if there's still room for the "…and N more" that may follow it.
            if (sb.Length + candidate.Length + more.Length > MaxLength)
            {
                AppendMore(sb, lines.Count - i);
                break;
            }

            sb.Append(candidate);
        }

        return Fit(sb.ToString());
    }

    public static string Header(int runningCount) =>
        string.Create(CultureInfo.InvariantCulture, $"⚠ {runningCount} timers running in parallel");

    private static string MoreText(int count) =>
        string.Create(CultureInfo.InvariantCulture, $"…and {count} more");

    private static void AppendMore(StringBuilder sb, int count)
    {
        var more = "\n" + MoreText(count);
        if (sb.Length + more.Length <= MaxLength)
        {
            sb.Append(more);
        }
    }

    private static string Fit(string s) => s.Length <= MaxLength ? s : string.Concat(s.AsSpan(0, MaxLength - 1), "…");
}
