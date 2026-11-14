using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using NaggingTimeTracker.App.ViewModels;

namespace NaggingTimeTracker.App.Windows;

public partial class EntriesWindow : Window
{
    private readonly EntriesViewModel _vm;

    public EntriesWindow(EntriesViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        InitializeComponent();
        _vm = vm;
        _vm.Owner = this;
        DataContext = vm;

        // A closed window must stop listening and querying.
        Closed += (_, _) => _vm.Detach();
    }

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only a double-click on a row edits; not on the header or the empty area.
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(Grid, source) is DataGridRow
            && _vm.EditCommand.CanExecute(null))
        {
            _vm.EditCommand.Execute(null);
        }
    }

    private void Grid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // DataGrid would otherwise use Enter to move to the next row.
        if (e.Key == Key.Enter && _vm.EditCommand.CanExecute(null))
        {
            e.Handled = true;
            _vm.EditCommand.Execute(null);
        }
        else if (e.Key == Key.Delete && _vm.DeleteCommand.CanExecute(null))
        {
            e.Handled = true;
            _vm.DeleteCommand.Execute(null);
        }
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
    }
}
