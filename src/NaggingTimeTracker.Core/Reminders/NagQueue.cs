namespace NaggingTimeTracker.Core.Reminders;

/// <summary>
/// The nag queue: one nag shown at a time, FIFO, held while presentation mode is on. Nags that
/// no longer apply (a timer started; a listed entry stopped) are pruned.
/// </summary>
public sealed class NagQueue
{
    private readonly LinkedList<Nag> _queued = new();
    private readonly HashSet<long> _handled = [];

    /// <summary>The nag on screen, if any.</summary>
    public Nag? Current { get; private set; }

    /// <summary>A nag is shown or queued (counts as "prompt open"; drives the attention icon).</summary>
    public bool HasPending => Current is not null || _queued.Count > 0;

    /// <summary>Entry ids of the current check-in nag that have been answered.</summary>
    public IReadOnlySet<long> HandledCheckIns => _handled;

    public void Enqueue(IEnumerable<Nag> nags)
    {
        ArgumentNullException.ThrowIfNull(nags);
        foreach (var nag in nags)
        {
            _queued.AddLast(nag);
        }
    }

    /// <summary>Promotes the head of the queue when nothing is shown and not presenting; returns the newly shown nag.</summary>
    public Nag? ShowNextIfIdle(bool presentationBusy)
    {
        if (Current is not null || presentationBusy || _queued.First is not { } head)
        {
            return null;
        }

        _queued.RemoveFirst();
        _handled.Clear();
        Current = head.Value;
        return Current;
    }

    public void CloseCurrent()
    {
        Current = null;
        _handled.Clear();
    }

    /// <summary>
    /// Drops not-tracking nags once anything runs, and check-in rows of entries that stopped (a
    /// check-in nag with no due rows left is dropped). Returns whether <see cref="Current"/> changed.
    /// </summary>
    public bool Prune(IReadOnlySet<long> runningIds)
    {
        ArgumentNullException.ThrowIfNull(runningIds);

        for (var node = _queued.First; node is not null;)
        {
            var next = node.Next;
            if (!Keep(node.Value, runningIds))
            {
                _queued.Remove(node);
            }

            node = next;
        }

        if (Current is null)
        {
            return false;
        }

        var rowsBefore = Current.CheckIns.Count;
        if (!Keep(Current, runningIds))
        {
            CloseCurrent();
            return true;
        }

        if (Current.CheckIns.Count == rowsBefore)
        {
            return false;
        }

        // Some listed entries stopped: the remaining due rows may all be answered already.
        if (AllDueHandled(Current))
        {
            CloseCurrent();
        }

        return true;
    }

    /// <summary>A due row of the current check-in was answered; closes the nag once every due row is.</summary>
    public void ResolveCheckIn(long entryId)
    {
        if (Current is not { Kind: NagKind.CheckIn } nag)
        {
            return;
        }

        _handled.Add(entryId);
        if (AllDueHandled(nag))
        {
            CloseCurrent();
        }
    }

    private bool AllDueHandled(Nag nag) => nag.CheckIns.Where(i => i.Due).All(i => _handled.Contains(i.Entry.Id));

    private static bool Keep(Nag nag, IReadOnlySet<long> runningIds)
    {
        switch (nag.Kind)
        {
            case NagKind.NotTracking:
                return runningIds.Count == 0;
            case NagKind.CheckIn:
                nag.CheckIns = nag.CheckIns.Where(i => runningIds.Contains(i.Entry.Id)).ToList();
                return nag.CheckIns.Any(i => i.Due);
            default:
                return true;
        }
    }
}
