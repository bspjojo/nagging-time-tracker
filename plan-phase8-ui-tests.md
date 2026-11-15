# Phase 8 — Automated UI tests (implementation plan)

**Status:** Shipped — 1 commit. Build clean, unit tests 373/373 green, the 6 UI tests pass 3 runs in a row (about 50 s each), and `verify-phase5.ps1` still passes (`DONE failed=0`).

**Sequencing note:** this doc expands **Phase 8** of [plan-time-tracker-v1.md](plan-time-tracker-v1.md) (§12, "Phase 8 — Automated UI tests") into implementable detail. It builds on Phases 1–7, all shipped (latest commit `9a70e3e`). The parent stays the source of truth for the harness shape and the five scenarios (§10), the AutomationId convention and command-line switches (§1), and the CSV columns (§9). Phase 9 (installer, parent §11) doesn't depend on this phase, but its README step documents the unit-vs-UI test commands this phase makes real. Deviations are listed in §7 of this doc and get folded back into the parent once this doc is approved.

## Overview

Phase 8 turns the throwaway per-phase PowerShell checks (`tools/ui-checks/verify-phase*.ps1`) into a real xUnit + FlaUI test project, `tests/NaggingTimeTracker.UiTests`. It covers the five scenarios in parent §10, runs on demand on an interactive desktop, and is excluded from the normal `dotnet test --filter "Category!=UI"` run.

**What exists today (confirmed by reading the code):**

