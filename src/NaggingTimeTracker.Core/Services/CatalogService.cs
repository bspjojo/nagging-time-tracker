using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.Core.Services;

public enum RenameResult
{
    Renamed,
    NameTaken,
    NotFound,
}

/// <summary>Id → name lookups for labelling rows; archived items included.</summary>
public sealed class CatalogNames(IReadOnlyDictionary<long, string> customers, IReadOnlyDictionary<long, string> taskTypes)
{
    public string Customer(long id) => customers.GetValueOrDefault(id, "?");

    public string TaskType(long id) => taskTypes.GetValueOrDefault(id, "?");
}

/// <summary>Customers and task types: typed names resolve to existing items or create new ones.</summary>
public sealed class CatalogService(ICustomerRepository customers, ITaskTypeRepository taskTypes, TimeProvider time)
{
    public Customer GetOrCreateCustomer(string name) =>
        GetOrCreate(customers, name, n => new Customer { Name = n, CreatedUtc = time.NowUtc() }, c => c with { Archived = false });

    public TaskType GetOrCreateTaskType(string name) =>
        GetOrCreate(taskTypes, name, n => new TaskType { Name = n, CreatedUtc = time.NowUtc() }, t => t with { Archived = false });

    public RenameResult RenameCustomer(long id, string newName) => Rename(customers, id, newName);

    public RenameResult RenameTaskType(long id, string newName) => Rename(taskTypes, id, newName);

    public bool SetCustomerArchived(long id, bool archived) => customers.SetArchived(id, archived);

    public bool SetTaskTypeArchived(long id, bool archived) => taskTypes.SetArchived(id, archived);

    public IReadOnlyList<Customer> GetActiveCustomers() => customers.GetActive();

    public IReadOnlyList<TaskType> GetActiveTaskTypes() => taskTypes.GetActive();

    public CatalogNames GetNames() => new(
        customers.GetAll().ToDictionary(c => c.Id, c => c.Name),
        taskTypes.GetAll().ToDictionary(t => t.Id, t => t.Name));

    private static T GetOrCreate<T>(ICatalogRepository<T> repo, string name, Func<string, T> create, Func<T, T> unarchived)
        where T : ICatalogItem
    {
        var trimmed = RequireName(name);
        var existing = repo.FindByName(trimmed);
        if (existing is null)
        {
            return repo.Insert(create(trimmed));
        }

        if (existing.Archived)
        {
            repo.SetArchived(existing.Id, false);
            return unarchived(existing);
        }

        return existing;
    }

    private static RenameResult Rename<T>(ICatalogRepository<T> repo, long id, string newName)
        where T : ICatalogItem
    {
        var trimmed = RequireName(newName);
        if (!repo.GetAll().Any(x => x.Id == id))
        {
            return RenameResult.NotFound;
        }

        if (repo.FindByName(trimmed) is { } other && other.Id != id)
        {
            return RenameResult.NameTaken;
        }

        repo.Rename(id, trimmed);
        return RenameResult.Renamed;
    }

    private static string RequireName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("A name is required.", nameof(name));
        }

        return trimmed;
    }
}
