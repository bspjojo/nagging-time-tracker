using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NaggingTimeTracker.Core.Startup;
using NaggingTimeTracker.Data;

namespace NaggingTimeTracker.App.ViewModels;

/// <summary>The General tab: Start with Windows (applies at once) and the data folder.</summary>
public sealed partial class GeneralSettingsViewModel : ObservableObject
{
    private readonly RunAtLogin _runAtLogin;
    private readonly DataPaths _paths;
    private bool _loading = true;

    public GeneralSettingsViewModel(RunAtLogin runAtLogin, DataPaths paths, StartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _runAtLogin = runAtLogin;
        _paths = paths;

        // The Run value has no arguments, so it always starts the default data folder: an instance
        // on another folder (--data-dir) mustn't change it.
        CanChangeStartWithWindows = options.DataDir is null;
        _startWithWindows = SafeIsEnabled();
        _loading = false;
    }

    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private string? _generalError;

    public bool CanChangeStartWithWindows { get; }

    public string DataFolder => _paths.Root;

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        GeneralError = null;
        try
        {
            if (value)
            {
                _runAtLogin.Enable(Environment.ProcessPath!);
            }
            else
            {
                _runAtLogin.Disable();
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            GeneralError = $"Couldn't change the startup setting: {ex.Message}";
            _loading = true;
            StartWithWindows = SafeIsEnabled();
            _loading = false;
        }
    }

    [RelayCommand]
    private void OpenDataFolder() => Process.Start("explorer.exe", $"\"{_paths.Root}\"");

    private bool SafeIsEnabled()
    {
        try
        {
            return _runAtLogin.IsEnabled;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return false;
        }
    }
}
