using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Time.Testing;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Settings;
using NaggingTimeTracker.Data;

namespace NaggingTimeTracker.Tests.Data;

public sealed class BackupServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _time = new(TestDb.T0); // Mon 2026-03-02 09:00Z = 04:00 in New York
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ntt-tests", "backups-" + Guid.NewGuid().ToString("N"));
    private readonly BackupService _backup;

    public BackupServiceTests()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));
        _backup = new BackupService(_db.FilePath, _dir, Settings, _time);
    }

    private ISettingsStore Settings => _db.Get<ISettingsStore>();

    public void Dispose()
    {
        _db.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort (includes DirectoryNotFoundException); %TEMP% is cleaned eventually.
        }
    }

    private string[] Names() => Directory.GetFiles(_dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void First_run_writes_todays_backup_and_records_the_date()
    {
        var path = _backup.RunIfDue();

        Assert.Equal(Path.Combine(_dir, "tracker-2026-03-02.db"), path);
        Assert.True(File.Exists(path));
        Assert.Equal("2026-03-02", Settings.Get(SettingKeys.LastBackupDate));
    }

    [Fact]
    public void Backup_is_a_valid_database_with_the_same_entries()
    {
        var acme = _db.AddCustomer("Acme").Id;
        _db.AddEntry(acme, TestDb.T0, TestDb.T0.AddHours(1));
        _db.AddEntry(acme, TestDb.T0.AddHours(2), null);

        var path = _backup.RunIfDue()!;

        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM entries";
        Assert.Equal(2L, cmd.ExecuteScalar());
    }

    [Fact]
    public void Second_run_the_same_day_does_nothing()
    {
        var path = _backup.RunIfDue()!;
        var written = File.GetLastWriteTimeUtc(path);
        _time.Advance(TimeSpan.FromHours(10));

        Assert.Null(_backup.RunIfDue());
        Assert.Equal(["tracker-2026-03-02.db"], Names());
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Next_local_day_writes_another()
    {
        _backup.RunIfDue();
        _time.Advance(TimeSpan.FromDays(1));
        _backup.RunIfDue();

        Assert.Equal(["tracker-2026-03-02.db", "tracker-2026-03-03.db"], Names());
    }

    [Fact]
    public void Today_is_the_local_date_not_utc()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 3, 3, 4, 30, 0, TimeSpan.Zero)); // 23:30 on the 2nd in New York

        Assert.EndsWith("tracker-2026-03-02.db", _backup.RunIfDue());
    }

    [Fact]
    public void Keeps_the_newest_seven_and_ignores_other_files()
    {
        Directory.CreateDirectory(_dir);
        for (var day = 20; day <= 27; day++)
        {
            File.WriteAllText(Path.Combine(_dir, $"tracker-2026-02-{day}.db"), "old");
        }

        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "mine");
        File.WriteAllText(Path.Combine(_dir, "tracker-copy.db"), "mine");

        _backup.RunIfDue();

        Assert.Equal(
            [
                "notes.txt",
                "tracker-2026-02-22.db", "tracker-2026-02-23.db", "tracker-2026-02-24.db",
                "tracker-2026-02-25.db", "tracker-2026-02-26.db", "tracker-2026-02-27.db",
                "tracker-2026-03-02.db",
                "tracker-copy.db",
            ],
            Names());
    }

    [Fact]
    public void Leftover_tmp_is_replaced_and_removed()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "tracker-2026-03-02.db.tmp"), "half a copy");

        _backup.RunIfDue();

        Assert.Equal(["tracker-2026-03-02.db"], Names());
    }
}
