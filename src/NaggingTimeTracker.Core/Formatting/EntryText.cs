using System.Globalization;

namespace NaggingTimeTracker.Core.Formatting;

public static class EntryText
{
    /// <summary>The text before the first line break, trimmed.</summary>
    public static string FirstLine(string? notes)
    {
        if (string.IsNullOrEmpty(notes))
        {
            return "";
        }

        var trimmed = notes.TrimStart();
        var end = trimmed.IndexOfAny(['\r', '\n']);
        return (end < 0 ? trimmed : trimmed[..end]).Trim();
    }

    /// <summary>An absolute http/https URL (opened in the browser); anything else is plain text.</summary>
    public static bool IsWebUrl(string? link, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(link)
            || !Uri.TryCreate(link.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>"" on the same day, " (+1)" the next day, " (+N)" later.</summary>
    public static string StopSuffix(DateOnly startLocalDate, DateOnly stopLocalDate)
    {
        var days = stopLocalDate.DayNumber - startLocalDate.DayNumber;
        return days <= 0 ? "" : string.Create(CultureInfo.InvariantCulture, $" (+{days})");
    }
}
