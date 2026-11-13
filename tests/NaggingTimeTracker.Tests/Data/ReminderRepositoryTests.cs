using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.Tests.Data;

public sealed class ReminderRepositoryTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly IReminderRepository _repo;

    public ReminderRepositoryTests() => _repo = _db.Get<IReminderRepository>();

    public void Dispose() => _db.Dispose();

    private ScheduledReminder Add(string title, int h, int m) =>
        _repo.Insert(new ScheduledReminder { Title = title, DaysMask = 62, TimeLocal = new TimeOnly(h, m) });

    [Fact]
    public void Crud_round_trip()
    {
        var r = _repo.Insert(new ScheduledReminder
        {
            Title = "Timesheet",
            Message = "Submit it",
            DaysMask = 1 << (int)DayOfWeek.Friday,
            TimeLocal = new TimeOnly(16, 30),
            Enabled = false,
            LastFiredUtc = TestDb.T0,
        });
        Assert.NotEqual(0, r.Id);
        Assert.Equal(r, _repo.GetById(r.Id));

        var changed = r with { Title = "T2", Message = "", DaysMask = 3, TimeLocal = new TimeOnly(8, 0), Enabled = true, LastFiredUtc = null };
        Assert.True(_repo.Update(changed));
        Assert.Equal(changed, _repo.GetById(r.Id));

        Assert.True(_repo.Delete(r.Id));
        Assert.Null(_repo.GetById(r.Id));
        Assert.False(_repo.Delete(r.Id));
        Assert.False(_repo.Update(changed));
    }

    [Fact]
    public void TimeLocal_is_stored_as_HH_mm_text()
    {
        var r = Add("Stretch", 9, 5);

        Assert.Equal("09:05", _db.Scalar($"SELECT time_local FROM reminders WHERE id = {r.Id}"));
        Assert.Equal(new TimeOnly(9, 5), _repo.GetById(r.Id)!.TimeLocal);
    }

    [Fact]
    public void SetLastFired_persists()
    {
        var r = Add("Stretch", 9, 5);
        var fired = TestDb.T0.AddMinutes(5);

        Assert.True(_repo.SetLastFired(r.Id, fired));

        Assert.Equal(fired, _repo.GetById(r.Id)!.LastFiredUtc);
        Assert.False(_repo.SetLastFired(999, fired));
    }

    [Fact]
    public void GetAll_is_ordered_by_time_then_title()
    {
        Add("b", 14, 0);
        Add("late", 23, 59);
        Add("a", 14, 0);
        Add("early", 7, 30);

        Assert.Equal(["early", "a", "b", "late"], _repo.GetAll().Select(r => r.Title));
    }
}