- **Switches the harness needs** are all in place: `--data-dir`, `--show quick-panel|entries|settings|export` and `--no-background` (`Core/Startup/StartupOptions.cs`; README "Build, test, run"). `--data-dir` also suffixes the single-instance names, so a test instance never collides with the user's real one.
- **No graceful exit from outside.** The app has no main window; the only `Application.Shutdown()` for a normal exit is the tray menu's *Exit* ([TrayHost.cs:248](src/NaggingTimeTracker.App/Tray/TrayHost.cs#L248)). `QuickPanelWindow.OnClosing` cancels and hides ([QuickPanelWindow.xaml.cs:71-77](src/NaggingTimeTracker.App/Windows/QuickPanelWindow.xaml.cs#L71-L77)), so `WM_CLOSE` doesn't end the process. Every existing script ends the app with `Stop-Process -Force` ([verify-phase2.ps1:164](tools/ui-checks/verify-phase2.ps1#L164)). Every tracking action writes to SQLite before it returns, so a kill loses nothing the tests check.
- **The quick panel hides itself when it loses activation**, unless a modal is open ([QuickPanelWindow.xaml.cs:48](src/NaggingTimeTracker.App/Windows/QuickPanelWindow.xaml.cs#L48), [79-89](src/NaggingTimeTracker.App/Windows/QuickPanelWindow.xaml.cs#L79-L89)). UIA `Invoke` / `ValuePattern.SetValue` don't move activation, which is why the scripts work. A test that clicks elsewhere with the real mouse would hide it.
- **Buttons that open a modal block a UIA `Invoke`** until the modal closes. The scripts work around this with `InvokeAsync`, which invokes from a background runspace ([verify-phase3.ps1:39-44](tools/ui-checks/verify-phase3.ps1#L39-L44)). It's used for Start → `ConcurrencyDialog`, Stop → `TooShortDialog`, and Export… → `SaveFileDialog`.
- **The too-short rule:** stopping an entry younger than 1 minute with an explicit single stop opens `TooShortDialog` ([TrackingService.cs:15](src/NaggingTimeTracker.Core/Services/TrackingService.cs#L15), [255](src/NaggingTimeTracker.Core/Services/TrackingService.cs#L255)). Any test that starts and then stops through the UI meets it.
- **Rows are addressable without display-text searches.** Quick panel rows have `QuickPanel.RunningRow` / `QuickPanel.RecentRow` container IDs ([QuickPanelWindow.xaml:55](src/NaggingTimeTracker.App/Windows/QuickPanelWindow.xaml#L55), [174](src/NaggingTimeTracker.App/Windows/QuickPanelWindow.xaml#L174)). Entries grid rows are `DataItem`s named by `EntryRowViewModel.AccessibleName` = `"{Date} {Start}–{running|Stop}, {Customer} · {TaskType}, {Duration}"` ([EntryRowViewModel.cs:82-83](src/NaggingTimeTracker.App/ViewModels/EntryRowViewModel.cs#L82-L83)). The ↪ marker is a `TextBlock` with text `↪` inside the row, and the column header has the same text ([EntriesWindow.xaml:124-128](src/NaggingTimeTracker.App/Windows/EntriesWindow.xaml#L124-L128)), so a marker check must be scoped to the row.
- **Texts the tests assert on:** the parallel banner is `"⚠ {n} timers running in parallel"` ([QuickPanelViewModel.cs:251](src/NaggingTimeTracker.App/ViewModels/QuickPanelViewModel.cs#L251)). The `QuickPanel.ParallelBanner` ID sits on the banner's `TextBlock`, so that element's `Name` is the text, and it leaves the UIA tree when the banner is collapsed ([QuickPanelWindow.xaml:33-38](src/NaggingTimeTracker.App/Windows/QuickPanelWindow.xaml#L33-L38)). the editor's stop-before-start error is `"Stop must be after start."` ([EntryEditorViewModel.cs:316](src/NaggingTimeTracker.App/ViewModels/EntryEditorViewModel.cs#L316)).
- **Seeding** can reuse the production wiring the unit tests use: `new ServiceCollection().AddTrackerData(dbPath)`, then `DatabaseInitializer.Initialize()` and the repositories ([TestDb.cs](tests/NaggingTimeTracker.Tests/TestDb.cs)). `AddTrackerData` registers the repositories and `DatabaseInitializer` only ([ServiceCollectionExtensions.cs:21-27](src/NaggingTimeTracker.Data/ServiceCollectionExtensions.cs#L21-L27)). `CatalogService(ICustomerRepository, ITaskTypeRepository, TimeProvider)` has `GetOrCreateCustomer` / `GetOrCreateTaskType` ([CatalogService.cs:23-29](src/NaggingTimeTracker.Core/Services/CatalogService.cs#L23-L29)).
- **The Save dialog's owner** is the Export window: `ExportWindow` sets `_vm.Owner = this` ([ExportWindow.xaml.cs:16](src/NaggingTimeTracker.App/Windows/ExportWindow.xaml.cs#L16)) and `ExportViewModel` calls `dialog.ShowDialog(Owner)` ([ExportViewModel.cs:127](src/NaggingTimeTracker.App/ViewModels/ExportViewModel.cs#L127)), so the dialog appears as a child of `ExportWindow` in the UIA tree.
- **Unit tests today:** 373, all green (`dotnet test --filter "Category!=UI"`, run while drafting this doc). Task types `Admin`, `Training`, `Dev Work`, `Meetings`, `Support` are seeded by migration ([TaskTypeConfiguration.cs:12](src/NaggingTimeTracker.Data/Configurations/TaskTypeConfiguration.cs#L12)). The DB lives at `<data-dir>\tracker.db` ([DataPaths.cs](src/NaggingTimeTracker.Data/DataPaths.cs)).
- **Save dialog:** the Phase 4 script drives `SaveFileDialog` by keyboard (Ctrl+A, type the path, Enter), noting its controls "aren't reliably exposed to this UIA client" ([verify-phase4.ps1:91-111](tools/ui-checks/verify-phase4.ps1#L91-L111)). That client is the managed `System.Windows.Automation` (UIA2 wrapper); FlaUI.UIA3 uses the COM UIA3 API, which normally sees the common dialog's `1001` (File name) edit and `1` (Save) button.
- **Test project conventions:** `tests/NaggingTimeTracker.Tests` pins `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.5, `Microsoft.NET.Test.Sdk` 18.12.0, and adds `<Using Include="Xunit" />` ([NaggingTimeTracker.Tests.csproj](tests/NaggingTimeTracker.Tests/NaggingTimeTracker.Tests.csproj)). `Directory.Build.props` sets warnings as errors.

**Decisions already made (givens), from the parent §10:** FlaUI.UIA3; `[Trait("Category", "UI")]` on every class; the app is launched with `--data-dir <temp> --no-background --show <window>`; a 10 s wait for the first window, a 5 s `Retry.WhileNull` on finds; the tray icon and timing-driven nags/idle prompts are not UI-tested; seed data goes through the UI or through `TrackerDbContext` (the project references Data).

**Choices made in this doc** (no stakeholder input needed; listed in §7 as deviations where they differ from the parent):

- The fixture **kills** the app on dispose instead of "closes, then kills after 5 s", because no outside close path exists.
- Tests run **one at a time** (xUnit parallelization off); they share one desktop.
- The `tools/ui-checks` scripts **stay** as reference material and for the checks this suite doesn't cover (nags, idle, backups, the Settings tabs).

**Commit granularity:** one commit, "Phase 8 — automated UI tests", per the parent's one-commit-per-phase rule. The AutomationId fixes (§3) are tiny and go in the same commit.

---

## 1. Project: `tests/NaggingTimeTracker.UiTests/NaggingTimeTracker.UiTests.csproj`

- `net10.0-windows`, `IsPackable=false`, `<Using Include="Xunit" />`.
- Packages: `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.5, `Microsoft.NET.Test.Sdk` 18.12.0 (the same pins as the unit-test project), and `FlaUI.UIA3` at the latest stable version at implementation time (5.x), pinned.
- `ProjectReference` to `src/NaggingTimeTracker.App` with `ReferenceOutputAssembly="false"`, so building the test project builds the App first. If the SDK rejects referencing an exe from a test project (NETSDK1151, self-contained mismatch), add `<ValidateExecutableReferencesMatchSelfContained>false</ValidateExecutableReferencesMatchSelfContained>` to the test csproj, and only that.
- `ProjectReference` to `src/NaggingTimeTracker.Data`, for seeding (brings Core along).
- **The exe path is baked in at build time**, not worked out from `..\..\..` hops at runtime:
  ```xml
  <ItemGroup>
    <AssemblyAttribute Include="System.Reflection.AssemblyMetadataAttribute">
      <_Parameter1>AppExe</_Parameter1>
      <_Parameter2>$(MSBuildThisFileDirectory)..\..\src\NaggingTimeTracker.App\bin\$(Configuration)\net10.0-windows\NaggingTimeTracker.exe</_Parameter2>
    </AssemblyAttribute>
  </ItemGroup>
  ```
  So a Debug test build drives the Debug exe and a Release test build drives the Release exe.
- `AssemblyInfo.cs`: `[assembly: CollectionBehavior(DisableTestParallelization = true)]`.
- Add the project to `NaggingTimeTracker.sln` under the existing `tests` solution folder (`dotnet sln add tests/NaggingTimeTracker.UiTests --solution-folder tests`).

---

## 2. Harness

### 2.1 `AppFixture.cs`

`public sealed class AppFixture : IDisposable`, created per test with `using var app = AppFixture.Launch(ShowTarget, dataDir: null)`. It's not an xUnit class fixture, because `QuickPanelTests` needs two launches in one test.

- **`static AppFixture Launch(string show, string? dataDir = null)`**:
  1. If `dataDir` is null, create `%TEMP%\ntt-ui-tests\<guid N>` and remember that this fixture owns it (`_ownsDataDir = true`).
  2. Read the `AppExe` assembly metadata (§1). If the file is missing, throw `InvalidOperationException("App exe not found at <path>; build the solution first.")`.
  3. `Application.Launch(new ProcessStartInfo(exe) { ArgumentList = { "--data-dir", dataDir, "--no-background", "--show", show } })`.
  4. `Automation = new UIA3Automation()`.
  5. Wait for the target window with `Retry.WhileNull(() => FindTopWindow(rootId), TimeSpan.FromSeconds(10))`. If it never appears, dispose and throw with the process's exit code (if it exited). Root IDs by `show`: `quick-panel` → `QuickPanel`, `entries` → `EntriesWindow`, `settings` → `SettingsWindow`, `export` → `ExportWindow`.
- **Properties:** `DataDir`, `DbPath` (`<DataDir>\tracker.db`), `Window` (the root `Window` found in step 5), `Automation`, `ProcessId`.
- **`Window FindTopWindow(string automationId)`**: searches the desktop's children filtered to this process (`ConditionFactory.ByProcessId`) for the ID, then those windows' children (owned dialogs sit under their owner in the UIA tree, as in the scripts' `TopWindow`). Returns null if not found; callers wrap it in `Retry.WhileNull`.
- **`Dispose()`**: `Application.Kill()`, wait up to 5 s for exit, dispose `Automation`, then `SqliteConnection.ClearAllPools()` and, if `_ownsDataDir`, delete the folder recursively (best effort: catch `IOException` / `UnauthorizedAccessException`, as `TestDb` does).
- **`Relaunch(string show)`**: kills this process and launches again on the same data dir, without deleting it. `QuickPanelTests` uses this. It replaces the parent's "dispose, then construct with an existing dir" two-step.

### 2.2 `Ui.cs` (static helpers)

- `T FindById<T>(AutomationElement parent, string id, int timeoutMs = 5000)` (and an `AutomationElement` overload): `Retry.WhileNull(() => parent.FindFirstDescendant(cf => cf.ByAutomationId(id)), TimeSpan.FromMilliseconds(timeoutMs))`, then `.Result` converted with `.As<T>()` (`Button`, `TextBox`, `ComboBox`, …). It fails with `Xunit.Sdk.XunitException("No element '<id>' under '<parent AutomationId>' within <n> ms")` instead of returning null.
- `AutomationElement[] FindAllById(AutomationElement parent, string id, int expectedAtLeast = 1, int timeoutMs = 5000)`: retries until at least `expectedAtLeast` are found. With `expectedAtLeast: 0` it returns immediately.
- `void WaitUntil(Func<bool> condition, string because, int timeoutMs = 5000)`: polls every 100 ms and throws `XunitException(because)` on timeout. Every "after X, the UI shows Y" assertion goes through this instead of a fixed `Thread.Sleep`.
- `void SetText(AutomationElement el, string text)`: uses `ValuePattern.SetValue` on the element if it supports it, otherwise on its first `Edit` descendant (an editable `ComboBox`). Same rule as the scripts' `SetText`.
- `Task InvokeAsync(AutomationElement el)`: `Task.Run(() => el.Patterns.Invoke.Pattern.Invoke())`, for buttons that open a modal. The test awaits the task after closing the modal, so an exception inside isn't lost.
- `AutomationElement GridRow(Window entries, string nameContains)`: waits for a `DataItem` child of `EntriesWindow.Grid` whose `Name` contains the text.

### 2.3 `Seed.cs`

Seeding happens **before** `Launch`, while no app has the DB open.

- **`sealed class SeededDir : IDisposable`**: the constructor creates `%TEMP%\ntt-ui-tests\<guid N>` and exposes it as `Path`; `Dispose()` calls `SqliteConnection.ClearAllPools()` and deletes the folder recursively (best effort, same catches as `AppFixture`). A test that seeds does `using var dir = new SeededDir();`, seeds, then `using var app = AppFixture.Launch(show, dir.Path);`. `using` disposes in reverse order, so the app is killed before the folder is deleted. `AppFixture` doesn't own a dir passed in, so it doesn't delete it.
- **`static long Seed.StoppedEntry(string dataDir, string customer, string taskType, TimeSpan startAgo, TimeSpan stopAgo, string notes = "")`**:
  1. `using var sp = new ServiceCollection().AddTrackerData(Path.Combine(dataDir, "tracker.db")).AddSingleton(TimeProvider.System).AddSingleton<CatalogService>().BuildServiceProvider();`
  2. `sp.GetRequiredService<DatabaseInitializer>().Initialize();`
  3. `var catalog = sp.GetRequiredService<CatalogService>();` then `catalog.GetOrCreateCustomer(customer)` and `catalog.GetOrCreateTaskType(taskType)`.
  4. `now = DateTimeOffset.UtcNow` truncated to the second; `IEntryRepository.Insert(new Entry { CustomerId, TaskTypeId, StartUtc = now - startAgo, StopUtc = now - stopAgo, Notes = notes, CreatedUtc = now, UpdatedUtc = now })`. Return its `Id`.
  5. After the provider is disposed, `SqliteConnection.ClearAllPools()`, so the app can open the file.
- Times are relative to now, so the seeded entry is inside the default *This week* filter and not in the future (`TrackingService.Save` rejects future times, parent §3). The one window where this fails is Monday between 00:00 and the `startAgo` offset (the entry falls into last week); §4 uses offsets of 90/30 minutes, and §5 documents that caveat.

---

## 3. AutomationId audit

Every `Button`, `TextBox`, `ComboBox`, `CheckBox`, `DatePicker`, `Hyperlink`, `DataGrid`, `ListBox` and `TabItem` in `Windows/*.xaml` and `Controls/*.xaml` was scanned for `AutomationProperties.AutomationId` (or a container style setter). All follow the `<Window>.<Control>` convention, except these, which get fixed:

| Where | Today | Fix |
|---|---|---|
| [EntriesWindow.xaml:96](src/NaggingTimeTracker.App/Windows/EntriesWindow.xaml#L96) — the Link cell's `Hyperlink` | no ID | `AutomationProperties.AutomationId="EntriesWindow.Grid.Link"` |
| [SettingsWindow.xaml:182](src/NaggingTimeTracker.App/Windows/SettingsWindow.xaml#L182) — the reminders grid's *On* `CheckBox` | `AutomationProperties.Name="Enabled"` only | add `AutomationProperties.AutomationId="SettingsWindow.ReminderEnabled"` (keep the `Name`) |
| [NagWindow.xaml:111](src/NaggingTimeTracker.App/Windows/NagWindow.xaml#L111) — the scheduled reminder's *Snooze 15 min* | `NagWindow.Snooze15Button`, the same ID as the not-tracking/check-in button at [line 99](src/NaggingTimeTracker.App/Windows/NagWindow.xaml#L99) | rename to `NagWindow.ScheduledSnoozeButton`. Only one panel is visible at a time, so nothing broke, but IDs should be unique per window. `verify-phase5.ps1:147` uses the line-99 button (a not-tracking nag), so no script changes. |

Bound IDs (`FilterBar`'s `{AutomationPrefix}.PresetCombo` etc., the Settings Customers/Task types lists) and container-style IDs (`QuickPanel.RunningRow`, `QuickPanel.RecentRow`, `NagWindow.CheckInRow`) already follow the convention. Grid rows are found by `Name` (§2.2), which is the accessible name, not display-text scraping.

---

## 4. Scenarios

One class per file, each `[Trait("Category", "UI")]`. Each test launches its own `AppFixture` (fresh data dir unless stated otherwise). Assertions after an action go through `Ui.WaitUntil`.

### 4.1 `QuickPanelTests.cs` — `Start_NewCustomerAndType_RunsAndSurvivesRestart`

1. `Launch("quick-panel")`. `QuickPanel.NothingRunning` is visible.
2. `SetText(QuickPanel.CustomerCombo, "Acme")`, `SetText(QuickPanel.TaskTypeCombo, "Research")`, invoke `QuickPanel.StartButton` (no modal on a fresh DB, so a plain `Invoke`).
3. Wait for exactly one `QuickPanel.RunningRow`; its `QuickPanel.RunningRow.Label` name is `"Acme · Research"`.
4. `app.Relaunch("entries")`.
5. `GridRow(Window, "running, Acme · Research")` is found.

### 4.2 `ConcurrencyTests.cs` — `SecondTimer_RunInParallel_ThenKeepOnlyThis`

1. `Launch("quick-panel")`. Start `Acme` · `Dev Work` as in 4.1.
2. Set `Globex` · `Meetings`; `InvokeAsync(QuickPanel.StartButton)`.
3. `ConcurrencyDialog` appears (`FindTopWindow`, 5 s). `SetText(ConcurrencyDialog.ReasonBox, "pairing")`; invoke `ConcurrencyDialog.RunInParallelButton`; await the start task.
4. Two `QuickPanel.RunningRow`s; `QuickPanel.ParallelBanner` is found, not offscreen, and its `Name` is exactly `"⚠ 2 timers running in parallel"`.
5. Invoke the first row's `QuickPanel.RunningRow.KeepOnlyButton`. Exactly one `QuickPanel.RunningRow` remains, and `WaitUntil` sees `QuickPanel.ParallelBanner` absent from the tree (or offscreen). (A bulk stop never shows the too-short prompt, parent Non-goals.)

### 4.3 `EntriesEditorTests.cs`

Both tests seed first (§2.3): `using var dir = new SeededDir();`, `Seed.StoppedEntry(dir.Path, "Acme", "Dev Work", startAgo: 90 min, stopAgo: 30 min, notes: "before")`, then `using var app = AppFixture.Launch("entries", dir.Path);`.

- **`EditNotes_Save_GridShowsNewNotes`:** `app.Window.SetForeground()`, `GridRow(…, "Acme · Dev Work").DoubleClick()`; `EntryEditor` appears; `SetText(EntryEditor.NotesBox, "after")`; invoke `EntryEditor.SaveButton`; the editor closes; the row's descendants include a text element named `"after"` (the Notes column's first line).
- **`StopBeforeStart_Save_ShowsErrorAndStaysOpen`:** open the editor the same way; read `EntryEditor.StartDate`'s value into `EntryEditor.StopDate`; `SetText(EntryEditor.StopTime, "00:00")`; invoke Save. `EntryEditor.Error`'s name is `"Stop must be after start."` and `EntryEditor` is still open. Close with `EntryEditor.CancelButton`.

Double-click uses the real mouse (`FlaUI.Core.Input.Mouse`), so the test brings the window to the front with `SetForeground()` first. It's the only mouse action in the suite; it is kept because double-click is the behaviour under test.

### 4.4 `ContinueTests.cs` — `StopThenContinueFromRecent_NewRunningRowMarkedContinued`

1. `Launch("quick-panel")`. Start `Acme` · `Dev Work`.
2. `InvokeAsync(QuickPanel.RunningRow.StopButton)`. `TooShortDialog` appears; invoke `TooShortDialog.KeepButton`; await.
3. One `QuickPanel.RecentRow`; invoke its `QuickPanel.RecentRow.ContinueButton` (nothing is running, so no concurrency dialog).
4. One `QuickPanel.RunningRow` labelled `"Acme · Dev Work"`.
5. Invoke `QuickPanel.AllEntriesLink`; wait for `EntriesWindow` (`FindTopWindow`).
6. `GridRow(…, "running, Acme · Dev Work")` has a descendant `Text` named `"↪"`. The search is scoped to that row, so the header's `↪` doesn't count.

### 4.5 `ExportTests.cs` — `Export_ToTypedPath_WritesFileWithHeader`

1. Seed as in 4.3 (`Acme` · `Dev Work`, 90/30 min ago). `using var app = AppFixture.Launch("export", dir.Path);`.
2. `target = Path.Combine(dir.Path, "out.csv")`. `InvokeAsync(ExportWindow.ExportButton)`.
3. Find the Save dialog: a `Window`-type child of `ExportWindow` (`Retry.WhileNull`, 8 s).
4. **Primary:** find the edit with AutomationId `1001` under the dialog (5 s), `SetText(edit, target)`, then invoke the button with AutomationId `1` (Save). **Fallback**, only if `1001` isn't found: `dialog.Focus()`, `Keyboard.TypeSimultaneously(CONTROL, KEY_A)`, `Keyboard.Type(target)`, `Keyboard.Type(VirtualKeyShort.ENTER)`, as the Phase 4 script does. Await the export task.
5. `WaitUntil(() => File.Exists(target))`, and `ExportWindow.Result` is visible.
6. The file's first 3 bytes are `EF BB BF` (UTF-8 BOM, parent §9). Its first line, read with `File.ReadLines(target, Encoding.UTF8)`, is exactly `entry_id,date,start,stop,duration_hours,duration_minutes,customer,task_type,external_link,notes,continued_from_id,overlap_reason`, a literal copied from parent §9 rather than `CsvExporter.Columns`, so the test checks the spec instead of re-reading the code. There are exactly 2 lines (header plus the seeded entry).

---

## 5. Running and verification

- Unit tests: `dotnet test --filter "Category!=UI"`. This still builds UiTests (and so the App), and runs zero tests from it.
- UI tests: `dotnet test tests/NaggingTimeTracker.UiTests` on an interactive, unlocked desktop. **Don't touch the mouse or keyboard during the run.** A click elsewhere hides the quick panel (§Overview), and the export fallback types keys. Expected time is under a minute (6 tests, about 5–12 s each).
- Known caveat: tests that seed (4.3, 4.5) fail if run on a Monday between 00:00 and 01:30 local, because the seeded entry falls outside *This week*.
- **Flakiness bar:** the UI suite passes 3 runs in a row before the commit, recorded in the commit message.
- A test that fails leaves no app running (the fixture kills it in `Dispose`). If the test host itself is killed mid-run, stray `NaggingTimeTracker` processes from the build folder may remain; the README says to end them from Task Manager. They run on a temp `--data-dir`, so they never touch real data.

### Implementation notes

- **No NETSDK1151 workaround was needed.** The exe `ProjectReference` with `ReferenceOutputAssembly=false` builds cleanly.
- **FlaUI.UIA3 5.0.0.** One `UIA3Automation` is shared by the assembly (`AppFixture.Automation`, static), not created per fixture. Tests run one at a time, and one automation object avoids re-initialising COM UIA for every launch.
- **The Save dialog took the primary path** (IDs `1001` / `1` through UIA3) in all three runs; the keyboard fallback never ran.
- **`Seed` truncates `now` to the second**, so the seeded times survive the round trip.
- **Before the fix in §7.9**, `QuickPanelTests` failed once in a full run (*"Window 'QuickPanel' didn't appear within 10 s"*) and passed 3/3 on its own. That's how the order-dependent hide was found.

---

## 6. Docs

- [README.md](README.md): the status line (Phases 1–8); under "Build, test, run", replace "The automated UI tests that arrive in Phase 8 need an interactive desktop…" with the two commands from §5, the don't-touch-the-desktop note, and the stray-process note.
- [tools/ui-checks/README.md](tools/ui-checks/README.md): replace "built in Phase 8; these scripts are reference material for it" with: the suite now exists; these scripts stay for what it doesn't cover (nags, idle, backups, Settings tabs, single instance, filters, overwrite/locked-file export).
- In [plan-time-tracker-v1.md](plan-time-tracker-v1.md) §9, the CSV-injection paragraph currently splits the columns table between `notes` and `continued_from_id`. Move it below the table while folding in §7 (a doc fix only; the Phase 8 test reads that table).

---

## 7. Deviations from the parent (fold into [plan-time-tracker-v1.md](plan-time-tracker-v1.md))

1. **The fixture kills the app instead of closing it** (§10 step 4): there's no outside close path (§Overview), and every write the tests check is already committed.
2. **`AppFixture.Launch(show, dataDir)` + `Relaunch(show)`**, a per-test object, instead of a constructor with an optional existing dir (§10 harness, `QuickPanelTests` row). Seeding helpers live in `Seed.cs` / `SeededDir`.
3. **The exe path comes from build-time assembly metadata**, following `$(Configuration)` (§10 step 2).
4. **Test parallelization is off** for the UiTests assembly (§10 doesn't say).
5. **`ContinueTests` stops through the UI and answers the too-short prompt with *Keep***, and opens Entries through `QuickPanel.AllEntriesLink` in the same instance (§10 row doesn't say how).
6. **The export test drives the Save dialog through UIA3 IDs `1001` / `1`**, with the keyboard as a fallback, and also checks the BOM (§10 row says "type … press Enter").
7. **AutomationId fixes:** `EntriesWindow.Grid.Link`, `SettingsWindow.ReminderEnabled`, and `NagWindow.ScheduledSnoozeButton` (renamed from a duplicate `Snooze15Button`) (§1 convention).
8. **The `tools/ui-checks` scripts are kept** alongside the suite (§1 tools list doesn't mention them).
9. **The fixture re-signals a quick panel that didn't show** (found while implementing; §10 step 3). In a full run, the quick panel of a freshly launched copy sometimes hid itself straight away. This happened right after the previous test's app was killed, presumably because activation moved on as that app died. If `QuickPanel` hasn't appeared after 5 s, `AppFixture` launches the exe once more on the same data dir; the single-instance show signal (`TrayHost.ShowQuickPanel`) shows it, and the fixture waits 5 s more. It logs a line when it does this. It happened once in the first full run and not in the next two.

---

## Steps

- [x] AutomationId fixes in [EntriesWindow.xaml:96](src/NaggingTimeTracker.App/Windows/EntriesWindow.xaml#L96), [SettingsWindow.xaml:182](src/NaggingTimeTracker.App/Windows/SettingsWindow.xaml#L182), [NagWindow.xaml:111](src/NaggingTimeTracker.App/Windows/NagWindow.xaml#L111) (§3)
- [x] Create `tests/NaggingTimeTracker.UiTests/NaggingTimeTracker.UiTests.csproj` and `AssemblyInfo.cs` (§1); `dotnet sln add … --solution-folder tests`
- [x] `tests/NaggingTimeTracker.UiTests/AppFixture.cs` (§2.1), `Ui.cs` (§2.2), `Seed.cs` with `Seed.StoppedEntry` and `SeededDir` (§2.3)
- [x] `QuickPanelTests.cs` (§4.1), `ConcurrencyTests.cs` (§4.2), `EntriesEditorTests.cs` (§4.3), `ContinueTests.cs` (§4.4), `ExportTests.cs` (§4.5)
- [x] `dotnet build` (zero warnings) and `dotnet test --filter "Category!=UI"` (all green, unchanged at 373; this phase adds no unit tests)
- [x] `dotnet test tests/NaggingTimeTracker.UiTests` three times in a row, all 6 green; note in the commit message which Save-dialog path (§4.5 primary or fallback) ran
- [x] Re-run [verify-phase5.ps1](tools/ui-checks/verify-phase5.ps1) (it touches `NagWindow`); `DONE failed=0`
- [x] Update [README.md](README.md) and [tools/ui-checks/README.md](tools/ui-checks/README.md) (§6)
- [x] Tick the parent's Phase 8 checklist in [plan-time-tracker-v1.md](plan-time-tracker-v1.md), fold in the §7 deviations (§1, §10) and the §9 table fix (§6), update its Status line, and flip this doc's Status to Shipped
- [x] Commit "Phase 8 — automated UI tests"

---

## Non-goals

- **No UI tests of the tray icon, nags or idle prompts** (parent §10, Non-goals). They stay unit-tested, and the Phase 5/6 scripts cover them by hand.
- **No CI job.** The suite needs an interactive desktop (parent §10); it's run on demand.
- **No porting of every script check.** The suite is the five scenarios in parent §10. Filters, archive, overwrite/locked export, single instance, Settings tabs and backups stay in `tools/ui-checks`.
- **No deleting `tools/ui-checks`** (§7.8).
- **No screenshots on failure.** The failure message names the missing element and its parent; screenshots can come later if failures turn out to be hard to read.
- **No graceful-exit switch for tests** (e.g. `--exit` signalled through the single-instance event). Kill is enough today (§7.1); a graceful path would be new app behaviour that only tests need.
- **No test-only hooks in the App.** The tests drive only the shipped switches and AutomationIds.
