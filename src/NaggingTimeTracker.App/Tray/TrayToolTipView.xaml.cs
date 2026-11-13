using System.Windows.Controls;

namespace NaggingTimeTracker.App.Tray;

public partial class TrayToolTipView : UserControl
{
    public TrayToolTipView(TrayToolTipViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
