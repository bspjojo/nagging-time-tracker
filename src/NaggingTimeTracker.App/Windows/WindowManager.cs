using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NaggingTimeTracker.Core.Entries;

namespace NaggingTimeTracker.App.Windows;

/// <summary>Settings tabs, selected by tag (not position) so later phases can add tabs freely.</summary>
public enum SettingsTab
{
    WorkHours,
    CheckIns,
    Idle,
    Reminders,
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
    private ExportWindow? _export;

    public void ShowEntries()
    {
        _entries ??= Track(services.GetRequiredService<EntriesWindow>(), () => _entries = null);
        Bring(_entries);
    }

    public void ShowSettings(SettingsTab tab = SettingsTab.WorkHours)
    {
        _settings ??= Track(services.GetRequiredService<SettingsWindow>(), () => _settings = null);
        _settings.SelectTab(tab);
        Bring(_settings);
    }

    /// <summary>
    /// Shows the Export window. A pre-fill (from the Entries window) is applied even when the
    /// window is already open, so it exports what is on screen.
    /// </summary>
    public void ShowExport(FilterState? prefill = null)
    {
        _export ??= Track(services.GetRequiredService<ExportWindow>(), () => _export = null);
        _export.ApplyPrefill(prefill);
        Bring(_export);
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
