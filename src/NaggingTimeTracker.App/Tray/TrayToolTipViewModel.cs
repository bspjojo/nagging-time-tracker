using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NaggingTimeTracker.App.Tray;

/// <summary>What the rich tray tooltip shows; updated by <see cref="TrayHost"/>.</summary>
public sealed partial class TrayToolTipViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isTracking;

    [ObservableProperty]
    private bool _showParallelHeader;

    [ObservableProperty]
    private string _parallelHeader = "";

    public ObservableCollection<string> Lines { get; } = [];

    public void Update(IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        Lines.Clear();
        foreach (var line in lines)
        {
            Lines.Add(line);
        }

        IsTracking = lines.Count > 0;
        ShowParallelHeader = lines.Count >= 2;
        ParallelHeader = NaggingTimeTracker.Core.Tray.TrayToolTipText.Header(lines.Count);
    }
}
