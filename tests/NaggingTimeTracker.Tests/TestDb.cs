using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Data;

namespace NaggingTimeTracker.Tests;

/// <summary>
/// A real, migrated SQLite database in a temp file, wired through the production
/// <see cref="ServiceCollectionExtensions.AddTrackerData"/> registration.
/// </summary>
public sealed class TestDb : IDisposable
{
    /// <summary>A fixed, ms-precision base instant for tests (Mon 2026-03-02 09:00Z).</summary>
    public static readonly DateTimeOffset T0 = new(2026, 3, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _provider;

    public TestDb()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ntt-tests");
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, $"{Guid.NewGuid():N}.db");

        _provider = new ServiceCollection().AddTrackerData(FilePath).BuildServiceProvider();
        Get<DatabaseInitializer>().Initialize();
    }

    public string FilePath { get; }

    public IDbContextFactory<TrackerDbContext> Factory => Get<IDbContextFactory<TrackerDbContext>>();

    public IEntryRepository Entries => Get<IEntryRepository>();

    public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

    public Customer AddCustomer(string name, bool archived = false) =>
        Get<ICustomerRepository>().Insert(new Customer { Name = name, Archived = archived, CreatedUtc = T0 });

    public Entry AddEntry(
        long customerId,
        DateTimeOffset start,
        DateTimeOffset? stop,
        long taskTypeId = 1,
        string? link = null,
        string notes = "",
        long? continuedFromId = null,
        string? overlapReason = null) =>
        Entries.Insert(new Entry
        {
            CustomerId = customerId,
            TaskTypeId = taskTypeId,
            StartUtc = start,
            StopUtc = stop,
            ExternalLink = link,
            Notes = notes,
            ContinuedFromId = continuedFromId,
            OverlapReason = overlapReason,
            CreatedUtc = start,
            UpdatedUtc = start,
        });

    /// <summary>Runs a scalar SQL query against a fresh connection.</summary>
    public object? Scalar(string sql)
    {
        using var db = Factory.CreateDbContext();
        var conn = db.Database.GetDbConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    /// <summary>Column name → declared type, from PRAGMA table_info.</summary>
    public IReadOnlyDictionary<string, string> ColumnTypes(string table)
    {
        using var db = Factory.CreateDbContext();
        var conn = db.Database.GetDbConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<string, string>();
        while (reader.Read())
        {
            result[reader.GetString(1)] = reader.GetString(2);
        }

        return result;
    }

    public void Dispose()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                File.Delete(FilePath + suffix);
            }
            catch (IOException)
            {
                // Best effort; %TEMP% is cleaned eventually.
            }
        }
    }
}
