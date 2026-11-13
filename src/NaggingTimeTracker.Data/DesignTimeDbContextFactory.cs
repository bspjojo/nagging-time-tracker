using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NaggingTimeTracker.Data;

/// <summary>
/// Used only by <c>dotnet ef</c>. Points at a throwaway file in %TEMP%, so the tool never needs
/// the App project or the real user database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TrackerDbContext>
{
    public TrackerDbContext CreateDbContext(string[] args)
    {
        var path = Path.Combine(Path.GetTempPath(), "ntt-design-time.db");
        var options = new DbContextOptionsBuilder<TrackerDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        return new TrackerDbContext(options);
    }
}
