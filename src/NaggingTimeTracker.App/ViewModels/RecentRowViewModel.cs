using NaggingTimeTracker.Core.Formatting;

namespace NaggingTimeTracker.App.ViewModels;

/// <summary>
/// A row of the quick panel's Recent section. <see cref="Label"/> is "customer · task type"; the
/// external link, if any, is shown on its own line: a hyperlink when it's an http(s) URL, plain
/// text otherwise (the same rule as the Entries grid).
/// </summary>
public sealed record RecentRowViewModel(long Id, string Label, string? Link)
{
    public Uri? LinkUri { get; } = EntryText.IsWebUrl(Link, out var uri) ? uri : null;

    public bool HasWebLink => LinkUri is not null;

    public bool HasPlainLink => !string.IsNullOrWhiteSpace(Link) && LinkUri is null;

    /// <summary>The row's name for screen readers and UI tests: the label plus the link.</summary>
    public string AccessibleName => string.IsNullOrWhiteSpace(Link) ? Label : $"{Label} · {Link}";
}
