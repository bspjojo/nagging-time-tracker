using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.Tests.Core;

public sealed class LocalTimeTests
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Theory]
    [InlineData("9:05", 9, 5)]
    [InlineData("09:05", 9, 5)]
    [InlineData("9.05", 9, 5)]
    [InlineData(" 23:59 ", 23, 59)]
    [InlineData("0:00", 0, 0)]
    public void TryParseTime_accepts(string text, int h, int m)
    {
        Assert.True(LocalTime.TryParseTime(text, out var t));
        Assert.Equal(new TimeOnly(h, m), t);
    }

    [Theory]
    [InlineData("24:00")]
    [InlineData("9:60")]
    [InlineData("905")]
    [InlineData("9:5")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ab:cd")]
    [InlineData("-1:00")]
    [InlineData("1:00:00")]
    public void TryParseTime_rejects(string? text)
    {
        Assert.False(LocalTime.TryParseTime(text, out _));
    }

    [Fact]
    public void ToUtc_on_a_normal_day()
    {
        var r = LocalTime.ToUtc(new DateOnly(2026, 1, 5), new TimeOnly(9, 30), NewYork);

        Assert.Null(r.Error);
        Assert.Equal(new DateTimeOffset(2026, 1, 5, 14, 30, 0, TimeSpan.Zero), r.Utc);
    }

    [Fact]
    public void ToUtc_rejects_a_time_skipped_by_the_clock_change()
    {
        var r = LocalTime.ToUtc(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), NewYork);

        Assert.Null(r.Utc);
        Assert.Contains("doesn't exist", r.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ToUtc_resolves_an_ambiguous_time_to_the_earlier_instant()
    {
        var r = LocalTime.ToUtc(new DateOnly(2026, 11, 1), new TimeOnly(1, 30), NewYork);

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero), r.Utc);
    }

    [Fact]
    public void ToUtcLenient_on_a_normal_day_matches_ToUtc()
    {
        Assert.Equal(
            new DateTimeOffset(2026, 1, 5, 14, 30, 0, TimeSpan.Zero),
            LocalTime.ToUtcLenient(new DateOnly(2026, 1, 5), new TimeOnly(9, 30), NewYork));
    }

    [Fact]
    public void ToUtcLenient_moves_a_skipped_time_to_the_first_valid_minute()
    {
        Assert.Equal(
            new DateTimeOffset(2026, 3, 8, 7, 0, 0, TimeSpan.Zero),
            LocalTime.ToUtcLenient(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), NewYork));
    }

    [Fact]
    public void ToUtcLenient_resolves_an_ambiguous_time_to_the_earlier_instant()
    {
        Assert.Equal(
            new DateTimeOffset(2026, 11, 1, 5, 30, 0, TimeSpan.Zero),
            LocalTime.ToUtcLenient(new DateOnly(2026, 11, 1), new TimeOnly(1, 30), NewYork));
    }
}
