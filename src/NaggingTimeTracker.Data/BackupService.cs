using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Settings;

namespace NaggingTimeTracker.Data;

/// <summary>
/// A daily copy of the database, made at startup: <c>backups\tracker-yyyy-MM-dd.db</c> (local
/// date), keeping the newest <see cref="Keep"/>. Throws on failure; the caller logs and carries on,
/// and the next start retries because <c>last_backup_date</c> wasn't written.
/// </summary>
public sealed partial class BackupService(string dbPath, string backupDir, ISettingsStore settings, TimeProvider time)
{
    public const int Keep = 7;

    /// <summary>The backup file written, or null when today's backup already exists.</summary>
    public string? RunIfDue()
    {
        var local = TimeZoneInfo.ConvertTime(time.GetUtcNow(), time.LocalTimeZone);
        var today = DateOnly.FromDateTime(local.DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (settings.Get(SettingKeys.LastBackupDate) == today)
        {
            return null;
        }

        Directory.CreateDirectory(backupDir);
        var target = Path.Combine(backupDir, $"tracker-{today}.db");
        var temp = target + ".tmp";
        File.Delete(temp);

        // The online backup API takes a consistent snapshot, WAL content included. No pooling, so
        // closing really releases the files before the move and the retention deletes.
        using (var source = new SqliteConnection(ConnectionString(dbPath)))
        using (var dest = new SqliteConnection(ConnectionString(temp)))
        {
            source.Open();
            dest.Open();
            source.BackupDatabase(dest);
        }

        // Copy to .tmp first, so a crash mid-copy never leaves a truncated backup that retention counts.
        File.Move(temp, target, overwrite: true);

        var backups = Directory.GetFiles(backupDir, "tracker-*.db")
            .Where(f => BackupName().IsMatch(Path.GetFileName(f)))
            .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal);
        foreach (var old in backups.Skip(Keep))
        {
            File.Delete(old);
        }

        settings.Set(SettingKeys.LastBackupDate, today);
        return target;
    }

    private static string ConnectionString(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();

    [GeneratedRegex(@"^tracker-\d{4}-\d{2}-\d{2}\.db$")]
    private static partial Regex BackupName();
}
