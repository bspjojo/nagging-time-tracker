using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace NaggingTimeTracker.App.Windows;

public enum SettingsTab
{
    Customers,
    TaskTypes,
}

/// <summary>
/// Owns the app's non-modal windows so each exists at most once: showing one that is already
/// open just brings it to the front. Closing a window really closes it; the next show builds a
/// fresh one.
/// </summary>
public sealed class WindowManager(IServiceProvider services)
{
    private EntriesWindow? _entries;
    private SettingsWindow? _settings;

    public void ShowEntries()
    {
        _entries ??= Track(services.GetRequiredService<EntriesWindow>(), () => _entries = null);
        Bring(_entries);
    }

    public void ShowSettings(SettingsTab tab = SettingsTab.Customers)
    {
        _settings ??= Track(services.GetRequiredService<SettingsWindow>(), () => _settings = null);
        _settings.SelectTab(tab);
        Bring(_settings);
    }

    private static T Track<T>(T window, Action onClosed)
        where T : Window
    {
        window.Closed += (_, _) => onClosed();
        return window;
    }

    private static void Bring(Window window)
    {
        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }
}
