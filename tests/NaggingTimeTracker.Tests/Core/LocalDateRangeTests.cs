using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.Tests.Core;

public sealed class LocalDateRangeTests
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Fact]
    public void To_is_inclusive_and_ends_at_next_local_midnight()
    {
        var day = new DateOnly(2026, 3, 2);

        var (from, to) = LocalDateRange.ToUtcRange(day, day, TimeZoneInfo.Utc);

        Assert.Equal(new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero), from);
        Assert.Equal(new DateTimeOffset(2026, 3, 3, 0, 0, 0, TimeSpan.Zero), to);
    }

    [Fact]
    public void Multi_day_range_in_new_york_uses_local_midnights()
    {
        var (from, to) = LocalDateRange.ToUtcRange(new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 9), NewYork);

        Assert.Equal(new DateTimeOffset(2026, 1, 5, 5, 0, 0, TimeSpan.Zero), from);
        Assert.Equal(new DateTimeOffset(2026, 1, 10, 5, 0, 0, TimeSpan.Zero), to);
    }

    [Theory]
    [InlineData(2026, 3, 8, 23)]   // spring forward
    [InlineData(2026, 11, 1, 25)]  // fall back
    [InlineData(2026, 3, 9, 24)]
    public void Dst_change_days_in_new_york_have_their_real_length(int y, int m, int d, int hours)
    {
        var day = new DateOnly(y, m, d);

        var (from, to) = LocalDateRange.ToUtcRange(day, day, NewYork);

        Assert.Equal(TimeSpan.FromHours(hours), to - from);
    }

    [Fact]
    public void Day_whose_midnight_is_skipped_starts_at_first_valid_minute()
    {
        // DST starts at 00:00 on the first Sunday of September: 00:00 → 01:00 local.
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 0, 0), 9, 1, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 0, 0), 4, 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);
        var tz = TimeZoneInfo.CreateCustomTimeZone(
            "Test/MidnightGap", TimeSpan.FromHours(-4), "Midnight gap", "Std", "Dst", [rule]);

        var gapDay = new DateOnly(2026, 9, 6); // first Sunday of September 2026
        Assert.True(tz.IsInvalidTime(gapDay.ToDateTime(TimeOnly.MinValue)));

        var (from, _) = LocalDateRange.ToUtcRange(gapDay, gapDay, tz);

        // 01:00 local at -03:00 = 04:00Z
        Assert.Equal(new DateTimeOffset(2026, 9, 6, 4, 0, 0, TimeSpan.Zero), from);
    }

    [Fact]
    public void To_before_from_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            LocalDateRange.ToUtcRange(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 1), TimeZoneInfo.Utc));
    }
}
