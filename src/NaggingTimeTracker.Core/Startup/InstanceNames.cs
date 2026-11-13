using System.Security.Cryptography;
using System.Text;

namespace NaggingTimeTracker.Core.Startup;

/// <summary>
/// Names of the single-instance mutex and show event. Under <c>--data-dir</c> both get a
/// suffix derived from the directory, so a test instance never collides with the real one.
/// </summary>
public sealed record InstanceNames(string Mutex, string ShowEvent)
{
    private const string Base = @"Local\NaggingTimeTracker";

    public static InstanceNames For(string? dataDir)
    {
        if (string.IsNullOrWhiteSpace(dataDir))
        {
            return new InstanceNames(Base, Base + ".Show");
        }

        // NTFS paths are case-insensitive and a trailing separator doesn't change the folder.
        var normalized = Path.GetFullPath(dataDir).TrimEnd('\\', '/').ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var suffix = "." + Convert.ToHexStringLower(hash)[..8];
        return new InstanceNames(Base + suffix, Base + suffix + ".Show");
    }
}
