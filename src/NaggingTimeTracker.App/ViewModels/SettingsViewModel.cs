using CommunityToolkit.Mvvm.ComponentModel;
using NaggingTimeTracker.Core.Reminders;
using NaggingTimeTracker.Core.Services;
using NaggingTimeTracker.Core.Settings;
using NaggingTimeTracker.Core.Startup;
using NaggingTimeTracker.Data;

namespace NaggingTimeTracker.App.ViewModels;

/// <summary>The Settings window. Phases 5–7 add their tabs' view-models here.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public SettingsViewModel(
        CatalogService catalog,
        SettingsService settings,
        ScheduledReminderService reminders,
        RunAtLogin runAtLogin,
        DataPaths paths,
        StartupOptions options,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        General = new GeneralSettingsViewModel(runAtLogin, paths, options);
        Reminder = new ReminderSettingsViewModel(settings);
        Scheduled = new ScheduledRemindersViewModel(reminders, time);
        Customers = new CatalogListViewModel(
            "customer", "Customer", catalog.GetAllCustomers, catalog.RenameCustomer, catalog.SetCustomerArchived);
        TaskTypes = new CatalogListViewModel(
            "task type", "TaskType", catalog.GetAllTaskTypes, catalog.RenameTaskType, catalog.SetTaskTypeArchived);
    }

    /// <summary>The General tab.</summary>
    public GeneralSettingsViewModel General { get; }

    /// <summary>The Work hours, Check-ins and Idle tabs.</summary>
    public ReminderSettingsViewModel Reminder { get; }

    /// <summary>The Reminders tab.</summary>
    public ScheduledRemindersViewModel Scheduled { get; }

    public CatalogListViewModel Customers { get; }

    public CatalogListViewModel TaskTypes { get; }

    /// <summary>Releases subscriptions when the window closes (none yet; later tabs add some).</summary>
    public void Detach()
    {
    }
}
