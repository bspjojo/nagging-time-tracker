using Microsoft.EntityFrameworkCore;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.Data.Repositories;

/// <summary>Shared implementation for customers and task types.</summary>
public abstract class CatalogRepository<T>(IDbContextFactory<TrackerDbContext> factory) : ICatalogRepository<T>
    where T : class, ICatalogItem
{
    public IReadOnlyList<T> GetActive()
    {
        using var db = factory.CreateDbContext();
        return db.Set<T>().AsNoTracking().Where(x => !x.Archived).OrderBy(x => x.Name).ToList();
    }

    public IReadOnlyList<T> GetAll()
    {
        using var db = factory.CreateDbContext();
        return db.Set<T>().AsNoTracking().OrderBy(x => x.Name).ToList();
    }

    public T? FindByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();
        using var db = factory.CreateDbContext();

        // The column's NOCASE collation makes this case-insensitive.
        return db.Set<T>().AsNoTracking().FirstOrDefault(x => x.Name == trimmed);
    }

    public T Insert(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        using var db = factory.CreateDbContext();
        db.Add(item);
        db.SaveChanges();
        return item; // EF wrote the generated id into the instance.
    }

    public bool Rename(long id, string newName)
    {
        ArgumentNullException.ThrowIfNull(newName);
        using var db = factory.CreateDbContext();
        return db.Set<T>().Where(x => x.Id == id)
            .ExecuteUpdate(s => s.SetProperty(x => x.Name, newName)) > 0;
    }

    public bool SetArchived(long id, bool archived)
    {
        using var db = factory.CreateDbContext();
        return db.Set<T>().Where(x => x.Id == id)
            .ExecuteUpdate(s => s.SetProperty(x => x.Archived, archived)) > 0;
    }
}

public sealed class CustomerRepository(IDbContextFactory<TrackerDbContext> factory)
    : CatalogRepository<Customer>(factory), ICustomerRepository;

public sealed class TaskTypeRepository(IDbContextFactory<TrackerDbContext> factory)
    : CatalogRepository<TaskType>(factory), ITaskTypeRepository;
