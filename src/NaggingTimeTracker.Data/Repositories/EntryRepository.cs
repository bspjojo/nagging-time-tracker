using Microsoft.EntityFrameworkCore;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.Data.Repositories;

public sealed class EntryRepository(IDbContextFactory<TrackerDbContext> factory) : IEntryRepository
{
    private const int RecentDistinctWindow = 200;

    public IReadOnlyList<Entry> GetRunning()
    {
        using var db = factory.CreateDbContext();
        return db.Entries.AsNoTracking()
            .Where(e => e.StopUtc == null)
            .OrderBy(e => e.StartUtc).ThenBy(e => e.Id)
            .ToList();
    }

    public Entry? GetById(long id)
    {
        using var db = factory.CreateDbContext();
        return db.Entries.AsNoTracking().FirstOrDefault(e => e.Id == id);
    }

    public IReadOnlyList<Entry> GetRecentStopped(int n)
    {
        using var db = factory.CreateDbContext();
        return RecentStoppedQuery(db).Take(n).ToList();
    }

    public IReadOnlyList<Entry> GetRecentDistinct(int n)
    {
        using var db = factory.CreateDbContext();
        return RecentStoppedQuery(db).Take(RecentDistinctWindow).ToList()
            .DistinctBy(e => (e.CustomerId, e.TaskTypeId, e.ExternalLink))
            .Take(n)
            .ToList();
    }

    public IReadOnlyList<Entry> Query(EntryFilter filter)
    {
        using var db = factory.CreateDbContext();
        return ApplyFilter(db.Entries.AsNoTracking(), filter)
            .OrderBy(e => e.StartUtc).ThenBy(e => e.Id)
            .ToList();
    }

    public IReadOnlyList<EntryView> QueryViews(EntryFilter filter)
    {
        using var db = factory.CreateDbContext();
        var query =
            from e in ApplyFilter(db.Entries.AsNoTracking(), filter)
            join c in db.Customers on e.CustomerId equals c.Id
            join t in db.TaskTypes on e.TaskTypeId equals t.Id
            orderby e.StartUtc, e.Id
            select new EntryView(e, c.Name, t.Name);
        return query.ToList();
    }

    public IReadOnlyList<Entry> GetOverlapping(
        DateTimeOffset startUtc, DateTimeOffset? stopUtc, long? excludeId, DateTimeOffset nowUtc)
    {
        var end = stopUtc ?? nowUtc;
        using var db = factory.CreateDbContext();
        return db.Entries.AsNoTracking()
            .Where(e => e.StartUtc < end && (e.StopUtc ?? nowUtc) > startUtc)
            .Where(e => excludeId == null || e.Id != excludeId)
            .OrderBy(e => e.StartUtc).ThenBy(e => e.Id)
            .ToList();
    }

    public IReadOnlySet<long> GetOverlappingIds(DateTimeOffset fromUtc, DateTimeOffset toUtc, DateTimeOffset nowUtc)
    {
        using var db = factory.CreateDbContext();
        var ids =
            from a in db.Entries
            where a.StartUtc >= fromUtc && a.StartUtc < toUtc
            from b in db.Entries
            where a.Id != b.Id
                  && a.StartUtc < (b.StopUtc ?? nowUtc)
                  && (a.StopUtc ?? nowUtc) > b.StartUtc
            select a.Id;
        return ids.Distinct().ToHashSet();
    }

    public Entry Insert(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Id != 0)
        {
            throw new ArgumentException("A new entry must have Id 0.", nameof(entry));
        }

        using var db = factory.CreateDbContext();
        db.Entries.Add(entry);
        db.SaveChanges();
        return entry; // EF wrote the generated id into the instance.
    }

    public bool Update(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using var db = factory.CreateDbContext();
        return db.Entries.Where(e => e.Id == entry.Id).ExecuteUpdate(s => s
            .SetProperty(e => e.CustomerId, entry.CustomerId)
            .SetProperty(e => e.TaskTypeId, entry.TaskTypeId)
            .SetProperty(e => e.StartUtc, entry.StartUtc)
            .SetProperty(e => e.StopUtc, entry.StopUtc)
            .SetProperty(e => e.ExternalLink, entry.ExternalLink)
            .SetProperty(e => e.Notes, entry.Notes)
            .SetProperty(e => e.ContinuedFromId, entry.ContinuedFromId)
            .SetProperty(e => e.OverlapReason, entry.OverlapReason)
            .SetProperty(e => e.UpdatedUtc, entry.UpdatedUtc)) > 0;
    }

    public bool UpdateNotes(long id, string notes, DateTimeOffset updatedUtc)
    {
        ArgumentNullException.ThrowIfNull(notes);
        using var db = factory.CreateDbContext();
        return db.Entries.Where(e => e.Id == id).ExecuteUpdate(s => s
            .SetProperty(e => e.Notes, notes)
            .SetProperty(e => e.UpdatedUtc, updatedUtc)) > 0;
    }

    public bool Delete(long id)
    {
        using var db = factory.CreateDbContext();

        // A raw DELETE: SQLite's own FK action nulls continued_from_id (Foreign Keys=True).
        return db.Entries.Where(e => e.Id == id).ExecuteDelete() > 0;
    }

    private static IQueryable<Entry> RecentStoppedQuery(TrackerDbContext db) =>
        db.Entries.AsNoTracking()
            .Where(e => e.StopUtc != null)
            .OrderByDescending(e => e.StopUtc).ThenByDescending(e => e.Id);

    private static IQueryable<Entry> ApplyFilter(IQueryable<Entry> q, EntryFilter f)
    {
        ArgumentNullException.ThrowIfNull(f);
        q = q.Where(e => e.StartUtc >= f.FromUtc && e.StartUtc < f.ToUtc);

        if (f.CustomerId is { } customerId)
        {
            q = q.Where(e => e.CustomerId == customerId);
        }

        if (f.TaskTypeId is { } taskTypeId)
        {
            q = q.Where(e => e.TaskTypeId == taskTypeId);
        }

        var text = f.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var pattern = "%" + EscapeLike(text) + "%";
            q = q.Where(e => EF.Functions.Like(e.Notes, pattern, "\\")
                             || (e.ExternalLink != null && EF.Functions.Like(e.ExternalLink, pattern, "\\")));
        }

        return q;
    }

    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
         .Replace("%", "\\%", StringComparison.Ordinal)
         .Replace("_", "\\_", StringComparison.Ordinal);
}
