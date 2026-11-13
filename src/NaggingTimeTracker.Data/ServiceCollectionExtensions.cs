using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Data.Repositories;

namespace NaggingTimeTracker.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTrackerData(this IServiceCollection services, string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            ForeignKeys = true,
        }.ToString();

        services.AddDbContextFactory<TrackerDbContext>(o => o.UseSqlite(connectionString));
        services.AddSingleton<ICustomerRepository, CustomerRepository>();
        services.AddSingleton<ITaskTypeRepository, TaskTypeRepository>();
        services.AddSingleton<IEntryRepository, EntryRepository>();
        services.AddSingleton<IReminderRepository, ReminderRepository>();
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<DatabaseInitializer>();
        return services;
    }
}
