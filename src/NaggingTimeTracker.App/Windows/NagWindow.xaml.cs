using System.ComponentModel;
using System.Windows;
using NaggingTimeTracker.App.ViewModels;
using NaggingTimeTracker.Core.Reminders;

namespace NaggingTimeTracker.App.Windows;

/// <summary>
/// A nag: topmost, bottom-right of the work area, never takes keyboard focus. Owned by
/// NagWindowManager, which closes it; a user close (Alt+F4) counts as the ✕ snooze.
/// </summary>
public partial class NagWindow : Window
{
    private const double EdgeMargin = 8;

    private readonly NagViewModel _vm;
    private bool _closingFromManager;

    public NagWindow(NagViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        SizeChanged += (_, _) => PlaceAtCorner();
        Loaded += (_, _) => PlaceAtCorner();
    }

    public Nag Nag => _vm.Nag;

    public void Refresh() => _vm.Refresh();

    /// <summary>Closes without counting as a user action.</summary>
    public void CloseQuietly()
    {
        _closingFromManager = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closingFromManager)
        {
            // Alt+F4 etc.: same as ✕. The manager closes the window itself.
            e.Cancel = true;
            _vm.CloseBoxCommand.Execute(null);
        }

        base.OnClosing(e);
    }

    private void PlaceAtCorner()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - EdgeMargin;
        Top = area.Bottom - ActualHeight - EdgeMargin;
    }
}
