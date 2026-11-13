using NaggingTimeTracker.Core.Formatting;

namespace NaggingTimeTracker.Tests.Core;

public sealed class DurationFormatTests
{
    [Theory]
    [InlineData(0, "0m")]
    [InlineData(59, "0m")]
    [InlineData(23 * 60, "23m")]
    [InlineData(83 * 60 + 59, "1h 23m")]
    [InlineData(26 * 3600, "26h 0m")]
    [InlineData(-90, "0m")]
    public void Short(int seconds, string expected)
    {
        Assert.Equal(expected, DurationFormat.Short(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(7, "0:00:07")]
    [InlineData(83 * 60 + 45, "1:23:45")]
    [InlineData(26 * 3600, "26:00:00")]
    [InlineData(-5, "0:00:00")]
    public void Clock(int seconds, string expected)
    {
        Assert.Equal(expected, DurationFormat.Clock(TimeSpan.FromSeconds(seconds)));
    }
}
