using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Settings;

namespace NaggingTimeTracker.Core.Idle;

/// <summary>The startup check: was the app down long enough, with timers running, to ask?</summary>
public static class Downtime
{
    /// <summary>The period the app wasn't running, or null when there's nothing to ask about.</summary>
    public static IdleSpan? Find(DateTimeOffset? heartbeatUtc, DateTimeOffset nowUtc, AppSettings s, IReadOnlyList<Entry> running)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(running);

        if (heartbeatUtc is not { } beat || !s.IdleEnabled)
        {
            return null;
        }

        if (nowUtc - beat < TimeSpan.FromMinutes(s.IdleThresholdMin))
        {
            return null;
        }

        return running.Any(e => e.StartUtc < beat) ? new IdleSpan(beat, nowUtc) : null;
    }
}
