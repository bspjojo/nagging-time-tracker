using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.Core.Abstractions;

// All repository methods are synchronous: Microsoft.Data.Sqlite has no true async I/O.
// A bool return means "a row was affected".

public interface ICatalogRepository<T> where T : ICatalogItem
{
    /// <summary>Non-archived items, ordered by name (case-insensitive).</summary>
    IReadOnlyList<T> GetActive();

    /// <summary>All items, archived included, ordered by name.</summary>
    IReadOnlyList<T> GetAll();

    /// <summary>Case-insensitive lookup of the trimmed name.</summary>
    T? FindByName(string name);

    /// <summary>Inserts the item and returns it with its generated id.</summary>
    T Insert(T item);

    /// <summary>Throws on a duplicate name (unique index); callers pre-check.</summary>
    bool Rename(long id, string newName);

    bool SetArchived(long id, bool archived);
}

public interface ICustomerRepository : ICatalogRepository<Customer>
{
}

public interface ITaskTypeRepository : ICatalogRepository<TaskType>
{
}

public interface IEntryRepository
{
    /// <summary>Running entries, ordered by start.</summary>
    IReadOnlyList<Entry> GetRunning();

    Entry? GetById(long id);

    /// <summary>The most recently started entry, running or stopped; null on an empty DB.</summary>
    Entry? GetLatestStarted();

    /// <summary>The <paramref name="n"/> stopped entries with the latest stop, newest first.</summary>
    IReadOnlyList<Entry> GetRecentStopped(int n);

    /// <summary>
    /// The newest stopped entry of each distinct (customer, task type, external link),
    /// newest stop first, at most <paramref name="n"/>.
    /// </summary>
    IReadOnlyList<Entry> GetRecentDistinct(int n);

    /// <summary>Entries matching the filter, ordered by start.</summary>
    IReadOnlyList<Entry> Query(EntryFilter filter);

    /// <summary>Same as <see cref="Query"/>, with customer and task type names.</summary>
    IReadOnlyList<EntryView> QueryViews(EntryFilter filter);

    /// <summary>
    /// Entries overlapping [startUtc, stopUtc ?? nowUtc); running entries end at nowUtc.
    /// Touching at a boundary is not an overlap.
    /// </summary>
    IReadOnlyList<Entry> GetOverlapping(
        DateTimeOffset startUtc, DateTimeOffset? stopUtc, long? excludeId, DateTimeOffset nowUtc);

    /// <summary>Ids of entries starting in [fromUtc, toUtc) that overlap any other entry.</summary>
    IReadOnlySet<long> GetOverlappingIds(DateTimeOffset fromUtc, DateTimeOffset toUtc, DateTimeOffset nowUtc);

    /// <summary>Inserts a new entry (Id must be 0) and returns it with its generated id.</summary>
    Entry Insert(Entry entry);

    /// <summary>Writes every column except id and created_utc.</summary>
    bool Update(Entry entry);

    /// <summary>Writes only notes and updated_utc.</summary>
    bool UpdateNotes(long id, string notes, DateTimeOffset updatedUtc);

    bool Delete(long id);
}

public interface IReminderRepository
{
    /// <summary>All reminders, ordered by time then title.</summary>
    IReadOnlyList<ScheduledReminder> GetAll();

    ScheduledReminder? GetById(long id);

    ScheduledReminder Insert(ScheduledReminder reminder);

    bool Update(ScheduledReminder reminder);

    bool Delete(long id);

    bool SetLastFired(long id, DateTimeOffset utc);
}

public interface ISettingsStore
{
    /// <summary>The stored value, or null when unset.</summary>
    string? Get(string key);

    void Set(string key, string value);

    /// <summary>Removes the key; a no-op when it is unset.</summary>
    void Remove(string key);
}
