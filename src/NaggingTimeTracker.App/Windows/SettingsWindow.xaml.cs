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
        vm.Scheduled.Owner = this;
        DataContext = vm;
        Closed += (_, _) => _vm.Detach();
    }

    private void RemindersGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_vm.Scheduled.EditCommand.CanExecute(null))
        {
            _vm.Scheduled.EditCommand.Execute(null);
        }
    }

    /// <summary>Selects the tab whose Tag is <paramref name="tab"/>.</summary>
    public void SelectTab(SettingsTab tab) =>
        Tabs.SelectedItem = Tabs.Items.OfType<System.Windows.Controls.TabItem>().First(t => Equals(t.Tag, tab));
}
