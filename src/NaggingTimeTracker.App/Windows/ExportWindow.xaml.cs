using System.Windows;
using NaggingTimeTracker.App.ViewModels;
using NaggingTimeTracker.Core.Entries;

namespace NaggingTimeTracker.App.Windows;

public partial class ExportWindow : Window
{
    private readonly ExportViewModel _vm;

    public ExportWindow(ExportViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        InitializeComponent();
        _vm = vm;
        _vm.Owner = this;
        DataContext = vm;
        Closed += (_, _) => _vm.Detach();
    }

    public void ApplyPrefill(FilterState? state) => _vm.ApplyPrefill(state);
}
