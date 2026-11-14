using NaggingTimeTracker.Core.Formatting;

namespace NaggingTimeTracker.Tests.Core;

public sealed class DaysMaskTextTests
{
    [Theory]
    [InlineData(62, "Mon–Fri")]
    [InlineData(127, "Every day")]
    [InlineData(65, "Sat, Sun")]
    [InlineData(10, "Mon, Wed")]
    [InlineData(0, "Never")]
    [InlineData(14, "Mon–Wed")]
    [InlineData(6, "Mon, Tue")]
    public void Format(int mask, string expected)
    {
        Assert.Equal(expected, DaysMaskText.Format(mask));
    }
}
