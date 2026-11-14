using NaggingTimeTracker.Core.Idle;

namespace NaggingTimeTracker.Tests.Core;

public sealed class IdleTrackerTests
{
    private static readonly DateTimeOffset T0 = TestDb.T0;
    private static readonly IdleOptions On = new(true, TimeSpan.FromMinutes(10));
    private static readonly IdleOptions Off = new(false, TimeSpan.FromMinutes(10));
    private readonly IdleTracker _t = new();

    private static DateTimeOffset At(double minutes) => T0.AddMinutes(minutes);

    private static TimeSpan Min(double m) => TimeSpan.FromMinutes(m);

    [Fact]
    public void Below_threshold_is_not_idle()
    {
        Assert.Null(_t.Poll(At(9), Min(9), On));
        Assert.False(_t.IsUserIdle);
    }

    [Fact]
    public void At_threshold_goes_idle_from_the_last_input()
    {
        Assert.Null(_t.Poll(At(10), Min(10), On));
        Assert.True(_t.IsUserIdle);
        Assert.Equal(T0, _t.IdleStartUtc);
    }

    [Fact]
    public void Input_after_idle_start_returns_span_to_the_last_input()
    {
        _t.Poll(At(10), Min(10), On);
        Assert.Null(_t.Poll(At(30), Min(30), On)); // still away
        var span = _t.Poll(At(45), Min(5), On);

        Assert.Equal(new IdleSpan(T0, At(40)), span);
        Assert.False(_t.IsUserIdle);
    }

    [Fact]
    public void Input_within_the_margin_is_not_a_return()
    {
        _t.Poll(At(10), Min(10), On);
        Assert.Null(_t.Poll(At(20), Min(20) - TimeSpan.FromMilliseconds(500), On));
        Assert.True(_t.IsUserIdle);
    }

    [Fact]
    public void Short_lock_suppresses_but_does_not_prompt()
    {
        _t.Lock(At(0), TimeSpan.FromSeconds(1), On);
        Assert.True(_t.IsUserIdle);

        Assert.Null(_t.Unlock(At(2), On));
        Assert.False(_t.IsUserIdle);
    }

    [Fact]
    public void Long_lock_returns_span_from_last_input_before_lock_to_unlock()
    {
        _t.Lock(At(5), Min(5), On); // auto-lock after 5 min of no input
        var span = _t.Unlock(At(35), On);

        Assert.Equal(new IdleSpan(T0, At(35)), span);
    }

    [Fact]
    public void Lock_while_already_idle_keeps_the_start()
    {
        _t.Poll(At(20), Min(20), On);
        _t.Lock(At(25), Min(25), On);

        Assert.Equal(T0, _t.IdleStartUtc);
        Assert.Equal(new IdleSpan(T0, At(40)), _t.Unlock(At(40), On));
    }

    [Fact]
    public void Poll_while_locked_ignores_lock_screen_input()
    {
        _t.Lock(At(0), TimeSpan.Zero, On);
        Assert.Null(_t.Poll(At(20), TimeSpan.FromSeconds(2), On));
        Assert.True(_t.IsUserIdle);
    }

    [Fact]
    public void Resume_alone_does_not_end_idle_next_input_does()
    {
        _t.Suspend(At(0), TimeSpan.Zero, On);
        _t.Resume();
        Assert.True(_t.IsUserIdle);

        var span = _t.Poll(At(60), TimeSpan.FromSeconds(3), On);
        Assert.Equal(new IdleSpan(T0, At(60).AddSeconds(-3)), span);
    }

    [Fact]
    public void Suspend_lock_resume_unlock_returns_at_unlock()
    {
        _t.Suspend(At(0), TimeSpan.Zero, On);
        _t.Lock(At(0), TimeSpan.Zero, On);
        _t.Resume();
        Assert.Equal(new IdleSpan(T0, At(90)), _t.Unlock(At(90), On));
    }

    [Fact]
    public void Unlock_before_resume_stays_idle()
    {
        _t.Suspend(At(0), TimeSpan.Zero, On);
        _t.Lock(At(0), TimeSpan.Zero, On);
        Assert.Null(_t.Unlock(At(90), On));
        Assert.True(_t.IsUserIdle);
    }

    [Fact]
    public void Disabled_never_goes_idle_and_ignores_lock()
    {
        Assert.Null(_t.Poll(At(60), Min(60), Off));
        _t.Lock(At(60), Min(60), Off);
        Assert.False(_t.IsUserIdle);
    }

    [Fact]
    public void Disabling_while_idle_resets_on_next_poll()
    {
        _t.Poll(At(10), Min(10), On);
        Assert.Null(_t.Poll(At(11), Min(11), Off));
        Assert.False(_t.IsUserIdle);
    }
}
