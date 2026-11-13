using Microsoft.EntityFrameworkCore;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Data.Converters;
using NaggingTimeTracker.Data.Entities;

namespace NaggingTimeTracker.Data;

public sealed class TrackerDbContext(DbContextOptions<TrackerDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<TaskType> TaskTypes => Set<TaskType>();
    public DbSet<Entry> Entries => Set<Entry>();
    public DbSet<ScheduledReminder> Reminders => Set<ScheduledReminder>();
    public DbSet<SettingRow> Settings => Set<SettingRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<EpochMsConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<EpochMsConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrackerDbContext).Assembly);
    }
}
