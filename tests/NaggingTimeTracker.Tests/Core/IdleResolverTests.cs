using Microsoft.Extensions.Time.Testing;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Services;

namespace NaggingTimeTracker.Tests.Core;

public sealed class IdleResolverTests : IDisposable
{
    private static readonly DateTimeOffset T0 = TestDb.T0;
    private static readonly DateTimeOffset IdleStart = T0.AddMinutes(30);
    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _time = new(T0);
    private readonly TrackingService _tracking;
    private readonly IdleResolver _resolver;
    private readonly long _acme;
    private readonly long _globex;

    public IdleResolverTests()
    {
        _tracking = new TrackingService(_db.Entries, _time);
        _resolver = new IdleResolver(_tracking);
        _acme = _db.AddCustomer("Acme").Id;
        _globex = _db.AddCustomer("Globex").Id;
        _time.SetUtcNow(T0.AddMinutes(75)); // back after 45 min away
    }

    public void Dispose() => _db.Dispose();

    private Entry Get(long id) => _db.Entries.GetById(id)!;

    [Fact]
    public void Keep_changes_nothing()
    {
        var a = _db.AddEntry(_acme, T0, null);
        _resolver.Apply(IdleChoice.Keep, IdleStart);

        Assert.Equal(a, Get(a.Id));
        Assert.Single(_tracking.GetRunning());
    }

    [Fact]
    public void DiscardAndStop_stops_at_idle_start()
    {
        var a = _db.AddEntry(_acme, T0, null);
        _resolver.Apply(IdleChoice.DiscardAndStop, IdleStart);

        Assert.Equal(IdleStart, Get(a.Id).StopUtc);
        Assert.Empty(_tracking.GetRunning());
    }

    [Fact]
    public void DiscardAndContinue_stops_at_idle_start_and_continues_now()
    {
        var a = _db.AddEntry(_acme, T0, null, taskTypeId: 3, link: "https://x/1", notes: "n");
        _resolver.Apply(IdleChoice.DiscardAndContinue, IdleStart);

        Assert.Equal(IdleStart, Get(a.Id).StopUtc);
        var next = Assert.Single(_tracking.GetRunning());
        Assert.Equal(_time.GetUtcNow(), next.StartUtc);
        Assert.Equal(a.Id, next.ContinuedFromId);
        Assert.Equal(_acme, next.CustomerId);
        Assert.Equal(3, next.TaskTypeId);
        Assert.Equal("https://x/1", next.ExternalLink);
        Assert.Null(next.OverlapReason);
    }

    [Fact]
    public void DiscardAndContinue_keeps_parallel_entries_and_their_reasons()
    {
        var a = _db.AddEntry(_acme, T0, null);
        var b = _db.AddEntry(_globex, T0.AddMinutes(5), null, overlapReason: "x");
        _resolver.Apply(IdleChoice.DiscardAndContinue, IdleStart);

        var running = _tracking.GetRunning();
        Assert.Equal(2, running.Count);
        Assert.Null(running.Single(e => e.ContinuedFromId == a.Id).OverlapReason);
        Assert.Equal("x", running.Single(e => e.ContinuedFromId == b.Id).OverlapReason);
        Assert.Equal(IdleStart, Get(a.Id).StopUtc);
        Assert.Equal(IdleStart, Get(b.Id).StopUtc);
    }

    [Theory]
    [InlineData(IdleChoice.Keep)]
    [InlineData(IdleChoice.DiscardAndContinue)]
    [InlineData(IdleChoice.DiscardAndStop)]
    public void Entry_started_during_idle_is_untouched(IdleChoice choice)
    {
        var during = _db.AddEntry(_globex, IdleStart, null, overlapReason: "y");
        var atStart = _db.AddEntry(_acme, IdleStart.AddMinutes(10), null);

        Assert.Empty(_resolver.Affected(IdleStart));
        _resolver.Apply(choice, IdleStart);

        Assert.Equal(during, Get(during.Id));
        Assert.Equal(atStart, Get(atStart.Id));
        Assert.Equal(2, _tracking.GetRunning().Count);
    }

    [Fact]
    public void Affected_is_oldest_first()
    {
        var b = _db.AddEntry(_globex, T0.AddMinutes(5), null);
        var a = _db.AddEntry(_acme, T0, null);

        Assert.Equal([a.Id, b.Id], _resolver.Affected(IdleStart).Select(e => e.Id));
    }

    [Fact]
    public void Entry_stopped_before_apply_is_skipped()
    {
        var a = _db.AddEntry(_acme, T0, null);
        Assert.Single(_resolver.Affected(IdleStart));
        _tracking.Stop(a.Id);
        var stoppedAt = Get(a.Id).StopUtc;

        _resolver.Apply(IdleChoice.DiscardAndContinue, IdleStart);

        Assert.Equal(stoppedAt, Get(a.Id).StopUtc);
        Assert.Empty(_tracking.GetRunning());
    }
}
