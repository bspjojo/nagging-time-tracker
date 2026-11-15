namespace NaggingTimeTracker.Core.Settings;

/// <summary>
/// The user-configurable settings, each initialised to its default. Runtime state (heartbeat,
/// pause, "not working") is deliberately not here; see <see cref="SettingsService.GetInstant"/>.
/// </summary>
public sealed record AppSettings
{
    /// <summary>Bit n = (DayOfWeek)n; 62 = Mon–Fri.</summary>
    public int WorkDaysMask { get; init; } = 62;

    /// <summary>Every moment counts as work hours; the days and times are kept but ignored.</summary>
    public bool WorkAlwaysOn { get; init; }

    public TimeOnly WorkStart { get; init; } = new(9, 0);
    public TimeOnly WorkEnd { get; init; } = new(17, 0);
    public bool NagNotTrackingEnabled { get; init; } = true;
    public int NagNotTrackingIntervalMin { get; init; } = 15;
    public bool CheckinEnabled { get; init; } = true;
    public int CheckinIntervalMin { get; init; } = 60;
    public bool IdleEnabled { get; init; } = true;
    public int IdleThresholdMin { get; init; } = 10;
}
