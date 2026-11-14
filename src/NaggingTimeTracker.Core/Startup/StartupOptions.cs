namespace NaggingTimeTracker.Core.Startup;

/// <summary>Which window to open once startup finishes (<c>--show</c>).</summary>
public enum ShowTarget
{
    None,
    QuickPanel,
    Entries,
    Settings,
}

public sealed class StartupOptionsException(string message) : Exception(message);

/// <summary>
/// Command-line switches, mainly for UI tests: <c>--data-dir &lt;path&gt;</c>,
/// <c>--show &lt;target&gt;</c> and <c>--no-background</c>.
/// </summary>
public sealed record StartupOptions(string? DataDir, ShowTarget Show, bool NoBackground)
{
    public static readonly StartupOptions Default = new(null, ShowTarget.None, false);

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? dataDir = null;
        var show = ShowTarget.None;
        var noBackground = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (!seen.Add(arg) && arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new StartupOptionsException($"Option '{arg}' was given more than once.");
            }

            switch (arg)
            {
                case "--data-dir":
                    dataDir = ValueFor(args, ref i, arg);
                    break;
                case "--show":
                    show = ParseShow(ValueFor(args, ref i, arg));
                    break;
                case "--no-background":
                    noBackground = true;
                    break;
                default:
                    throw new StartupOptionsException($"Unknown option '{arg}'.");
            }
        }

        return new StartupOptions(dataDir, show, noBackground);
    }

    private static string ValueFor(IReadOnlyList<string> args, ref int i, string option)
    {
        if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(args[i + 1]))
        {
            throw new StartupOptionsException($"Option '{option}' needs a value.");
        }

        i++;
        return args[i];
    }

    private static ShowTarget ParseShow(string value) =>
        value.ToLowerInvariant() switch
        {
            "quick-panel" => ShowTarget.QuickPanel,
            "entries" => ShowTarget.Entries,
            "settings" => ShowTarget.Settings,
            _ => throw new StartupOptionsException(
                $"Unknown --show value '{value}'. Valid values: quick-panel, entries, settings."),
        };
}
