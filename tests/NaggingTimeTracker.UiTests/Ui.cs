using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using Xunit.Sdk;

namespace NaggingTimeTracker.UiTests;

/// <summary>Find / wait / act helpers. Every find waits, and fails with a message naming what was missing.</summary>
public static class Ui
{
    public static AutomationElement FindById(AutomationElement parent, string id, int timeoutMs = 5000) =>
        Retry.WhileNull(
            () => parent.FindFirstDescendant(cf => cf.ByAutomationId(id)),
            TimeSpan.FromMilliseconds(timeoutMs),
            ignoreException: true).Result
        ?? throw new XunitException($"No element '{id}' under '{SafeId(parent)}' within {timeoutMs} ms");

    /// <summary>Null instead of failing; for "it's gone" checks.</summary>
    public static AutomationElement? TryFindById(AutomationElement parent, string id)
    {
        try
        {
            return parent.FindFirstDescendant(cf => cf.ByAutomationId(id));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Waits until at least <paramref name="expectedAtLeast"/> match (0 returns at once).</summary>
    public static AutomationElement[] FindAllById(AutomationElement parent, string id, int expectedAtLeast = 1, int timeoutMs = 5000)
    {
        AutomationElement[] found = [];
        var ok = WaitFor(
            () =>
            {
                found = parent.FindAllDescendants(cf => cf.ByAutomationId(id));
                return found.Length >= expectedAtLeast;
            },
            timeoutMs);
        return ok
            ? found
            : throw new XunitException($"Expected at least {expectedAtLeast} '{id}' under '{SafeId(parent)}', found {found.Length}");
    }

    public static void WaitUntil(Func<bool> condition, string because, int timeoutMs = 5000)
    {
        if (!WaitFor(condition, timeoutMs))
        {
            throw new XunitException($"Timed out after {timeoutMs} ms: {because}");
        }
    }

    /// <summary>Sets a value through ValuePattern, on the element or its first Edit (an editable ComboBox).</summary>
    public static void SetText(AutomationElement el, string text)
    {
        var target = el.Patterns.Value.IsSupported
            ? el
            : el.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit))
              ?? throw new XunitException($"'{SafeId(el)}' has no value pattern and no Edit child");
        target.Patterns.Value.Pattern.SetValue(text);
    }

    public static string Value(AutomationElement el) => el.Patterns.Value.Pattern.Value.Value;

    public static void Invoke(AutomationElement el) => el.Patterns.Invoke.Pattern.Invoke();

    /// <summary>
    /// For buttons that open a modal: Invoke blocks until the modal closes, so it runs in the
    /// background. Await the task after closing the modal.
    /// </summary>
    public static Task InvokeAsync(AutomationElement el) => Task.Run(() => el.Patterns.Invoke.Pattern.Invoke());

    /// <summary>The Entries grid row whose accessible name contains <paramref name="nameContains"/>.</summary>
    public static AutomationElement GridRow(AutomationElement entriesWindow, string nameContains, int timeoutMs = 5000)
    {
        var grid = FindById(entriesWindow, "EntriesWindow.Grid");
        return Retry.WhileNull(
                () => grid.FindAllChildren(cf => cf.ByControlType(ControlType.DataItem))
                    .FirstOrDefault(r => r.Name.Contains(nameContains, StringComparison.Ordinal)),
                TimeSpan.FromMilliseconds(timeoutMs),
                ignoreException: true).Result
            ?? throw new XunitException(
                $"No grid row containing '{nameContains}' within {timeoutMs} ms; rows: " +
                string.Join(" | ", grid.FindAllChildren(cf => cf.ByControlType(ControlType.DataItem)).Select(r => r.Name)));
    }

    /// <summary>True if a descendant has exactly this name.</summary>
    public static bool HasText(AutomationElement parent, string name) =>
        parent.FindFirstDescendant(cf => cf.ByName(name)) is not null;

    public static bool IsShown(AutomationElement? el)
    {
        try
        {
            return el is not null && !el.IsOffscreen;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            try
            {
                if (condition())
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Elements come and go while the UI updates; try again.
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            Thread.Sleep(100);
        }
    }

    private static string SafeId(AutomationElement el)
    {
        try
        {
            return el.AutomationId;
        }
        catch (Exception)
        {
            return "?";
        }
    }
}
