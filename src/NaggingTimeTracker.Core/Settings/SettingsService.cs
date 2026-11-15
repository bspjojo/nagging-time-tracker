using System.Globalization;
using NaggingTimeTracker.Core.Abstractions;

namespace NaggingTimeTracker.Core.Settings;

/// <summary>
/// Typed access to the key/value settings store. Values are invariant-culture strings; missing
/// or unparsable values fall back to the defaults. <see cref="Load"/> is cached, because
/// <see cref="Save"/> is the only writer of the <see cref="AppSettings"/> keys; the state keys
/// (<see cref="GetInstant"/> / <see cref="SetInstant"/>) are written elsewhere and always read
/// the store. Callers are all on the UI thread, so the cache needs no locking.
/// </summary>
public sealed class SettingsService(ISettingsStore store)
{
    private const string TimeFormat = "HH:mm";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Raised by <see cref="Save"/> only.</summary>
    public event EventHandler? SettingsChanged;

    private AppSettings? _cached;

    /// <summary>The current settings: read from the store once, then kept up to date by <see cref="Save"/>.</summary>
    public AppSettings Load() => _cached ??= Read();

    private AppSettings Read()
    {
        var d = new AppSettings();
        return new AppSettings
        {
            WorkDaysMask = ReadInt(SettingKeys.WorkDaysMask, d.WorkDaysMask),
            WorkAlwaysOn = ReadBool(SettingKeys.WorkAlwaysOn, d.WorkAlwaysOn),
            WorkStart = ReadTime(SettingKeys.WorkStart, d.WorkStart),
            WorkEnd = ReadTime(SettingKeys.WorkEnd, d.WorkEnd),
            NagNotTrackingEnabled = ReadBool(SettingKeys.NagNotTrackingEnabled, d.NagNotTrackingEnabled),
            NagNotTrackingIntervalMin = ReadInt(SettingKeys.NagNotTrackingIntervalMin, d.NagNotTrackingIntervalMin),
            CheckinEnabled = ReadBool(SettingKeys.CheckinEnabled, d.CheckinEnabled),
            CheckinIntervalMin = ReadInt(SettingKeys.CheckinIntervalMin, d.CheckinIntervalMin),
            IdleEnabled = ReadBool(SettingKeys.IdleEnabled, d.IdleEnabled),
            IdleThresholdMin = ReadInt(SettingKeys.IdleThresholdMin, d.IdleThresholdMin),
        };
    }

    public void Save(AppSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentOutOfRangeException.ThrowIfNegative(s.WorkDaysMask, nameof(s.WorkDaysMask));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(s.WorkDaysMask, 127, nameof(s.WorkDaysMask));
        ArgumentOutOfRangeException.ThrowIfLessThan(s.NagNotTrackingIntervalMin, 1, nameof(s.NagNotTrackingIntervalMin));
        ArgumentOutOfRangeException.ThrowIfLessThan(s.CheckinIntervalMin, 1, nameof(s.CheckinIntervalMin));
        ArgumentOutOfRangeException.ThrowIfLessThan(s.IdleThresholdMin, 1, nameof(s.IdleThresholdMin));

        // Dropped first, so a write that fails partway leaves the next Load to re-read the store.
        _cached = null;
        store.Set(SettingKeys.WorkDaysMask, s.WorkDaysMask.ToString(Inv));
        store.Set(SettingKeys.WorkAlwaysOn, Bool(s.WorkAlwaysOn));
        store.Set(SettingKeys.WorkStart, s.WorkStart.ToString(TimeFormat, Inv));
        store.Set(SettingKeys.WorkEnd, s.WorkEnd.ToString(TimeFormat, Inv));
        store.Set(SettingKeys.NagNotTrackingEnabled, Bool(s.NagNotTrackingEnabled));
        store.Set(SettingKeys.NagNotTrackingIntervalMin, s.NagNotTrackingIntervalMin.ToString(Inv));
        store.Set(SettingKeys.CheckinEnabled, Bool(s.CheckinEnabled));
        store.Set(SettingKeys.CheckinIntervalMin, s.CheckinIntervalMin.ToString(Inv));
        store.Set(SettingKeys.IdleEnabled, Bool(s.IdleEnabled));
        store.Set(SettingKeys.IdleThresholdMin, s.IdleThresholdMin.ToString(Inv));

        // What a re-read would return: times are stored to the minute.
        _cached = s with { WorkStart = ToMinute(s.WorkStart), WorkEnd = ToMinute(s.WorkEnd) };
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A state key holding epoch milliseconds; null when unset or unparsable.</summary>
    public DateTimeOffset? GetInstant(string key)
    {
        var raw = store.Get(key);
        return long.TryParse(raw, NumberStyles.Integer, Inv, out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : null;
    }

    /// <summary>Writes a state key as epoch milliseconds; null removes it. Doesn't raise SettingsChanged.</summary>
    public void SetInstant(string key, DateTimeOffset? value)
    {
        if (value is { } v)
        {
            store.Set(key, v.ToUnixTimeMilliseconds().ToString(Inv));
        }
        else
        {
            store.Remove(key);
        }
    }

    private static string Bool(bool b) => b ? "true" : "false";

    private static TimeOnly ToMinute(TimeOnly t) => new(t.Hour, t.Minute);

    private int ReadInt(string key, int fallback) =>
        int.TryParse(store.Get(key), NumberStyles.Integer, Inv, out var v) ? v : fallback;

    private bool ReadBool(string key, bool fallback) =>
        store.Get(key) switch
        {
            "true" => true,
            "false" => false,
            _ => fallback,
        };

    private TimeOnly ReadTime(string key, TimeOnly fallback) =>
        TimeOnly.TryParseExact(store.Get(key), TimeFormat, Inv, DateTimeStyles.None, out var v) ? v : fallback;
}
