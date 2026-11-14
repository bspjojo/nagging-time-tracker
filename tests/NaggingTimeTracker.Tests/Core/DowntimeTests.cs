using NaggingTimeTracker.Core.Idle;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Settings;

namespace NaggingTimeTracker.Tests.Core;

public sealed class DowntimeTests
{
    private static readonly DateTimeOffset T0 = TestDb.T0;
    private static readonly AppSettings Defaults = new(); // enabled, 10 min
    private static readonly DateTimeOffset Beat = T0.AddMinutes(60);
    private static readonly DateTimeOffset Now = T0.AddMinutes(90);

    private static Entry Running(DateTimeOffset start) => new() { Id = 1, CustomerId = 1, TaskTypeId = 1, StartUtc = start };

    [Fact]
    public void No_heartbeat_is_nothing() =>
        Assert.Null(Downtime.Find(null, Now, Defaults, [Running(T0)]));

    [Fact]
    public void Idle_disabled_is_nothing() =>
        Assert.Null(Downtime.Find(Beat, Now, Defaults with { IdleEnabled = false }, [Running(T0)]));

    [Fact]
    public void Gap_below_threshold_is_nothing() =>
        Assert.Null(Downtime.Find(Beat, Beat.AddMinutes(9), Defaults, [Running(T0)]));

    [Fact]
    public void No_entry_running_since_before_the_heartbeat_is_nothing()
    {
        Assert.Null(Downtime.Find(Beat, Now, Defaults, []));
        Assert.Null(Downtime.Find(Beat, Now, Defaults, [Running(Beat.AddMinutes(1))]));
    }

    [Fact]
    public void Otherwise_span_from_heartbeat_to_now() =>
        Assert.Equal(new IdleSpan(Beat, Now), Downtime.Find(Beat, Now, Defaults, [Running(T0)]));
}
