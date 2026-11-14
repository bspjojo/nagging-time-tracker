using NaggingTimeTracker.App.Windows;
using NaggingTimeTracker.Core.Idle;
using NaggingTimeTracker.Core.Model;
using NaggingTimeTracker.Core.Services;

namespace NaggingTimeTracker.App.Idle;

internal static class IdlePromptLabels
{
    /// <summary>"customer · task type" per entry, customer first as everywhere else.</summary>
    public static IReadOnlyList<string> For(IEnumerable<Entry> entries, CatalogService catalog)
    {
        var names = catalog.GetNames();
        return entries.Select(e => $"{names.Customer(e.CustomerId)} · {names.TaskType(e.TaskTypeId)}").ToList();
    }

    /// <summary>Shows the prompt for the affected entries and applies the choice. No-op when none are affected.</summary>
    public static void Ask(IdlePromptKind kind, IdleSpan span, IdleResolver resolver, CatalogService catalog, TimeProvider time)
    {
        var affected = resolver.Affected(span.StartUtc);
        if (affected.Count == 0)
        {
            return;
        }

        var dialog = new IdlePromptDialog(kind, span, For(affected, catalog), time.LocalTimeZone);
        dialog.ShowDialog();
        resolver.Apply(dialog.Choice, span.StartUtc);
    }
}
