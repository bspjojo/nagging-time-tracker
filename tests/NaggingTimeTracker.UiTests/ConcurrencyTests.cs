using static NaggingTimeTracker.UiTests.Ui;

namespace NaggingTimeTracker.UiTests;

[Trait("Category", "UI")]
public sealed class ConcurrencyTests
{
    [Fact]
    public async Task SecondTimer_RunInParallel_ThenKeepOnlyThis()
    {
        using var app = AppFixture.Launch("quick-panel");
        var panel = app.Window;

        SetText(FindById(panel, "QuickPanel.CustomerCombo"), "Acme");
        SetText(FindById(panel, "QuickPanel.TaskTypeCombo"), "Dev Work");
        Invoke(FindById(panel, "QuickPanel.StartButton"));
        FindAllById(panel, "QuickPanel.RunningRow");

        SetText(FindById(panel, "QuickPanel.CustomerCombo"), "Globex");
        SetText(FindById(panel, "QuickPanel.TaskTypeCombo"), "Meetings");
        var start = InvokeAsync(FindById(panel, "QuickPanel.StartButton"));

        var dialog = app.WaitForWindow("ConcurrencyDialog");
        SetText(FindById(dialog, "ConcurrencyDialog.ReasonBox"), "pairing");
        Invoke(FindById(dialog, "ConcurrencyDialog.RunInParallelButton"));
        await start;

        Assert.Equal(2, FindAllById(panel, "QuickPanel.RunningRow", expectedAtLeast: 2).Length);
        var banner = FindById(panel, "QuickPanel.ParallelBanner");
        Assert.True(IsShown(banner), "the parallel banner is shown");
        Assert.Equal("⚠ 2 timers running in parallel", banner.Name);

        Invoke(FindAllById(panel, "QuickPanel.RunningRow.KeepOnlyButton", expectedAtLeast: 2)[0]);
        WaitUntil(
            () => FindAllById(panel, "QuickPanel.RunningRow", expectedAtLeast: 0).Length == 1,
            "Keep only this leaves exactly one running row");
        WaitUntil(
            () => !IsShown(TryFindById(panel, "QuickPanel.ParallelBanner")),
            "the parallel banner goes away");
    }
}
