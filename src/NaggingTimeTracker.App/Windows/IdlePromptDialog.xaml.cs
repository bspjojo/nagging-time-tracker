using System.Globalization;
using System.Windows;
using NaggingTimeTracker.Core.Formatting;
using NaggingTimeTracker.Core.Idle;
using NaggingTimeTracker.Core.Services;

namespace NaggingTimeTracker.App.Windows;

public enum IdlePromptKind
{
    /// <summary>The user was away (idle, locked or asleep) while the app ran.</summary>
    Away,

    /// <summary>The app wasn't running (startup downtime check).</summary>
    AppNotRunning,
}

/// <summary>
/// Asks what to do with time nobody was at the PC for. Any close other than a Discard button
/// (✕, Alt+F4, Esc) means Keep, which changes nothing.
/// </summary>
public partial class IdlePromptDialog : Window
{
    public IdlePromptDialog(IdlePromptKind kind, IdleSpan span, IReadOnlyList<string> affectedLabels, TimeZoneInfo tz)
    {
        ArgumentNullException.ThrowIfNull(affectedLabels);
        ArgumentNullException.ThrowIfNull(tz);
        InitializeComponent();
        MessageText.Text = Message(kind, span, affectedLabels, tz);
    }

    public IdleChoice Choice { get; private set; } = IdleChoice.Keep;

    internal static string Message(IdlePromptKind kind, IdleSpan span, IReadOnlyList<string> labels, TimeZoneInfo tz)
    {
        var start = TimeZoneInfo.ConvertTime(span.StartUtc, tz);
        var end = TimeZoneInfo.ConvertTime(span.EndUtc, tz);
        var format = start.Date == end.Date ? "t" : "g";
        var culture = CultureInfo.CurrentCulture;

        var what = labels.Count switch
        {
            0 => "",
            1 => labels[0],
            2 => $"{labels[0]} (and 1 other)",
            _ => string.Create(culture, $"{labels[0]} (and {labels.Count - 1} others)"),
        };
        var lead = kind == IdlePromptKind.Away ? "You were away" : "The app wasn't running";
        return string.Create(
            culture,
            $"{lead} from {start.ToString(format, culture)} to {end.ToString(format, culture)} ({DurationFormat.Short(span.Duration)}) while tracking {what}.");
    }

    private void Keep_Click(object sender, RoutedEventArgs e) => Close();

    private void DiscardContinue_Click(object sender, RoutedEventArgs e) => Choose(IdleChoice.DiscardAndContinue);

    private void DiscardStop_Click(object sender, RoutedEventArgs e) => Choose(IdleChoice.DiscardAndStop);

    private void Choose(IdleChoice choice)
    {
        Choice = choice;
        DialogResult = true;
    }
}
