using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Services;
using NaggingTimeTracker.Data;

namespace NaggingTimeTracker.UiTests;

/// <summary>A temp data dir for a test that seeds before launching; deleted on dispose.</summary>
public sealed class SeededDir : IDisposable
{
    public string Path { get; } = AppFixture.NewTempDir();

    public void Dispose() => AppFixture.DeleteDir(Path);
}

/// <summary>Writes test data straight into a data dir's DB, before the app is launched on it.</summary>
public static class Seed
{
    /// <summary>
    /// A stopped entry relative to now, so it's inside the default <i>This week</i> filter and
    /// never in the future. Customer and task type are found or created by name.
    /// </summary>
    public static long StoppedEntry(
        string dataDir, string customer, string taskType, TimeSpan startAgo, TimeSpan stopAgo, string notes = "", string? link = null)
    {
        long id;
        using (var sp = new ServiceCollection()
                   .AddTrackerData(System.IO.Path.Combine(dataDir, "tracker.db"))
                   .AddSingleton(TimeProvider.System)
                   .AddSingleton<CatalogService>()
                   .BuildServiceProvider())
        {
            sp.GetRequiredService<DatabaseInitializer>().Initialize();
            var catalog = sp.GetRequiredService<CatalogService>();
            var customerId = catalog.GetOrCreateCustomer(customer).Id;
            var taskTypeId = catalog.GetOrCreateTaskType(taskType).Id;

            var now = DateTimeOffset.UtcNow;
            now = new DateTimeOffset(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
            id = sp.GetRequiredService<IEntryRepository>().Insert(new Entry
            {
                CustomerId = customerId,
                TaskTypeId = taskTypeId,
                StartUtc = now - startAgo,
                StopUtc = now - stopAgo,
                Notes = notes,
                ExternalLink = link,
                CreatedUtc = now,
                UpdatedUtc = now,
            }).Id;
        }

        SqliteConnection.ClearAllPools();
        return id;
    }
}
