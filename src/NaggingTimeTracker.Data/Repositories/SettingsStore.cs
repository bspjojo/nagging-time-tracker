using Microsoft.EntityFrameworkCore;
using NaggingTimeTracker.Core.Abstractions;

namespace NaggingTimeTracker.Data.Repositories;

public sealed class SettingsStore(IDbContextFactory<TrackerDbContext> factory) : ISettingsStore
{
    public string? Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        using var db = factory.CreateDbContext();
        return db.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefault();
    }

    public void Set(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        using var db = factory.CreateDbContext();
        db.Database.ExecuteSql(
            $"INSERT INTO settings (key, value) VALUES ({key}, {value}) ON CONFLICT(key) DO UPDATE SET value = excluded.value");
    }

    public void Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        using var db = factory.CreateDbContext();
        db.Settings.Where(s => s.Key == key).ExecuteDelete();
    }
}
