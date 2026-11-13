using NaggingTimeTracker.Core.Tray;

namespace NaggingTimeTracker.Tests.Core;

public sealed class TrayToolTipTextTests
{
    [Fact]
    public void Nothing_running()
    {
        Assert.Equal("Not tracking", TrayToolTipText.Build([]));
    }

    [Fact]
    public void One_running_is_just_its_line()
    {
        Assert.Equal("Acme · Dev Work — 1h 23m", TrayToolTipText.Build(["Acme · Dev Work — 1h 23m"]));
    }

    [Fact]
    public void Several_running_get_the_parallel_header_then_one_line_each()
    {
        var text = TrayToolTipText.Build(["Acme · Dev Work — 1h 23m", "Globex · Meetings — 5m"]);

        Assert.Equal("⚠ 2 timers running in parallel\nAcme · Dev Work — 1h 23m\nGlobex · Meetings — 5m", text);
    }

    [Fact]
    public void Too_many_lines_end_with_and_N_more_within_the_limit()
    {
        var lines = Enumerable.Range(1, 8).Select(i => $"Customer number {i} · Development — {i}h 0m").ToList();

        var text = TrayToolTipText.Build(lines);

        Assert.True(text.Length <= TrayToolTipText.MaxLength, $"length {text.Length}");
        Assert.StartsWith("⚠ 8 timers running in parallel\nCustomer number 1", text, StringComparison.Ordinal);
        var shown = text.Split('\n').Count(l => l.StartsWith("Customer", StringComparison.Ordinal));
        Assert.EndsWith($"…and {8 - shown} more", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_very_long_line_is_cut_with_an_ellipsis()
    {
        var text = TrayToolTipText.Build([new string('x', 300)]);

        Assert.Equal(TrayToolTipText.MaxLength, text.Length);
        Assert.EndsWith("…", text, StringComparison.Ordinal);
    }
}
