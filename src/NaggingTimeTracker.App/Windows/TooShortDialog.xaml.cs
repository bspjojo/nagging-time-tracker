using System.Globalization;
using System.Windows;

namespace NaggingTimeTracker.App.Windows;

/// <summary>Offered after an explicit single stop of an entry under a minute. Closing = Keep.</summary>
public partial class TooShortDialog : Window
{
    public TooShortDialog(string customer, string taskType, TimeSpan duration)
    {
        InitializeComponent();
        var seconds = (int)Math.Max(1, Math.Floor(duration.TotalSeconds));
        MessageText.Text = string.Create(
            CultureInfo.CurrentCulture,
            $"This entry was shorter than a minute ({customer} · {taskType}, {seconds}s). Keep it?");
    }

    public bool Discard { get; private set; }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        Discard = true;
        DialogResult = true;
    }
}
