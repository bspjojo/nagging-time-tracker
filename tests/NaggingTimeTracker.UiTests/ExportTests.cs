using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using static NaggingTimeTracker.UiTests.Ui;

namespace NaggingTimeTracker.UiTests;

[Trait("Category", "UI")]
public sealed class ExportTests
{
    // Copied from plan-time-tracker-v1.md §9, not from CsvExporter, so the test checks the spec.
    private const string Header =
        "entry_id,date,start,stop,duration_hours,duration_minutes,customer,task_type,external_link,notes,continued_from_id,overlap_reason";

    [Fact]
    public async Task Export_ToTypedPath_WritesFileWithHeader()
    {
        using var dir = new SeededDir();
        Seed.StoppedEntry(dir.Path, "Acme", "Dev Work", TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(30));
        using var app = AppFixture.Launch("export", dir.Path);
        var target = Path.Combine(dir.Path, "out.csv");

        var export = InvokeAsync(FindById(app.Window, "ExportWindow.ExportButton"));
        var dialog = Retry.WhileNull(
                () => app.Window.FindFirstChild(cf => cf.ByControlType(ControlType.Window)),
                TimeSpan.FromSeconds(8),
                ignoreException: true).Result
            ?? throw new Xunit.Sdk.XunitException("The Save dialog didn't appear");
        SaveAs(dialog, target);
        await export;

        WaitUntil(() => File.Exists(target), $"{target} is written");
        WaitUntil(() => IsShown(TryFindById(app.Window, "ExportWindow.Result")), "the result line is shown");

        var bytes = File.ReadAllBytes(target);
        Assert.True(
            bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "the file starts with a UTF-8 BOM");
        var lines = File.ReadAllLines(target, Encoding.UTF8);
        Assert.Equal(Header, lines[0]);
        Assert.Equal(2, lines.Length);
    }

    /// <summary>The File name edit (1001) and Save (1) through UIA3; the keyboard if they aren't exposed.</summary>
    private static void SaveAs(AutomationElement dialog, string path)
    {
        var edit = Retry.WhileNull(
            () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("1001").And(cf.ByControlType(ControlType.Edit))),
            TimeSpan.FromSeconds(5),
            ignoreException: true).Result;
        var save = dialog.FindFirstChild(cf => cf.ByAutomationId("1").And(cf.ByControlType(ControlType.Button)));
        if (edit is not null && save is not null)
        {
            SetText(edit, path);
            Invoke(save);
            return;
        }

        Console.WriteLine("Save dialog: IDs 1001/1 not exposed; using the keyboard.");
        dialog.SetForeground();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(path);
        Keyboard.Type(VirtualKeyShort.ENTER);
    }
}
