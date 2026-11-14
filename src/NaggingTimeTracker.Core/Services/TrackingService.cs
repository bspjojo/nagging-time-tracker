using NaggingTimeTracker.Core.Abstractions;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.Core.Services;

/// <summary>
/// The only code that starts, stops or continues entries. A start or continue needs
/// confirmation exactly when it would increase the number of running entries while at least
/// one is already running.
/// </summary>
public sealed class TrackingService(IEntryRepository entries, TimeProvider time)
{
    private static readonly TimeSpan MinStoredDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TooShortThreshold = TimeSpan.FromMinutes(1);

    /// <summary>Raised after every call that changed a row, never after a no-op.</summary>
    public event EventHandler? RunningEntriesChanged;

    public IReadOnlyList<Entry> GetRunning() => entries.GetRunning();

    /// <summary>Whether "Continue last" has anything to continue.</summary>
    public bool HasStoppedEntries() => entries.GetRecentStopped(1).Count > 0;

    /// <summary>The entry "Continue last" would continue (latest stop), or null.</summary>
    public Entry? LastStopped() => entries.GetRecentStopped(1).FirstOrDefault();

    public StartResult Start(StartRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);
        var running = entries.GetRunning();
        if (running.Count > 0 && !req.ConfirmedConcurrent)
        {
            return new StartResult.NeedsConfirmation(running);
        }

