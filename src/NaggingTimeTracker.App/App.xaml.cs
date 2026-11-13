using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NaggingTimeTracker.Data;

namespace NaggingTimeTracker.App;

/// <summary>
/// Phase 1 placeholder: creates and migrates the real database, then exits.
/// Phase 2 replaces this with the Generic Host wiring, tray and windows.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var paths = new DataPaths();
            paths.EnsureCreated();

            using var services = new ServiceCollection()
                .AddTrackerData(paths.DbPath)
                .BuildServiceProvider();
            services.GetRequiredService<DatabaseInitializer>().Initialize();

            Shutdown(0);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Nagging Time Tracker", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
