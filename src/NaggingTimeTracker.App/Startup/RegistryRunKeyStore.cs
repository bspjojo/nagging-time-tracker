using Microsoft.Win32;
using NaggingTimeTracker.Core.Startup;

namespace NaggingTimeTracker.App.Startup;

/// <summary>The per-user Run key. The installer (Phase 9) removes the same value on uninstall.</summary>
public sealed class RegistryRunKeyStore : IRunKeyStore
{
    public const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "NaggingTimeTracker";

    public string? Get()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) as string;
    }

    public void Set(string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(ValueName, value, RegistryValueKind.String);
    }

    public void Remove()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
