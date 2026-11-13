namespace NaggingTimeTracker.Core.Settings;

/// <summary>Keys of the <c>settings</c> table (parent plan §8).</summary>
public static class SettingKeys
{
    public const string WorkDaysMask = "work_days_mask";
    public const string WorkStart = "work_start";
    public const string WorkEnd = "work_end";
    public const string NagNotTrackingEnabled = "nag_not_tracking_enabled";
    public const string NagNotTrackingIntervalMin = "nag_not_tracking_interval_min";
    public const string CheckinEnabled = "checkin_enabled";
    public const string CheckinIntervalMin = "checkin_interval_min";
    public const string IdleEnabled = "idle_enabled";
    public const string IdleThresholdMin = "idle_threshold_min";

    // State keys: read and written through SettingsService.GetInstant / SetInstant.
    public const string HeartbeatUtc = "heartbeat_utc";
    public const string RemindersPausedUntilUtc = "reminders_paused_until_utc";
    public const string NotWorkingUntilUtc = "not_working_until_utc";
    public const string LastBackupDate = "last_backup_date";
}
