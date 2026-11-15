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

    [Fact]
    public void RunningRow_ShowsTheLink_WebUrlClickable()
    {
        const string web = "https://example.com/browse/JIRA-77-another-long-ticket-slug-for-the-running-row";
        using var app = AppFixture.Launch("quick-panel");
        var panel = app.Window;

        SetText(FindById(panel, "QuickPanel.CustomerCombo"), "Acme");
        SetText(FindById(panel, "QuickPanel.TaskTypeCombo"), "Dev Work");
        SetText(FindById(panel, "QuickPanel.LinkBox"), web);
        Invoke(FindById(panel, "QuickPanel.StartButton"));

        Assert.Single(FindAllById(panel, "QuickPanel.RunningRow"));
        Assert.Equal("Acme · Dev Work", FindById(panel, "QuickPanel.RunningRow.Label").Name);
        var link = FindById(panel, "QuickPanel.RunningRow.Link");
        Assert.True(IsShown(link), "the web link is visible");
        Assert.Equal(web, link.Name);
        Assert.True(link.Patterns.Invoke.IsSupported, "the web link is clickable");
        Assert.False(IsShown(TryFindById(panel, "QuickPanel.RunningRow.LinkText")), "no plain-text line for a web link");
    }

    [Fact]
    public void RecentRows_ShowTheLink_WebUrlsClickable()
    {
        const string web = "https://example.com/browse/JIRA-1234-a-rather-long-ticket-slug-that-will-not-fit";
        using var dir = new SeededDir();
        Seed.StoppedEntry(dir.Path, "Acme", "Dev Work", TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(60), link: web);
        Seed.StoppedEntry(dir.Path, "Globex", "Meetings", TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(30), link: "JIRA-2");
        using var app = AppFixture.Launch("quick-panel", dir.Path);
        var panel = app.Window;

        Assert.Equal(2, FindAllById(panel, "QuickPanel.RecentRow", expectedAtLeast: 2).Length);

        // The label no longer carries the link; it has its own line.
        var labels = FindAllById(panel, "QuickPanel.RecentRow.Label", expectedAtLeast: 2).Select(l => l.Name).ToArray();
        Assert.Contains("Acme · Dev Work", labels);
        Assert.Contains("Globex · Meetings", labels);

        // A web URL is a visible hyperlink (it can be invoked; not invoked here, so no browser opens).
        var link = FindById(panel, "QuickPanel.RecentRow.Link");
        Assert.True(IsShown(link), "the web link is visible");
        Assert.Equal(web, link.Name);
        Assert.True(link.Patterns.Invoke.IsSupported, "the web link is clickable");

        // Anything else is plain text, and only the web one is a hyperlink.
        var text = FindById(panel, "QuickPanel.RecentRow.LinkText");
        Assert.True(IsShown(text), "the plain link is visible");
        Assert.Equal("JIRA-2", text.Name);
        Assert.Single(FindAllById(panel, "QuickPanel.RecentRow.Link"));
    }
}
