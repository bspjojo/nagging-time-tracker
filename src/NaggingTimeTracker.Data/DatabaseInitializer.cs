using Microsoft.EntityFrameworkCore;

namespace NaggingTimeTracker.Data;

/// <summary>Runs at startup before anything else touches the database.</summary>
public sealed class DatabaseInitializer(IDbContextFactory<TrackerDbContext> factory)
{
    public void Initialize()
    {
        using var db = factory.CreateDbContext();

        // Throws if the model has changes with no migration (EF Core 9+ default), which is wanted.
        db.Database.Migrate();

        // Persists in the database file.
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
    }
}
