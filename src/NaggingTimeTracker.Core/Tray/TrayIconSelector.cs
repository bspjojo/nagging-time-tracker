using System.Globalization;

namespace NaggingTimeTracker.Core.Tray;

/// <summary>Picks the tray icon file for the current state (parent plan §4.1).</summary>
public static class TrayIconSelector
{
    /// <summary>Every icon file the tray can show (22 files).</summary>
    public static IReadOnlyList<string> AllFileNames { get; } = BuildAll();

    public static string Select(bool hasAttention, int runningCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(runningCount);

        if (hasAttention)
        {
            return runningCount switch
            {
                <= 9 => $"attention-{runningCount.ToString(CultureInfo.InvariantCulture)}.ico",
                _ => "attention-9plus.ico",
            };
        }

        return runningCount switch
        {
            0 => "idle.ico",
            <= 9 => $"tracking-{runningCount.ToString(CultureInfo.InvariantCulture)}.ico",
            _ => "tracking-9plus.ico",
        };
    }

    private static string[] BuildAll()
    {
        var names = new List<string> { Select(false, 0) };
        for (var n = 1; n <= 10; n++)
        {
            names.Add(Select(false, n));
        }

        for (var n = 0; n <= 10; n++)
        {
            names.Add(Select(true, n));
        }

        return [.. names];
    }
}
