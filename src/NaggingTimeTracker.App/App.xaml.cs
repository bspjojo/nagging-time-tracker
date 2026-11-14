using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NaggingTimeTracker.App.Tracking;
using NaggingTimeTracker.App.Tray;
using NaggingTimeTracker.App.ViewModels;
using NaggingTimeTracker.App.Windows;
using NaggingTimeTracker.Core.Services;
using NaggingTimeTracker.Core.Settings;
using NaggingTimeTracker.Core.Startup;
using NaggingTimeTracker.Data;

namespace NaggingTimeTracker.App;

/// <summary>Tray-only app: no main window, shutdown is explicit.</summary>
public partial class App : Application
{
    private const string Title = "Nagging Time Tracker";

    private IHost? _host;
    private SingleInstance? _singleInstance;
    private TrayHost? _trayHost;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        StartupOptions options;
        try
        {
            options = StartupOptions.Parse(e.Args);
        }
        catch (StartupOptionsException ex)
        {
            MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        // Before any database work, so a second launch never migrates concurrently.
        _singleInstance = SingleInstance.TryAcquire(InstanceNames.For(options.DataDir));
        if (_singleInstance is null)
        {
            Shutdown(0);
            return;
        }

        try
        {
            var paths = new DataPaths(options.DataDir);
            paths.EnsureCreated();

            _host = BuildHost(options, paths);
            _host.Services.GetRequiredService<DatabaseInitializer>().Initialize();
            _host.StartAsync().GetAwaiter().GetResult();

            _trayHost = _host.Services.GetRequiredService<TrayHost>();
            _trayHost.Initialize();
            var trayHost = _trayHost;
            _singleInstance.ListenForShow(trayHost.ShowQuickPanel);

            DispatcherUnhandledException += OnDispatcherUnhandledException;

            var windows = _host.Services.GetRequiredService<WindowManager>();
            switch (options.Show)
            {
                case ShowTarget.QuickPanel:
                    trayHost.ShowQuickPanel();
                    break;
                case ShowTarget.Entries:
                    windows.ShowEntries();
                    break;
                case ShowTarget.Settings:
                    windows.ShowSettings();
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayHost?.Dispose();
        if (_host is not null)
        {
            _host.StopAsync().GetAwaiter().GetResult();
            _host.Dispose();
        }

        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static IHost BuildHost(StartupOptions options, DataPaths paths)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        var services = builder.Services;
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton(paths);
        services.AddTrackerData(paths.DbPath);

        services.AddSingleton<TrackingService>();
        services.AddSingleton<CatalogService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<TrackingActions>();
        services.AddSingleton<TrayHost>();
        services.AddSingleton<QuickPanelViewModel>();
        services.AddTransient<QuickPanelWindow>();

        services.AddSingleton<WindowManager>();
        services.AddSingleton<EntryEditorFactory>();
        services.AddTransient<EntriesViewModel>();
        services.AddTransient<EntriesWindow>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SettingsWindow>();

        return builder.Build();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Every operation uses its own short-lived DbContext, so a failed one leaves no broken shared state.
        MessageBox.Show(e.Exception.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
