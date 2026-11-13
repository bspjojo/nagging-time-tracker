using System.Globalization;
using System.Windows;
using NaggingTimeTracker.Core.Formatting;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Services;

namespace NaggingTimeTracker.App.Windows;

public enum ConcurrencyChoice
{
    Cancel,
    StopOthers,
    RunInParallel,
}

/// <summary>Confirms starting a timer while others run; closing the window counts as Cancel.</summary>
public partial class ConcurrencyDialog : Window
{
    public ConcurrencyDialog(IReadOnlyList<Entry> running, CatalogNames names, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(running);
        ArgumentNullException.ThrowIfNull(names);
        InitializeComponent();

        HeaderText.Text = running.Count == 1
            ? "Another timer is already running:"
            : string.Create(CultureInfo.CurrentCulture, $"{running.Count} timers are already running:");
        RunningList.ItemsSource = running
            .Select(e => $"{names.Customer(e.CustomerId)} · {names.TaskType(e.TaskTypeId)} — {DurationFormat.Short(nowUtc - e.StartUtc)}")
            .ToList();
        Loaded += (_, _) => ReasonBox.Focus();
    }

    public ConcurrencyChoice Choice { get; private set; } = ConcurrencyChoice.Cancel;

    public string Reason => ReasonBox.Text;

    private void StopOthers_Click(object sender, RoutedEventArgs e)
    {
        Choice = ConcurrencyChoice.StopOthers;
        DialogResult = true;
    }

    private void RunInParallel_Click(object sender, RoutedEventArgs e)
    {
        Choice = ConcurrencyChoice.RunInParallel;
        DialogResult = true;
    }
}