        var now = time.NowUtc();
        var entry = entries.Insert(new Entry
        {
            CustomerId = req.CustomerId,
            TaskTypeId = req.TaskTypeId,
            StartUtc = now,
            ExternalLink = BlankToNull(req.ExternalLink),
            Notes = req.Notes ?? "",
            OverlapReason = running.Count > 0 ? BlankToNull(req.OverlapReason) : null,
            CreatedUtc = now,
            UpdatedUtc = now,
        });
        Raise();
        return new StartResult.Started(entry);
    }

    /// <summary>
    /// Stops a running entry. Null if the id doesn't exist; a no-op (no write, no event) if the
    /// entry is already stopped.
    /// </summary>
    public StopResult? Stop(long id)
    {
        var result = StopCore(id);
        if (result is { Changed: true })
        {
            Raise();
        }

        return result?.Result;
    }

    public IReadOnlyList<StopResult> StopAll() => StopMany(null);

    /// <summary>Stops every running entry except <paramref name="keepId"/>.</summary>
    public IReadOnlyList<StopResult> StopAllExcept(long keepId) => StopMany(keepId);

    /// <summary>
    /// Stops a running entry at a given instant (idle resolution). Null if missing or not running.
    /// </summary>
    public Entry? StopAt(long id, DateTimeOffset stopUtc)
    {
        var stored = entries.GetById(id);
        if (stored is null || stored.StopUtc is not null)
        {
            return null;
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stopUtc, stored.StartUtc);
        var stopped = stored with { StopUtc = stopUtc, UpdatedUtc = time.NowUtc() };
        entries.Update(stopped);
        Raise();
        return stopped;
    }

    public StartResult Continue(long sourceId, bool confirmedConcurrent = false, string? overlapReason = null)
    {
        var source = entries.GetById(sourceId);
        if (source is null)
        {
            return new StartResult.NothingToContinue();
        }

        string? reason;
        if (source.StopUtc is null)
        {
            // Continuing a running entry replaces it, so the running count doesn't grow.
            StopCore(sourceId);
            reason = BlankToNull(overlapReason) ?? source.OverlapReason;
        }
        else
        {
            var running = entries.GetRunning();
            if (running.Count > 0 && !confirmedConcurrent)
            {
                return new StartResult.NeedsConfirmation(running);
            }

            reason = running.Count > 0 ? BlankToNull(overlapReason) : null;
        }

        var now = time.NowUtc();
        var entry = entries.Insert(new Entry
        {
            CustomerId = source.CustomerId,
            TaskTypeId = source.TaskTypeId,
            StartUtc = now,
            ExternalLink = source.ExternalLink,
            Notes = "",
            ContinuedFromId = source.Id,
            OverlapReason = reason,
            CreatedUtc = now,
            UpdatedUtc = now,
        });
        Raise();
        return new StartResult.Started(entry);
    }

    public StartResult ContinueLast(bool confirmedConcurrent = false, string? overlapReason = null)
    {
        var last = entries.GetRecentStopped(1);
        return last.Count == 0
            ? new StartResult.NothingToContinue()
            : Continue(last[0].Id, confirmedConcurrent, overlapReason);
    }

    /// <summary>Writes only notes and updated_utc, so a stale copy can never overwrite stop_utc.</summary>
    public bool UpdateNotes(long id, string notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var changed = entries.UpdateNotes(id, notes, time.NowUtc());
        if (changed)
        {
            Raise();
        }

        return changed;
    }

    public bool Delete(long id)
    {
        var deleted = entries.Delete(id);
        if (deleted)
        {
            Raise();
        }

        return deleted;
    }

    /// <summary>
    /// The editor's "no future times" rule, or null when the times are fine. A start, or a stop
    /// when set, is in the future from the first instant of the next local minute on, so a time
    /// typed as the current HH:mm is allowed.
    /// </summary>
    public string? FutureTimeError(DateTimeOffset startUtc, DateTimeOffset? stopUtc)
    {
        var future = StartOfNextLocalMinute();
        return startUtc >= future || stopUtc >= future ? "Times can't be in the future." : null;
    }

    /// <summary>Saves an edited or manually created entry (the editor).</summary>
    public Entry Save(Entry e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.StopUtc is { } stop && stop <= e.StartUtc)
        {
            throw new ArgumentException("Stop must be after start.", nameof(e));
        }

        if (FutureTimeError(e.StartUtc, e.StopUtc) is { } future)
        {
            throw new ArgumentException(future, nameof(e));
        }

        var now = time.NowUtc();
        Entry saved;
        if (e.Id == 0)
        {
            if (e.StopUtc is null)
            {
                throw new InvalidOperationException("A new entry needs a stop time.");
            }

            saved = entries.Insert(e with { ExternalLink = BlankToNull(e.ExternalLink), CreatedUtc = now, UpdatedUtc = now });
        }
        else
        {
            var stored = entries.GetById(e.Id)
                ?? throw new InvalidOperationException($"Entry {e.Id} no longer exists.");
            if (e.StopUtc is null && stored.StopUtc is not null)
            {
                throw new InvalidOperationException("The entry was stopped elsewhere while it was being edited.");
            }

            saved = e with { ExternalLink = BlankToNull(e.ExternalLink), CreatedUtc = stored.CreatedUtc, UpdatedUtc = now };
            entries.Update(saved);
        }

        Raise();
        return saved;
    }

    private IReadOnlyList<StopResult> StopMany(long? keepId)
    {
        var results = new List<StopResult>();
        foreach (var running in entries.GetRunning())
        {
            if (running.Id != keepId && StopCore(running.Id) is { Changed: true } r)
            {
                results.Add(r.Result);
            }
        }

        if (results.Count > 0)
        {
            Raise();
        }

        return results;
    }

    private (StopResult Result, bool Changed)? StopCore(long id)
    {
        var stored = entries.GetById(id);
        if (stored is null)
        {
            return null;
        }

        if (stored.StopUtc is not null)
        {
            return (new StopResult(stored, TooShort: false), false);
        }

        var now = time.NowUtc();
        var stopUtc = now > stored.StartUtc + MinStoredDuration ? now : stored.StartUtc + MinStoredDuration;
        var stopped = stored with { StopUtc = stopUtc, UpdatedUtc = now };
        entries.Update(stopped);
        return (new StopResult(stopped, stopUtc - stored.StartUtc < TooShortThreshold), true);
    }

    // Now in local time, truncated to the minute, plus one minute; returned as UTC.
    private DateTimeOffset StartOfNextLocalMinute()
    {
        var local = TimeZoneInfo.ConvertTime(time.NowUtc(), time.LocalTimeZone);
        return local.AddTicks(-(local.Ticks % TimeSpan.TicksPerMinute)).AddMinutes(1).ToUniversalTime();
    }

    private static string? BlankToNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void Raise() => RunningEntriesChanged?.Invoke(this, EventArgs.Empty);
}
