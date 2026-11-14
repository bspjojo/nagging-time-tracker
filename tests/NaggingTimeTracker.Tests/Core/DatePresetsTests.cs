using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.Tests.Core;

public sealed class DatePresetsTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Fact]
    public void Today()
    {
        Assert.Equal((D(2026, 3, 4), D(2026, 3, 4)), DatePresets.Range(DatePreset.Today, D(2026, 3, 4)));
    }

    [Theory]
    [InlineData(2026, 3, 4)]  // Wednesday
    [InlineData(2026, 3, 2)]  // Monday: the week starts that day
    [InlineData(2026, 3, 8)]  // Sunday: still the week that began on Monday the 2nd
    public void ThisWeek_runs_monday_to_sunday(int y, int m, int d)
    {
        Assert.Equal((D(2026, 3, 2), D(2026, 3, 8)), DatePresets.Range(DatePreset.ThisWeek, D(y, m, d)));
    }

    [Fact]
    public void LastWeek_crosses_a_month_boundary()
    {
        // Wed 2026-03-04 → last week is Mon 2026-02-23 … Sun 2026-03-01.
        Assert.Equal((D(2026, 2, 23), D(2026, 3, 1)), DatePresets.Range(DatePreset.LastWeek, D(2026, 3, 4)));
    }

    [Fact]
    public void ThisMonth_handles_leap_february_and_december()
    {
        Assert.Equal((D(2028, 2, 1), D(2028, 2, 29)), DatePresets.Range(DatePreset.ThisMonth, D(2028, 2, 10)));
        Assert.Equal((D(2026, 12, 1), D(2026, 12, 31)), DatePresets.Range(DatePreset.ThisMonth, D(2026, 12, 31)));
    }

    [Fact]
    public void Custom_throws()
    {
        Assert.Throws<ArgumentException>(() => DatePresets.Range(DatePreset.Custom, D(2026, 3, 4)));
    }
}
