using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Services;

namespace NaggingTimeTracker.Tests.Core;

/// <summary>Runs each case for customers and for task types.</summary>
public sealed class CatalogServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _time = new(TestDb.T0);
    private readonly CatalogService _service;

    public CatalogServiceTests()
    {
        // Start task types from an empty catalog too, so both kinds behave the same.
        using (var ctx = _db.Factory.CreateDbContext())
        {
            ctx.TaskTypes.ExecuteDelete();
        }

        _service = new CatalogService(_db.Get<ICustomerRepository>(), _db.Get<ITaskTypeRepository>(), _time);
    }

    public void Dispose() => _db.Dispose();

    public static TheoryData<string> Kinds => ["customer", "task_type"];

    private sealed record Ops(
        Func<string, ICatalogItem> GetOrCreate,
        Func<long, string, RenameResult> Rename,
        Func<long, bool, bool> SetArchived,
        Func<IEnumerable<ICatalogItem>> GetActive,
        Func<CatalogNames, long, string> Name);

    private Ops For(string kind) => kind == "customer"
        ? new Ops(_service.GetOrCreateCustomer, _service.RenameCustomer, _service.SetCustomerArchived,
            _service.GetActiveCustomers, (n, id) => n.Customer(id))
        : new Ops(_service.GetOrCreateTaskType, _service.RenameTaskType, _service.SetTaskTypeArchived,
            _service.GetActiveTaskTypes, (n, id) => n.TaskType(id));

    [Theory]
    [MemberData(nameof(Kinds))]
    public void GetOrCreate_trims_and_creates_with_now(string kind)
    {
        var ops = For(kind);

        var item = ops.GetOrCreate("  Acme  ");

        Assert.Equal("Acme", item.Name);
        Assert.NotEqual(0, item.Id);
        var created = item is Customer c ? c.CreatedUtc : ((TaskType)item).CreatedUtc;
        Assert.Equal(TestDb.T0, created);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void GetOrCreate_matches_case_insensitively_and_keeps_stored_name(string kind)
    {
        var ops = For(kind);
        var first = ops.GetOrCreate("Acme");

        var second = ops.GetOrCreate("acme");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Acme", second.Name);
        Assert.Single(ops.GetActive());
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void GetOrCreate_unarchives_an_archived_match(string kind)
    {
        var ops = For(kind);
        var item = ops.GetOrCreate("Acme");
        ops.SetArchived(item.Id, true);
        Assert.Empty(ops.GetActive());

        var again = ops.GetOrCreate("ACME");

        Assert.Equal(item.Id, again.Id);
        Assert.False(again.Archived);
        Assert.Single(ops.GetActive());
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Empty_names_throw(string kind)
    {
        var ops = For(kind);

        Assert.Throws<ArgumentException>(() => ops.GetOrCreate(""));
        Assert.Throws<ArgumentException>(() => ops.GetOrCreate("   "));
        Assert.Throws<ArgumentException>(() => ops.Rename(1, " "));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Rename_to_another_rows_name_in_any_case_is_NameTaken(string kind)
    {
        var ops = For(kind);
        ops.GetOrCreate("Acme");
        var globex = ops.GetOrCreate("Globex");

        Assert.Equal(RenameResult.NameTaken, ops.Rename(globex.Id, "ACME"));
        Assert.Equal("Globex", ops.Name(_service.GetNames(), globex.Id));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Rename_to_case_change_of_own_name_succeeds(string kind)
    {
        var ops = For(kind);
        var item = ops.GetOrCreate("acme");

        Assert.Equal(RenameResult.Renamed, ops.Rename(item.Id, " Acme "));
        Assert.Equal("Acme", ops.Name(_service.GetNames(), item.Id));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Rename_of_missing_id_is_NotFound(string kind)
    {
        Assert.Equal(RenameResult.NotFound, For(kind).Rename(999, "x"));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Archive_toggles_active_list(string kind)
    {
        var ops = For(kind);
        var item = ops.GetOrCreate("Acme");

        Assert.True(ops.SetArchived(item.Id, true));
        Assert.Empty(ops.GetActive());
        Assert.True(ops.SetArchived(item.Id, false));
        Assert.Single(ops.GetActive());
    }

    [Fact]
    public void GetAll_includes_archived_items_ordered_by_name()
    {
        var b = _service.GetOrCreateCustomer("beta");
        _service.GetOrCreateCustomer("Alpha");
        _service.SetCustomerArchived(b.Id, true);
        var t = _service.GetOrCreateTaskType("zeta");
        _service.GetOrCreateTaskType("Eta");
        _service.SetTaskTypeArchived(t.Id, true);

        Assert.Equal(["Alpha", "beta"], _service.GetAllCustomers().Select(c => c.Name));
        Assert.Equal(["Eta", "zeta"], _service.GetAllTaskTypes().Select(x => x.Name));
        Assert.True(_service.GetAllCustomers()[1].Archived);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void GetNames_resolves_archived_items_and_unknown_ids(string kind)
    {
        var ops = For(kind);
        var item = ops.GetOrCreate("Old");
        ops.SetArchived(item.Id, true);

        var names = _service.GetNames();

        Assert.Equal("Old", ops.Name(names, item.Id));
        Assert.Equal("?", ops.Name(names, 999));
    }
}
