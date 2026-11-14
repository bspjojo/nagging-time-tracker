using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Reminders;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.App.Windows;

/// <summary>Add or edit a custom reminder. Errors show inline and keep the dialog open.</summary>
public partial class ReminderEditorDialog : Window
{
    private readonly ScheduledReminder? _existing;
    private readonly ScheduledReminderService _service;
    private readonly TimeProvider _time;
    private readonly (CheckBox Box, DayOfWeek Day)[] _days;

    public ReminderEditorDialog(ScheduledReminder? existing, ScheduledReminderService service, TimeProvider time)
    {
        InitializeComponent();
        _existing = existing;
        _service = service;
        _time = time;
        _days =
        [
            (DayMon, DayOfWeek.Monday), (DayTue, DayOfWeek.Tuesday), (DayWed, DayOfWeek.Wednesday),
            (DayThu, DayOfWeek.Thursday), (DayFri, DayOfWeek.Friday), (DaySat, DayOfWeek.Saturday),
            (DaySun, DayOfWeek.Sunday),
        ];

        Title = existing is null ? "New reminder" : "Edit reminder";
        if (existing is null)
        {
            // Default: every day, at the next whole hour.
            var local = TimeZoneInfo.ConvertTime(time.NowUtc(), time.LocalTimeZone);
            TimeBox.Text = new TimeOnly((local.Hour + 1) % 24, 0).ToString("HH:mm", CultureInfo.InvariantCulture);
            foreach (var (box, _) in _days)
            {
                box.IsChecked = true;
            }
        }
        else
        {
            TitleBox.Text = existing.Title;
            MessageText.Text = existing.Message;
            TimeBox.Text = existing.TimeLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
            foreach (var (box, day) in _days)
            {
                box.IsChecked = (existing.DaysMask & (1 << (int)day)) != 0;
            }
        }

        Loaded += (_, _) => TitleBox.Focus();
        UpdateHint();
    }

    public long SavedId { get; private set; }

    private int Mask => _days.Where(d => d.Box.IsChecked == true).Aggregate(0, (m, d) => m | (1 << (int)d.Day));

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        UpdateHint();
    }

    /// <summary>Makes the no-instant-fire rule visible before saving.</summary>
    private void UpdateHint()
    {
        HintText.Text = "";
        if (!LocalTime.TryParseTime(TimeBox.Text, out var t))
        {
            return;
        }

        var now = _time.NowUtc();
        var tz = _time.LocalTimeZone;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, tz).DateTime);
        if ((Mask & (1 << (int)today.DayOfWeek)) != 0 && LocalTime.ToUtcLenient(today, t, tz) <= now)
        {
            HintText.Text = "Today's time has passed — the first reminder will be on the next selected day.";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            ErrorText.Text = "A title is required.";
            return;
        }

        if (Mask == 0)
        {
            ErrorText.Text = "Pick at least one day.";
            return;
        }

        if (!LocalTime.TryParseTime(TimeBox.Text, out var time))
        {
            ErrorText.Text = "Time must be like 16:30.";
            return;
        }

        var r = (_existing ?? new ScheduledReminder { Title = "" }) with
        {
            Title = TitleBox.Text,
            Message = MessageText.Text,
            DaysMask = Mask,
            TimeLocal = time,
        };

        try
        {
            SavedId = _service.Save(r).Id;
            DialogResult = true;
        }
        catch (ArgumentException ex)
        {
            ErrorText.Text = ex.Message;
        }
    }
}
