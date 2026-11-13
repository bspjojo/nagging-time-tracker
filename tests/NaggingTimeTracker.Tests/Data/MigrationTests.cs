using Microsoft.EntityFrameworkCore;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Data;
using NaggingTimeTracker.Data.Configurations;

namespace NaggingTimeTracker.Tests.Data;

public sealed class MigrationTests : IDisposable
{
    private readonly TestDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Fresh_database_has_only_InitialCreate_and_no_pending_changes()
    {
        using var ctx = _db.Factory.CreateDbContext();

        var applied = ctx.Database.GetAppliedMigrations().ToList();
        Assert.Single(applied);
        Assert.EndsWith("_InitialCreate", applied[0]);
        Assert.Empty(ctx.Database.GetPendingMigrations());
        Assert.False(ctx.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Seeds_five_task_types()
    {
        var types = _db.Get<ITaskTypeRepository>().GetAll().OrderBy(t => t.Id).ToList();

        Assert.Equal([1L, 2, 3, 4, 5], types.Select(t => t.Id));
        Assert.Equal(TaskTypeConfiguration.SeedNames, types.Select(t => t.Name));
        Assert.All(types, t =>
        {
            Assert.False(t.Archived);
            Assert.Equal(TaskTypeConfiguration.SeedCreatedUtcMs, t.CreatedUtc.ToUnixTimeMilliseconds());
        });
    }

    [Fact]
    public void Initialize_twice_is_a_no_op()
    {
        _db.Get<DatabaseInitializer>().Initialize();

        using var ctx = _db.Factory.CreateDbContext();
        Assert.Single(ctx.Database.GetAppliedMigrations());
        Assert.Equal(5, ctx.TaskTypes.Count());
    }

    [Fact]
    public void Journal_mode_is_wal()
    {
        Assert.Equal("wal", _db.Scalar("PRAGMA journal_mode;"));
    }

    [Fact]
    public void User_ids_continue_after_seeded_ids()
    {
        var customer = _db.AddCustomer("Acme");
        var type = _db.Get<ITaskTypeRepository>().Insert(new TaskType { Name = "Research", CreatedUtc = TestDb.T0 });

        Assert.Equal(1, customer.Id);
        Assert.Equal(6, type.Id);
    }
}
