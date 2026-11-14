namespace NaggingTimeTracker.Core.Startup;

/// <summary>The "start with Windows" value (HKCU Run key in the app); null when absent.</summary>
public interface IRunKeyStore
{
    string? Get();

    void Set(string value);

    void Remove();
}

/// <summary>
/// Start with Windows: the Run value is the quoted exe path, with no arguments, so it always starts
/// the app on the default data folder.
/// </summary>
public sealed class RunAtLogin(IRunKeyStore store)
{
    public bool IsEnabled => store.Get() is not null;

    public void Enable(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        store.Set($"\"{exePath}\"");
    }

    public void Disable() => store.Remove();

    /// <summary>
    /// Rewrites an existing value that points at a different exe (e.g. after a reinstall to another
    /// scope). True when it rewrote it; an absent value is left absent.
    /// </summary>
    public bool HealIfMoved(string exePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exePath);
        var value = store.Get();
        if (value is null)
        {
            return false;
        }

        if (ParsePath(value) is { } current && SamePath(current, exePath))
        {
            return false;
        }

        Enable(exePath);
        return true;
    }

    /// <summary>The path in a Run value: one pair of surrounding quotes stripped, trimmed; null if empty.</summary>
    public static string? ParsePath(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var v = value.Trim();
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"')
        {
            v = v[1..^1].Trim();
        }

        return v.Length == 0 ? null : v;
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;   // an unparsable value is treated as pointing elsewhere
        }
    }
}
