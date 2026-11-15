using System.Diagnostics;
using System.Reflection;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using Microsoft.Data.Sqlite;

namespace NaggingTimeTracker.UiTests;

/// <summary>
/// One running copy of the built App on its own temp <c>--data-dir</c>, launched with
/// <c>--no-background</c> and a <c>--show</c> window (plan-time-tracker-v1.md §10).
/// Dispose kills it: the app is tray-only and has no close path from outside.
/// </summary>
public sealed class AppFixture : IDisposable
{
    private static readonly Dictionary<string, string> RootIds = new()
    {
        ["quick-panel"] = "QuickPanel",
        ["entries"] = "EntriesWindow",
        ["settings"] = "SettingsWindow",
        ["export"] = "ExportWindow",
    };

    private readonly bool _ownsDataDir;
    private Application _app;

    private AppFixture(Application app, string dataDir, bool ownsDataDir, Window window)
    {
        _app = app;
        DataDir = dataDir;
        _ownsDataDir = ownsDataDir;
        Window = window;
    }

    public static UIA3Automation Automation { get; } = new();

    public string DataDir { get; }

    public string DbPath => Path.Combine(DataDir, "tracker.db");

    /// <summary>The window named by the last <c>--show</c>.</summary>
    public Window Window { get; private set; }

    public int ProcessId => _app.ProcessId;

    /// <summary>
    /// Launches the app. With no <paramref name="dataDir"/> a temp folder is created and deleted
    /// on dispose; a folder passed in is left for its owner to delete.
    /// </summary>
    public static AppFixture Launch(string show, string? dataDir = null)
    {
        var owns = dataDir is null;
        dataDir ??= NewTempDir();
        var app = Start(show, dataDir);
        try
        {
            var window = WaitForRoot(app, show, dataDir);
            return new AppFixture(app, dataDir, owns, window);
        }
        catch
        {
            Kill(app);
            if (owns)
            {
                DeleteDir(dataDir);
            }

            throw;
        }
    }

    /// <summary>Kills this copy and starts another on the same data dir.</summary>
    public void Relaunch(string show)
    {
        Kill(_app);
        _app = Start(show, DataDir);
        Window = WaitForRoot(_app, show, DataDir);
    }

    /// <summary>
    /// A window of this process by AutomationId: a top-level one, or one owned by a top-level
    /// window (owned dialogs sit under their owner in the UIA tree). Null if not found.
    /// </summary>
    public Window? FindTopWindow(string automationId) => FindTopWindow(_app.ProcessId, automationId);

    /// <summary>Waits up to <paramref name="timeoutMs"/> for <see cref="FindTopWindow(string)"/>.</summary>
    public Window WaitForWindow(string automationId, int timeoutMs = 5000) =>
        Retry.WhileNull(() => FindTopWindow(automationId), TimeSpan.FromMilliseconds(timeoutMs), ignoreException: true).Result
        ?? throw new Xunit.Sdk.XunitException($"Window '{automationId}' didn't appear within {timeoutMs} ms");

    public void Dispose()
    {
        Kill(_app);
        if (_ownsDataDir)
        {
            DeleteDir(DataDir);
        }
    }

    internal static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ntt-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    internal static void DeleteDir(string dir)
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort; %TEMP% is cleaned eventually.
        }
    }

    private static string ExePath()
    {
        var path = typeof(AppFixture).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "AppExe").Value!;
        path = Path.GetFullPath(path);
        return File.Exists(path)
            ? path
            : throw new InvalidOperationException($"App exe not found at {path}; build the solution first.");
    }

    private static Application Start(string show, string dataDir)
    {
        var psi = new ProcessStartInfo(ExePath())
        {
            ArgumentList = { "--data-dir", dataDir, "--no-background", "--show", show },
        };
        return Application.Launch(psi);
    }

    private static Window WaitForRoot(Application app, string show, string dataDir)
    {
        var id = RootIds[show];
        Window? Wait(int seconds) => Retry.WhileNull(
            () => app.HasExited ? null : FindTopWindow(app.ProcessId, id),
            TimeSpan.FromSeconds(seconds),
            ignoreException: true).Result;

        var window = Wait(show == "quick-panel" ? 5 : 10);
        if (window is null && show == "quick-panel" && !app.HasExited)
        {
            // The panel hides itself when it loses activation, which can happen right after the
            // previous test's app is killed and focus moves on. A second launch on the same data
            // dir signals the running copy to show it again (single instance), then exits.
            Console.WriteLine("QuickPanel not shown after 5 s; signalling the running copy to show it.");
            using (var signal = Process.Start(new ProcessStartInfo(ExePath()) { ArgumentList = { "--data-dir", dataDir } })!)
            {
                signal.WaitForExit(10_000);
            }

            window = Wait(5);
        }

        if (window is not null)
        {
            return window;
        }

        var exited = app.HasExited ? $" (the app exited with code {app.ExitCode})" : "";
        throw new Xunit.Sdk.XunitException($"Window '{id}' didn't appear within 10 s{exited}");
    }

    private static Window? FindTopWindow(int processId, string automationId)
    {
        var tops = Automation.GetDesktop().FindAllChildren(cf => cf.ByProcessId(processId));
        foreach (var top in tops)
        {
            if (top.AutomationId == automationId)
            {
                return top.AsWindow();
            }
        }

        foreach (var top in tops)
        {
            var owned = top.FindFirstChild(cf => cf.ByAutomationId(automationId));
            if (owned is not null)
            {
                return owned.AsWindow();
            }
        }

        return null;
    }

    private static void Kill(Application app)
    {
        try
        {
            if (!app.HasExited)
            {
                app.Kill();
            }

            using var process = Process.GetProcessById(app.ProcessId);
            process.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        finally
        {
            app.Dispose();
        }
    }
}
