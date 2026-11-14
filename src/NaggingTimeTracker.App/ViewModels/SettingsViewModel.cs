using CommunityToolkit.Mvvm.ComponentModel;
using NaggingTimeTracker.Core.Services;

namespace NaggingTimeTracker.App.ViewModels;

/// <summary>The Settings window. Phases 5–7 add their tabs' view-models here.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public SettingsViewModel(CatalogService catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        Customers = new CatalogListViewModel(
            "customer", "Customer", catalog.GetAllCustomers, catalog.RenameCustomer, catalog.SetCustomerArchived);
        TaskTypes = new CatalogListViewModel(
            "task type", "TaskType", catalog.GetAllTaskTypes, catalog.RenameTaskType, catalog.SetTaskTypeArchived);
    }

    public CatalogListViewModel Customers { get; }

    public CatalogListViewModel TaskTypes { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>Releases subscriptions when the window closes (none yet; later tabs add some).</summary>
    public void Detach()
    {
    }
}
