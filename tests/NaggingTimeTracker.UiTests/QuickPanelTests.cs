using static NaggingTimeTracker.UiTests.Ui;

namespace NaggingTimeTracker.UiTests;

[Trait("Category", "UI")]
public sealed class QuickPanelTests
{
    [Fact]
    public void Start_NewCustomerAndType_RunsAndSurvivesRestart()
    {
        using var app = AppFixture.Launch("quick-panel");
        var panel = app.Window;
        Assert.True(IsShown(FindById(panel, "QuickPanel.NothingRunning")), "a fresh DB shows 'Nothing running'");

        SetText(FindById(panel, "QuickPanel.CustomerCombo"), "Acme");
        SetText(FindById(panel, "QuickPanel.TaskTypeCombo"), "Research");
        Invoke(FindById(panel, "QuickPanel.StartButton"));

        Assert.Single(FindAllById(panel, "QuickPanel.RunningRow"));
        Assert.Equal("Acme · Research", FindById(panel, "QuickPanel.RunningRow.Label").Name);

        app.Relaunch("entries");
        GridRow(app.Window, "running, Acme · Research");
    }
}
