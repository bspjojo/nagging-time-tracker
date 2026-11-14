using NaggingTimeTracker.Core.Entries;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.Tests.Core;

public sealed class FilterStateTests
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static FilterState State(DateOnly from, DateOnly to) =>
        new(DatePreset.Custom, from, to, 4, 2, " notes ");

    [Fact]
    public void Validate_accepts_a_range_and_rejects_to_before_from()
    {
        Assert.Null(State(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 2)).Validate());
        Assert.Equal("The 'to' date is before the 'from' date.", State(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 1)).Validate());
    }

    [Fact]
    public void ToEntryFilter_gives_the_half_open_local_range_and_carries_ids_and_text()
    {
        var f = State(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)).ToEntryFilter(NewYork);

        Assert.Equal(new DateTimeOffset(2026, 3, 2, 5, 0, 0, TimeSpan.Zero), f.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 3, 9, 4, 0, 0, TimeSpan.Zero), f.ToUtc); // after spring-forward
        Assert.Equal(4, f.CustomerId);
        Assert.Equal(2, f.TaskTypeId);
        Assert.Equal(" notes ", f.Text);
    }

    [Fact]
    public void ToEntryFilter_on_an_invalid_state_throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            State(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 1)).ToEntryFilter(NewYork));
    }

    [Fact]
    public void DefaultFileName_uses_the_inclusive_to_date()
    {
        Assert.Equal(
            "timesheet-2026-03-02_to_2026-03-08.csv",
            State(new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 8)).DefaultFileName());
    }
}
