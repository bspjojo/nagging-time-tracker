using System.Windows;
using NaggingTimeTracker.App.Windows;
using NaggingTimeTracker.Core.Services;
using NaggingTimeTracker.Core.Time;

namespace NaggingTimeTracker.App.Tracking;

/// <summary>
/// The UI flow around <see cref="TrackingService"/>: confirm-and-retry for concurrent starts,
/// and the Discard/Keep prompt after an explicit single stop. Callers that hide on deactivation
/// wrap calls in their own modal guard.
/// </summary>
public sealed class TrackingActions(TrackingService tracking, CatalogService catalog, TimeProvider time)
{
    public StartResult Start(StartRequest req, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(req);
        return Confirmed(
            tracking.Start(req),
            owner,
            reason => tracking.Start(req with { ConfirmedConcurrent = true, OverlapReason = reason }));
    }

    public StartResult Continue(long sourceId, Window? owner) =>
        Confirmed(
            tracking.Continue(sourceId),
            owner,
            reason => tracking.Continue(sourceId, confirmedConcurrent: true, overlapReason: reason));

    /// <summary>
    /// Resolves "last" once, before any dialog: "Stop others" stops the running entry, which then
    /// becomes the latest stop, so re-resolving in the retry would continue the wrong entry.
    /// </summary>
    public StartResult ContinueLast(Window? owner) =>
        tracking.LastStopped() is { } last
            ? Continue(last.Id, owner)
            : new StartResult.NothingToContinue();

    /// <summary>An explicit single stop: offers Discard/Keep when the entry is under a minute.</summary>
    public void StopSingle(long id, Window? owner)
    {
        var result = tracking.Stop(id);
        if (result is not { TooShort: true })
        {
            return;
        }

        var names = catalog.GetNames();
        var dialog = new TooShortDialog(
            names.Customer(result.Entry.CustomerId),
            names.TaskType(result.Entry.TaskTypeId),
            result.Entry.StopUtc!.Value - result.Entry.StartUtc);
        SetOwner(dialog, owner);
        dialog.ShowDialog();
        if (dialog.Discard)
        {
            tracking.Delete(id);
        }
    }

    private StartResult Confirmed(StartResult first, Window? owner, Func<string?, StartResult> retry)
    {
        if (first is not StartResult.NeedsConfirmation needs)
        {
            return first;
        }

        var dialog = new ConcurrencyDialog(needs.Running, catalog.GetNames(), time.NowUtc());
        SetOwner(dialog, owner);
        dialog.ShowDialog();

        switch (dialog.Choice)
        {
            case ConcurrencyChoice.StopOthers:
                tracking.StopAll();
                return retry(null);
            case ConcurrencyChoice.RunInParallel:
                return retry(string.IsNullOrWhiteSpace(dialog.Reason) ? null : dialog.Reason.Trim());
            default:
                return first;
        }
    }

    private static void SetOwner(Window dialog, Window? owner)
    {
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }
}
