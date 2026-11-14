using System.Windows;
using NaggingTimeTracker.App.ViewModels;

namespace NaggingTimeTracker.App.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        InitializeComponent();
        _vm = vm;
        vm.Customers.Owner = this;
        vm.TaskTypes.Owner = this;
        DataContext = vm;
        Closed += (_, _) => _vm.Detach();
    }

    public void SelectTab(SettingsTab tab) => _vm.SelectedTabIndex = (int)tab;
}
