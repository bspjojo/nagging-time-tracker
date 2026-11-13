using Microsoft.Extensions.Time.Testing;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.Tests.Core;

public sealed class ClockTests
{
    [Fact]
    public void NowUtc_drops_sub_millisecond_ticks_and_has_zero_offset()
    {
        var instant = new DateTimeOffset(2026, 3, 2, 9, 0, 0, 123, TimeSpan.Zero).AddTicks(4567);
        var tp = new FakeTimeProvider(instant);
        tp.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));

        var now = tp.NowUtc();

        Assert.Equal(new DateTimeOffset(2026, 3, 2, 9, 0, 0, 123, TimeSpan.Zero), now);
        Assert.Equal(0, now.UtcTicks % TimeSpan.TicksPerMillisecond);
        Assert.Equal(TimeSpan.Zero, now.Offset);
    }
}
