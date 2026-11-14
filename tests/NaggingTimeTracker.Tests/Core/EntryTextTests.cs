using NaggingTimeTracker.Core.Formatting;

namespace NaggingTimeTracker.Tests.Core;

public sealed class EntryTextTests
{
    [Theory]
    [InlineData("first\nsecond", "first")]
    [InlineData("first\r\nsecond", "first")]
    [InlineData("only line ", "only line")]
    [InlineData("\n\n  real first\nsecond", "real first")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void FirstLine(string? notes, string expected)
    {
        Assert.Equal(expected, EntryText.FirstLine(notes));
    }

    [Theory]
    [InlineData("https://x.test/a", true)]
    [InlineData("http://x", true)]
    [InlineData(" https://jira.test/browse/A-1 ", true)]
    [InlineData("JIRA-123", false)]
    [InlineData("ftp://x", false)]
    [InlineData("www.x.com", false)]
    [InlineData("file:///C:/x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsWebUrl(string? link, bool expected)
    {
        Assert.Equal(expected, EntryText.IsWebUrl(link, out var uri));
        Assert.Equal(expected, uri is not null);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, " (+1)")]
    [InlineData(3, " (+3)")]
    public void StopSuffix(int days, string expected)
    {
        var start = new DateOnly(2026, 3, 2);
        Assert.Equal(expected, EntryText.StopSuffix(start, start.AddDays(days)));
    }
}
