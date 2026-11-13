using Microsoft.EntityFrameworkCore;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.Tests.Data;

/// <summary>Runs every case against both customers and task types.</summary>
public sealed class CatalogRepositoryTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    public static TheoryData<string> Kinds => ["customer", "task_type"];

    /// <summary>A uniform view over either repository.</summary>
    private sealed record Repo(
        Func<string, bool, ICatalogItem> Insert,
        Func<IReadOnlyList<ICatalogItem>> GetActive,
        Func<IReadOnlyList<ICatalogItem>> GetAll,
        Func<string, ICatalogItem?> FindByName,
        Func<long, string, bool> Rename,
        Func<long, bool, bool> SetArchived);

    private Repo For(string kind)
    {
        if (kind == "customer")
        {
            var r = _db.Get<ICustomerRepository>();
            return new Repo(
                (n, a) => r.Insert(new Customer { Name = n, Archived = a, CreatedUtc = TestDb.T0 }),
                () => r.GetActive(), () => r.GetAll(), n => r.FindByName(n), r.Rename, r.SetArchived);
        }

        var t = _db.Get<ITaskTypeRepository>();
        using (var ctx = _db.Factory.CreateDbContext())
        {
            ctx.TaskTypes.ExecuteDelete(); // start from an empty catalog, like customers
        }

        return new Repo(
            (n, a) => t.Insert(new TaskType { Name = n, Archived = a, CreatedUtc = TestDb.T0 }),
            () => t.GetActive(), () => t.GetAll(), n => t.FindByName(n), t.Rename, t.SetArchived);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Unique_name_is_case_insensitive(string kind)
    {
        var repo = For(kind);
        repo.Insert("Acme", false);

        Assert.Throws<DbUpdateException>(() => repo.Insert("acme", false));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void FindByName_trims_and_ignores_case(string kind)
    {
        var repo = For(kind);
        var acme = repo.Insert("Acme", false);

        Assert.Equal(acme.Id, repo.FindByName(" ACME ")?.Id);
        Assert.Null(repo.FindByName("Acme Corp"));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void GetActive_excludes_archived_and_orders_case_insensitively(string kind)
    {
        var repo = For(kind);
        repo.Insert("cherry", false);
        repo.Insert("Banana", false);
        repo.Insert("apple", false);
        repo.Insert("Archived one", true);

        Assert.Equal(["apple", "Banana", "cherry"], repo.GetActive().Select(x => x.Name));
        Assert.Equal(["apple", "Archived one", "Banana", "cherry"], repo.GetAll().Select(x => x.Name));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Rename_and_SetArchived_persist(string kind)
    {
        var repo = For(kind);
        var item = repo.Insert("Old", false);

        Assert.True(repo.Rename(item.Id, "New"));
        Assert.True(repo.SetArchived(item.Id, true));

        var stored = Assert.Single(repo.GetAll());
        Assert.Equal("New", stored.Name);
        Assert.True(stored.Archived);
        Assert.Empty(repo.GetActive());

        Assert.True(repo.SetArchived(item.Id, false));
        Assert.Single(repo.GetActive());
        Assert.False(repo.Rename(999, "x"));
        Assert.False(repo.SetArchived(999, true));
    }

    [Fact]
    public void Archived_names_still_resolve_through_QueryViews()
    {
        var customer = _db.AddCustomer("Gone Ltd");
        var type = _db.Get<ITaskTypeRepository>().Insert(new TaskType { Name = "Old type", CreatedUtc = TestDb.T0 });
        _db.AddEntry(customer.Id, TestDb.T0, TestDb.T0.AddHours(1), taskTypeId: type.Id);
        _db.Get<ICustomerRepository>().SetArchived(customer.Id, true);
        _db.Get<ITaskTypeRepository>().SetArchived(type.Id, true);

        var view = Assert.Single(_db.Entries.QueryViews(new EntryFilter(TestDb.T0, TestDb.T0.AddDays(1))));

        Assert.Equal("Gone Ltd", view.CustomerName);
        Assert.Equal("Old type", view.TaskTypeName);
    }
}
