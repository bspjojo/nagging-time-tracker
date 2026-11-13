using Microsoft.EntityFrameworkCore;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.Data.Repositories;

public sealed class ReminderRepository(IDbContextFactory<TrackerDbContext> factory) : IReminderRepository
{
    public IReadOnlyList<ScheduledReminder> GetAll()
    {
        using var db = factory.CreateDbContext();

        // 'HH:mm' text sorts chronologically.
        return db.Reminders.AsNoTracking().OrderBy(r => r.TimeLocal).ThenBy(r => r.Title).ToList();
    }

    public ScheduledReminder? GetById(long id)
    {
        using var db = factory.CreateDbContext();
        return db.Reminders.AsNoTracking().FirstOrDefault(r => r.Id == id);
    }

    public ScheduledReminder Insert(ScheduledReminder reminder)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        using var db = factory.CreateDbContext();
        db.Reminders.Add(reminder);
        db.SaveChanges();
        return reminder;
    }

    public bool Update(ScheduledReminder reminder)
    {
        ArgumentNullException.ThrowIfNull(reminder);
        using var db = factory.CreateDbContext();
        return db.Reminders.Where(r => r.Id == reminder.Id).ExecuteUpdate(s => s
            .SetProperty(r => r.Title, reminder.Title)
            .SetProperty(r => r.Message, reminder.Message)
            .SetProperty(r => r.DaysMask, reminder.DaysMask)
            .SetProperty(r => r.TimeLocal, reminder.TimeLocal)
            .SetProperty(r => r.Enabled, reminder.Enabled)
            .SetProperty(r => r.LastFiredUtc, reminder.LastFiredUtc)) > 0;
    }

    public bool Delete(long id)
    {
        using var db = factory.CreateDbContext();
        return db.Reminders.Where(r => r.Id == id).ExecuteDelete() > 0;
    }

    public bool SetLastFired(long id, DateTimeOffset utc)
    {
        using var db = factory.CreateDbContext();
        return db.Reminders.Where(r => r.Id == id)
            .ExecuteUpdate(s => s.SetProperty(r => r.LastFiredUtc, utc)) > 0;
    }
}
