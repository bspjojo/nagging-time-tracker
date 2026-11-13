using NaggingTimeTracker.Core.Model;

namespace NaggingTimeTracker.Core.Services;

public sealed record StartRequest(
    long CustomerId,
    long TaskTypeId,
    string? ExternalLink,
    string? Notes,
    bool ConfirmedConcurrent = false,
    string? OverlapReason = null);

public abstract record StartResult
{
    private StartResult()
    {
    }

    public sealed record Started(Entry Entry) : StartResult;

    /// <summary>Another timer is running and the caller hasn't confirmed; nothing was inserted.</summary>
    public sealed record NeedsConfirmation(IReadOnlyList<Entry> Running) : StartResult;

    public sealed record NothingToContinue : StartResult;
}

/// <param name="TooShort">The entry is shorter than a minute (the UI may offer Discard).</param>
public sealed record StopResult(Entry Entry, bool TooShort);
