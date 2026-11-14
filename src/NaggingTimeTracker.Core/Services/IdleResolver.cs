using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.Core.Services;

public enum IdleChoice
{
    Keep,
    DiscardAndContinue,
    DiscardAndStop,
}

/// <summary>
/// Applies the idle prompt's choice to the entries that were running across the idle period.
/// Entries started during it are never touched.
/// </summary>
public sealed class IdleResolver(TrackingService tracking)
{
    /// <summary>Running entries that started before the idle period, oldest start first.</summary>
    public IReadOnlyList<Entry> Affected(DateTimeOffset idleStartUtc) =>
        tracking.GetRunning()
            .Where(e => e.StartUtc < idleStartUtc)
            .OrderBy(e => e.StartUtc)
            .ThenBy(e => e.Id)
            .ToList();

    public void Apply(IdleChoice choice, DateTimeOffset idleStartUtc)
    {
        if (choice == IdleChoice.Keep)
        {
            return;
        }

        // Re-read, so an entry stopped elsewhere while the prompt was open is skipped.
        foreach (var e in Affected(idleStartUtc))
        {
            var stopped = tracking.StopAt(e.Id, idleStartUtc);
            if (stopped is not null && choice == IdleChoice.DiscardAndContinue)
            {
                // The source is stopped now, so this replaces it without growing the running count.
                tracking.Continue(e.Id, confirmedConcurrent: true, overlapReason: e.OverlapReason);
            }
        }
    }
}
