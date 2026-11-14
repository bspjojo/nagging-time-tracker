using System.Windows.Controls;

namespace NaggingTimeTracker.App.Controls;

/// <summary>The shared filter bar (Entries and Export windows); DataContext is a FilterBarViewModel.</summary>
public partial class FilterBar : UserControl
{
    public FilterBar()
    {
        InitializeComponent();
    }
}
