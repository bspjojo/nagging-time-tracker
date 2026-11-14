using Microsoft.Extensions.Time.Testing;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Reminders;

namespace NaggingTimeTracker.Tests.Core;

public sealed class ScheduledReminderServiceTests : IDisposable
{
    // Mon 2026-03-02 12:00 New York (17:00Z).
    private static readonly DateTimeOffset Noon = new(2026, 3, 2, 17, 0, 0, TimeSpan.Zero);

    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _time = new(Noon);
    private readonly ScheduledReminderService _service;

    public ScheduledReminderServiceTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        _service = new ScheduledReminderService(_db.Get<IReminderRepository>(), _time);
    }

    public void Dispose() => _db.Dispose();

    private static ScheduledReminder New(int hour, int days = 127, string title = "Stand-up") =>
        new() { Title = title, DaysMask = days, TimeLocal = new TimeOnly(hour, 0) };

    [Fact]
    public void Saving_after_todays_time_marks_it_fired_now()
    {
        Assert.Equal(Noon, _service.Save(New(9)).LastFiredUtc);
    }

    [Fact]
    public void Saving_before_todays_time_leaves_it_unfired()
    {
        Assert.Null(_service.Save(New(16)).LastFiredUtc);
    }

    [Fact]
    public void A_weekday_not_in_the_mask_leaves_it_unfired()
    {
        Assert.Null(_service.Save(New(9, days: 1 << (int)DayOfWeek.Tuesday)).LastFiredUtc);
    }

    [Theory]
    [InlineData("", 127)]
    [InlineData("   ", 127)]
    [InlineData("x", 0)]
    [InlineData("x", 128)]
    public void Invalid_reminders_are_rejected(string title, int days)
    {
        Assert.Throws<ArgumentException>(() => _service.Save(New(9, days, title)));
        Assert.Empty(_service.GetAll());
    }

    [Fact]
    public void Update_keeps_the_id_and_an_unpassed_time_keeps_last_fired()
    {
        var saved = _service.Save(New(16));
        var earlier = Noon.AddHours(-1);

        var updated = _service.Save(saved with { Title = "  Renamed  ", LastFiredUtc = earlier, TimeLocal = new TimeOnly(18, 0) });

        Assert.Equal(saved.Id, updated.Id);
        Assert.Equal("Renamed", updated.Title);
        Assert.Equal(earlier, Assert.Single(_service.GetAll()).LastFiredUtc);
    }

    [Fact]
    public void SetEnabled_reapplies_the_rule()
    {
        var saved = _service.Save(New(16) with { Enabled = false });
        _time.Advance(TimeSpan.FromHours(5)); // 17:00 local: today's 16:00 has passed

        Assert.True(_service.SetEnabled(saved.Id, true));

        var stored = Assert.Single(_service.GetAll());
        Assert.True(stored.Enabled);
        Assert.Equal(Noon.AddHours(5), stored.LastFiredUtc);
        Assert.False(_service.SetEnabled(999, true));
    }

    [Fact]
    public void GetById_returns_the_stored_row_after_SetEnabled()
    {
        var saved = _service.Save(New(16));
        Assert.True(_service.SetEnabled(saved.Id, false));

        // The editor saves what GetById returns, so a stale snapshot can't re-enable it.
        var current = _service.GetById(saved.Id);
        Assert.NotNull(current);
        Assert.False(current.Enabled);
        _service.Save(current with { Title = "Renamed" });

        var stored = Assert.Single(_service.GetAll());
        Assert.Equal("Renamed", stored.Title);
        Assert.False(stored.Enabled);
        Assert.Null(_service.GetById(999));
    }
}
