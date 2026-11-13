namespace NaggingTimeTracker.Data;

/// <summary>Resolves the app's data locations. The constructor has no side effects.</summary>
public sealed class DataPaths
{
    public DataPaths(string? rootOverride = null)
    {
        Root = string.IsNullOrWhiteSpace(rootOverride)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NaggingTimeTracker")
            : Path.GetFullPath(rootOverride);
        DbPath = Path.Combine(Root, "tracker.db");
        BackupDir = Path.Combine(Root, "backups");
    }

    public string Root { get; }
    public string DbPath { get; }
    public string BackupDir { get; }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(BackupDir);
    }
}
