using static NaggingTimeTracker.UiTests.Ui;

namespace NaggingTimeTracker.UiTests;

[Trait("Category", "UI")]
public sealed class ContinueTests
{
    [Fact]
    public async Task StopThenContinueFromRecent_NewRunningRowMarkedContinued()
    {
        using var app = AppFixture.Launch("quick-panel");
        var panel = app.Window;

        SetText(FindById(panel, "QuickPanel.CustomerCombo"), "Acme");
        SetText(FindById(panel, "QuickPanel.TaskTypeCombo"), "Dev Work");
        Invoke(FindById(panel, "QuickPanel.StartButton"));

        // Stopping within a minute asks whether to keep the entry.
        var stop = InvokeAsync(FindById(panel, "QuickPanel.RunningRow.StopButton"));
        var tooShort = app.WaitForWindow("TooShortDialog");
        Invoke(FindById(tooShort, "TooShortDialog.KeepButton"));
        await stop;

        Assert.Single(FindAllById(panel, "QuickPanel.RecentRow"));
        Invoke(FindById(panel, "QuickPanel.RecentRow.ContinueButton"));

        Assert.Single(FindAllById(panel, "QuickPanel.RunningRow"));
        Assert.Equal("Acme · Dev Work", FindById(panel, "QuickPanel.RunningRow.Label").Name);

        Invoke(FindById(panel, "QuickPanel.AllEntriesLink"));
        var entries = app.WaitForWindow("EntriesWindow");
        WaitUntil(
            () => HasText(GridRow(entries, "running, Acme · Dev Work"), "↪"),
            "the running row shows the ↪ marker");
    }
}
