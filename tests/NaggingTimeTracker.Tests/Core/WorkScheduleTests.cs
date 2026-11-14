using NaggingTimeTracker.Core.Reminders;
using NaggingTimeTracker.Core.Settings;

namespace NaggingTimeTracker.Tests.Core;

public sealed class WorkScheduleTests
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private const int MonToFri = 62;

    /// <summary>A local New York wall time as UTC (EST, -5, in early March 2026).</summary>
    private static DateTimeOffset Local(int month, int day, int hour, int minute = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Unspecified), NewYork);

    private static AppSettings Hours(int startH, int endH, int mask = MonToFri, int startM = 0) =>
        new() { WorkStart = new TimeOnly(startH, startM), WorkEnd = new TimeOnly(endH, 0), WorkDaysMask = mask };

    [Fact]
    public void Day_shift_inside_and_outside()
    {
        var s = Hours(9, 17);
        // 2026-03-02 is a Monday.
        Assert.Equal(new Shift(Local(3, 2, 9), Local(3, 2, 17)), WorkSchedule.CurrentShift(Local(3, 2, 10), s, NewYork));
        Assert.Null(WorkSchedule.CurrentShift(Local(3, 2, 8, 59), s, NewYork));
        Assert.Null(WorkSchedule.CurrentShift(Local(3, 2, 17), s, NewYork)); // end is exclusive
    }

    [Fact]
    public void Overnight_shift_belongs_to_the_day_it_starts()
    {
        var s = Hours(22, 6);
        // Fri 2026-03-06 22:00 → Sat 06:00.
        Assert.Equal(new Shift(Local(3, 6, 22), Local(3, 7, 6)), WorkSchedule.CurrentShift(Local(3, 7, 3), s, NewYork));
        Assert.Null(WorkSchedule.CurrentShift(Local(3, 7, 7), s, NewYork));
        Assert.Null(WorkSchedule.CurrentShift(Local(3, 7, 23), s, NewYork)); // Saturday isn't a work day
    }

    [Fact]
    public void Overnight_shift_from_an_unticked_sunday_doesnt_count_on_monday_morning()
    {
        // Mon 2026-03-02 02:00 would be inside a shift that started Sunday 22:00.
        Assert.Null(WorkSchedule.CurrentShift(Local(3, 2, 2), Hours(22, 6), NewYork));
    }

    [Fact]
    public void Start_in_a_dst_gap_moves_to_the_first_valid_minute()
    {
        // Sun 2026-03-08: 02:00 → 03:00. A 02:30 start becomes 03:00 EDT (07:00Z).
        var s = Hours(2, 5, mask: 127, startM: 30);
        var shift = WorkSchedule.CurrentShift(new DateTimeOffset(2026, 3, 8, 8, 0, 0, TimeSpan.Zero), s, NewYork);

        Assert.Equal(new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero), shift!.StartUtc);
    }

    [Fact]
    public void Start_equal_to_end_is_rejected()
    {
        Assert.NotNull(WorkSchedule.Validate(new TimeOnly(9, 0), new TimeOnly(9, 0)));
        Assert.Null(WorkSchedule.Validate(new TimeOnly(9, 0), new TimeOnly(17, 0)));
        Assert.Null(WorkSchedule.CurrentShift(Local(3, 2, 9), Hours(9, 9), NewYork));
    }

    [Fact]
    public void IsOvernight()
    {
        Assert.True(WorkSchedule.IsOvernight(new TimeOnly(22, 0), new TimeOnly(6, 0)));
        Assert.False(WorkSchedule.IsOvernight(new TimeOnly(9, 0), new TimeOnly(17, 0)));
    }
}
