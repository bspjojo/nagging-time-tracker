using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using NaggingTimeTracker.Core.Reminders;
using NaggingTimeTracker.Core.Settings;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.App.ViewModels;

/// <summary>
/// The Work hours, Check-ins and Idle tabs. Every valid change is saved at once (text boxes on
/// losing focus); an invalid one shows an inline error and isn't saved.
/// </summary>
public sealed partial class ReminderSettingsViewModel : ObservableObject
{
    private const string TimeFormat = "HH:mm";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private readonly SettingsService _settings;
    private AppSettings _saved;
    private bool _loading = true;

    public ReminderSettingsViewModel(SettingsService settings)
    {
        _settings = settings;
        _saved = settings.Load();

        _alwaysOn = _saved.WorkAlwaysOn;
        _mon = Has(DayOfWeek.Monday);
        _tue = Has(DayOfWeek.Tuesday);
        _wed = Has(DayOfWeek.Wednesday);
        _thu = Has(DayOfWeek.Thursday);
        _fri = Has(DayOfWeek.Friday);
        _sat = Has(DayOfWeek.Saturday);
        _sun = Has(DayOfWeek.Sunday);
        _workStartText = _saved.WorkStart.ToString(TimeFormat, Inv);
        _workEndText = _saved.WorkEnd.ToString(TimeFormat, Inv);
        _notTrackingEnabled = _saved.NagNotTrackingEnabled;
        _notTrackingIntervalText = _saved.NagNotTrackingIntervalMin.ToString(Inv);
        _checkInEnabled = _saved.CheckinEnabled;
        _checkInIntervalText = _saved.CheckinIntervalMin.ToString(Inv);
        _idleEnabled = _saved.IdleEnabled;
        _idleThresholdText = _saved.IdleThresholdMin.ToString(Inv);
        _loading = false;
        UpdateHints();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesSchedule))]
    private bool _alwaysOn;

    [ObservableProperty] private bool _mon;
    [ObservableProperty] private bool _tue;
    [ObservableProperty] private bool _wed;
    [ObservableProperty] private bool _thu;
    [ObservableProperty] private bool _fri;
    [ObservableProperty] private bool _sat;
    [ObservableProperty] private bool _sun;
    [ObservableProperty] private string _workStartText;
    [ObservableProperty] private string _workEndText;
    [ObservableProperty] private bool _notTrackingEnabled;
    [ObservableProperty] private string _notTrackingIntervalText;
    [ObservableProperty] private bool _checkInEnabled;
    [ObservableProperty] private string _checkInIntervalText;
    [ObservableProperty] private string? _workHoursHint;
    [ObservableProperty] private string? _workHoursError;
    [ObservableProperty] private string? _checkInsError;
    [ObservableProperty] private bool _idleEnabled;
    [ObservableProperty] private string _idleThresholdText;
    [ObservableProperty] private string? _idleError;

    /// <summary>The days and times only apply when work hours aren't always on.</summary>
    public bool UsesSchedule => !AlwaysOn;

    partial void OnAlwaysOnChanged(bool value) => TryApply();
    partial void OnMonChanged(bool value) => TryApply();
    partial void OnTueChanged(bool value) => TryApply();
    partial void OnWedChanged(bool value) => TryApply();
    partial void OnThuChanged(bool value) => TryApply();
    partial void OnFriChanged(bool value) => TryApply();
    partial void OnSatChanged(bool value) => TryApply();
    partial void OnSunChanged(bool value) => TryApply();
    partial void OnWorkStartTextChanged(string value) => TryApply();
    partial void OnWorkEndTextChanged(string value) => TryApply();
    partial void OnNotTrackingEnabledChanged(bool value) => TryApply();
    partial void OnNotTrackingIntervalTextChanged(string value) => TryApply();
    partial void OnCheckInEnabledChanged(bool value) => TryApply();
    partial void OnCheckInIntervalTextChanged(string value) => TryApply();
    partial void OnIdleEnabledChanged(bool value) => TryApply();
    partial void OnIdleThresholdTextChanged(string value) => TryApply();

    private bool Has(DayOfWeek d) => (_saved.WorkDaysMask & (1 << (int)d)) != 0;

    private void TryApply()
    {
        if (_loading)
        {
            return;
        }

        var next = _saved;

        // Work hours: each field falls back to its last saved value while invalid.
        WorkHoursError = null;
        var mask = Bit(Sun, DayOfWeek.Sunday) | Bit(Mon, DayOfWeek.Monday) | Bit(Tue, DayOfWeek.Tuesday)
                   | Bit(Wed, DayOfWeek.Wednesday) | Bit(Thu, DayOfWeek.Thursday) | Bit(Fri, DayOfWeek.Friday)
                   | Bit(Sat, DayOfWeek.Saturday);
        next = next with { WorkAlwaysOn = AlwaysOn, WorkDaysMask = mask, NagNotTrackingEnabled = NotTrackingEnabled };
        if (!LocalTime.TryParseTime(WorkStartText, out var start) || !LocalTime.TryParseTime(WorkEndText, out var end))
        {
            WorkHoursError = "Times must be like 09:00.";
        }
        else if (WorkSchedule.Validate(start, end) is { } error)
        {
            WorkHoursError = error;
        }
        else
        {
            next = next with { WorkStart = start, WorkEnd = end };
        }

        if (TryInterval(NotTrackingIntervalText, out var notTracking))
        {
            next = next with { NagNotTrackingIntervalMin = notTracking };
        }
        else
        {
            WorkHoursError ??= "Interval must be 1–1440 minutes.";
        }

        // Check-ins.
        CheckInsError = null;
        next = next with { CheckinEnabled = CheckInEnabled };
        if (TryInterval(CheckInIntervalText, out var checkIn))
        {
            next = next with { CheckinIntervalMin = checkIn };
        }
        else
        {
            CheckInsError = "Interval must be 1–1440 minutes.";
        }

        // Idle.
        IdleError = null;
        next = next with { IdleEnabled = IdleEnabled };
        if (TryInterval(IdleThresholdText, out var threshold))
        {
            next = next with { IdleThresholdMin = threshold };
        }
        else
        {
            IdleError = "Threshold must be 1–1440 minutes.";
        }

        if (next != _saved)
        {
            _settings.Save(next);
            _saved = next;
        }

        UpdateHints();
    }

    private void UpdateHints()
    {
        WorkHoursHint = _saved.WorkAlwaysOn
            ? null
            : _saved.WorkDaysMask == 0
            ? "No work days — the not-tracking reminder never fires."
            : WorkSchedule.IsOvernight(_saved.WorkStart, _saved.WorkEnd)
                ? "(overnight — ends the next day)"
                : null;
    }

    private static int Bit(bool on, DayOfWeek d) => on ? 1 << (int)d : 0;

    private static bool TryInterval(string text, out int minutes) =>
        int.TryParse(text?.Trim(), NumberStyles.None, Inv, out minutes) && minutes is >= 1 and <= 1440;
}
