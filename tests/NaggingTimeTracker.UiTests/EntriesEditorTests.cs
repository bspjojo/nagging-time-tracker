using FlaUI.Core.AutomationElements;
using static NaggingTimeTracker.UiTests.Ui;

namespace NaggingTimeTracker.UiTests;

[Trait("Category", "UI")]
public sealed class EntriesEditorTests
{
    [Fact]
    public void EditNotes_Save_GridShowsNewNotes()
    {
        using var dir = new SeededDir();
        using var app = LaunchSeeded(dir);

        var editor = OpenEditor(app);
        SetText(FindById(editor, "EntryEditor.NotesBox"), "after");
        Invoke(FindById(editor, "EntryEditor.SaveButton"));

        WaitUntil(() => app.FindTopWindow("EntryEditor") is null, "Save closes the editor");
        WaitUntil(() => HasText(GridRow(app.Window, "Acme · Dev Work"), "after"), "the row shows the new notes");
    }

    [Fact]
    public void StopBeforeStart_Save_ShowsErrorAndStaysOpen()
    {
        using var dir = new SeededDir();
        using var app = LaunchSeeded(dir);

        var editor = OpenEditor(app);
        SetText(FindById(editor, "EntryEditor.StopDate"), Value(FindById(editor, "EntryEditor.StartDate")));
        SetText(FindById(editor, "EntryEditor.StopTime"), "00:00");
        Invoke(FindById(editor, "EntryEditor.SaveButton"));

        WaitUntil(
            () => FindById(editor, "EntryEditor.Error").Name == "Stop must be after start.",
            "the blocking error is shown");
        Assert.NotNull(app.FindTopWindow("EntryEditor"));
        Invoke(FindById(editor, "EntryEditor.CancelButton"));
    }

    private static AppFixture LaunchSeeded(SeededDir dir)
    {
        Seed.StoppedEntry(dir.Path, "Acme", "Dev Work", TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(30), notes: "before");
        return AppFixture.Launch("entries", dir.Path);
    }

    /// <summary>Double-clicks the seeded row with the real mouse, so the window comes to the front first.</summary>
    private static Window OpenEditor(AppFixture app)
    {
        app.Window.SetForeground();
        GridRow(app.Window, "Acme · Dev Work").DoubleClick();
        return app.WaitForWindow("EntryEditor");
    }
}
