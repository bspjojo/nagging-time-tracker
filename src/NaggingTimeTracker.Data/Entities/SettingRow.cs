namespace NaggingTimeTracker.Data.Entities;

/// <summary>A row of the key/value <c>settings</c> table. Core sees it only via ISettingsStore.</summary>
public sealed class SettingRow
{
    public required string Key { get; set; }
    public required string Value { get; set; }
}
