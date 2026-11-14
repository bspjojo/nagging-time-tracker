# Phase 4 — CSV Export (implementation plan)

**Status:** Shipped — 1 commit. Build clean, tests 242/242 green, the Phase 3 UI script still passes (34/34), the 27 Phase 4 UI checks of §5.2 pass, and the tray-menu item was checked by hand.

**Sequencing note:** this doc expands **Phase 4** of [plan-time-tracker-v1.md](plan-time-tracker-v1.md) (§12, "Phase 4 — CSV export") into implementable detail. It builds on Phase 3 ([plan-phase3-entries-editor.md](plan-phase3-entries-editor.md)), which has shipped (commit `fccaccc`), and reuses its filter logic and `WindowManager`. The parent plan stays the source of truth for the file format (§9) and the export UI test (§10, `ExportTests.cs`). Deviations are listed in §6 here and get folded back into the parent once this doc is approved.

## Overview

Phase 4 adds the reporting path:

- **`CsvExporter`** in Core writes the §9 file. It's UTF-8 with BOM, comma-delimited and RFC 4180-quoted, with fixed local-time formats.
- **The Export window** has the same filters as the Entries window, plus *Include running entries*, and **Export…** opens a save dialog.
- **Entry points:** an *Export…* button in the Entries window pre-fills the Export window with the on-screen filters. The tray menu and quick panel get *Export CSV…* / *Export…*, and `--show export` opens it directly.

To avoid writing the filter bar twice, the Entries window's filter state is pulled out into a shared view-model, a user control and a small pure Core type. This is a refactor of shipped Phase 3 code, covered by its existing UI checks (§5).

**What exists today (confirmed by reading the code):**

- **CsvHelper 34.0.0** is already referenced by Core ([NaggingTimeTracker.Core.csproj:8](src/NaggingTimeTracker.Core/NaggingTimeTracker.Core.csproj#L8)) and unused so far.
- **`IEntryRepository.QueryViews(EntryFilter)`** returns `EntryView`s ordered by start, archived names included ([Repositories.cs](src/NaggingTimeTracker.Core/Abstractions/Repositories.cs)). That's exactly the export's input.
- **The filter state is inline in `EntriesViewModel`:**
  - the presets list ([EntriesViewModel.cs:64](src/NaggingTimeTracker.App/ViewModels/EntriesViewModel.cs#L64));
  - the preset, dates, customer/task-type options and search fields ([:76-100](src/NaggingTimeTracker.App/ViewModels/EntriesViewModel.cs#L76-L100));
  - the date validation and `EntryFilter` construction inside `Reload()` ([:120-147](src/NaggingTimeTracker.App/ViewModels/EntriesViewModel.cs#L120-L147));
  - the change handlers ([:179-205](src/NaggingTimeTracker.App/ViewModels/EntriesViewModel.cs#L179-L205));
  - `ApplyPresetDates` and `RefreshFilterOptions` ([:248-292](src/NaggingTimeTracker.App/ViewModels/EntriesViewModel.cs#L248-L292)).

  The filter bar's XAML is [EntriesWindow.xaml:21-59](src/NaggingTimeTracker.App/Windows/EntriesWindow.xaml#L21-L59). None of it is reusable as it stands.
- **`WindowManager`** has `ShowEntries` / `ShowSettings` ([WindowManager.cs:22-32](src/NaggingTimeTracker.App/Windows/WindowManager.cs#L22-L32)).
- **Menu and footer:** the tray menu has *All entries…* and *Settings…* ([TrayHost.cs:181-182](src/NaggingTimeTracker.App/Tray/TrayHost.cs#L181-L182)), and so does the quick-panel footer ([QuickPanelWindow.xaml:196-201](src/NaggingTimeTracker.App/Windows/QuickPanelWindow.xaml#L196-L201)).
- **`--show export`** is currently rejected ([StartupOptions.cs:72-77](src/NaggingTimeTracker.Core/Startup/StartupOptions.cs#L72-L77)), and `StartupOptionsTests` asserts that ([:39](tests/NaggingTimeTracker.Tests/Core/StartupOptionsTests.cs#L39)).

**Decisions already made (givens):**

- **From the parent:** the parent's §9 column list and formats; running entries are excluded by default, and running entries that are included get a blank stop and a duration measured to now; the pre-fill of all four filters when opened from the Entries window; local-only (the file goes wherever the user saves it).
- **CSV formula injection is escaped** (stakeholder decision for this phase). A text value that starts with `=`, `+`, `-`, `@`, a tab or a carriage return gets a leading `'`, so Excel shows it as text instead of evaluating it. Those cells then differ from the stored text by that one character.

**Commit granularity:** one commit, "Phase 4 — CSV export", per the parent's one-commit-per-phase rule. The filter-bar extraction goes in the same commit. It has no value on its own, and Phase 3's UI checks re-run against it here.

---

## 1. Core

### 1.1 `Core/Export/CsvExporter.cs`

```csharp
public static class CsvExporter
{
    public static readonly IReadOnlyList<string> Columns = [
        "entry_id", "date", "start", "stop", "duration_hours", "duration_minutes",
        "customer", "task_type", "external_link", "notes", "continued_from_id", "overlap_reason"];

    public static void Write(Stream output, IEnumerable<EntryView> rows, DateTimeOffset nowUtc, TimeZoneInfo tz);
}
```

`tz` is new compared with the parent's signature. Local dates and times need a time zone, and passing it in (`TimeProvider.LocalTimeZone` from the caller) keeps the exporter testable in `America/New_York`.

**Writer setup:**

- `new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true)`. The BOM lets Excel detect UTF-8, and `leaveOpen` means the caller owns the stream.
- `CsvWriter` with `new CsvConfiguration(CultureInfo.InvariantCulture)`, plus:
  - `NewLine = "\r\n"`, per RFC 4180. This is also CsvHelper's default, but it's set explicitly so a library default change can't alter the files.
  - `InjectionOptions = InjectionOptions.Escape`, keeping CsvHelper's default injection characters (`=`, `@`, `+`, `-`, tab, CR) and escape character (`'`).
- Quoting is CsvHelper's default: a field is quoted when it contains a comma, quote or line break, and quotes are doubled. Notes keep their newlines inside quotes.

**Rows:**

- The header comes from `Columns`, then one record per row ordered by `StartUtc`, then `Id`. The exporter sorts defensively even though `QueryViews` already orders by start.
- Fields are written with `WriteField` per column, not by class mapping, so the column order is explicit in one place.
- Each row:

  | Column | Value |
  |---|---|
  | `entry_id` | `Id`, invariant |
  | `date` | local start, `yyyy-MM-dd` |
  | `start` | local start, `yyyy-MM-dd HH:mm` |
  | `stop` | local stop, `yyyy-MM-dd HH:mm`; `""` when running |
  | `duration_hours` | `Math.Round(d.TotalHours, 2, MidpointRounding.AwayFromZero).ToString("0.00", Inv)` |
  | `duration_minutes` | `Math.Round(d.TotalMinutes, 0, MidpointRounding.AwayFromZero)` as an integer |
  | `customer`, `task_type` | the view's names |
  | `external_link` | as stored, or `""` |
  | `notes` | as stored |
  | `continued_from_id` | the id, or `""` |
  | `overlap_reason` | as stored, or `""` |

  Here `d = (StopUtc ?? nowUtc) - StartUtc`.

  Local conversion is `TimeZoneInfo.ConvertTime(utc, tz)`, formatted with `CultureInfo.InvariantCulture`. The two duration columns are rounded independently from the exact duration, so neither is derived from the other's rounding. For example, 90 s gives `0.03` hours and `2` minutes.
- `Flush()` at the end. An empty input writes the header only.

### 1.2 `Core/Entries/FilterState.cs`

The pure part of the filter bar. It's shared by the Entries and Export windows and passed between them for the pre-fill.

```csharp
public sealed record FilterState(
    DatePreset Preset, DateOnly From, DateOnly ToInclusive,
    long? CustomerId, long? TaskTypeId, string Text)
{
    public string? Validate();   // "The 'to' date is before the 'from' date." or null
    public EntryFilter ToEntryFilter(TimeZoneInfo tz);   // via LocalDateRange.ToUtcRange; throws if invalid
    public string DefaultFileName();   // "timesheet-2026-03-02_to_2026-03-08.csv"
}
```

`DefaultFileName` uses the inclusive "to" date (parent §9) in invariant `yyyy-MM-dd`. `ToEntryFilter` passes `Text` through untouched; `EntryRepository` already trims it and ignores a blank value.

### 1.3 `--show export`

`ShowTarget` gains `Export`, and `ParseShow` accepts `export`. The error text becomes *"Valid values: quick-panel, entries, settings, export."*

---

## 2. Shared filter bar (refactor of Phase 3)

### 2.1 `App/ViewModels/FilterBarViewModel.cs`

This moves out of `EntriesViewModel`, unchanged in behaviour:

- the presets list and the `Preset` / `FromDate` / `ToDate` / `IsCustom` / `CustomerOptions` / `CustomerFilter` / `TaskTypeOptions` / `TaskTypeFilter` / `SearchText` / `FilterError` properties;
- the Custom-keeps-the-previous-dates rule;
- the 300 ms search debounce;
- `RefreshOptions()` (the old `RefreshFilterOptions`, keeping the replace-only-when-changed guard);
- the `_suppress` re-entrancy guard.

New members:

- `string AutomationPrefix` (`"EntriesWindow"` / `"ExportWindow"`), set by the constructor. The shared control reads its control ids from it (§2.2).
- `event EventHandler? Changed`, raised after any effective filter change, where the old code called `Reload()`. The search box raises it only after the debounce.
- `FilterState? Current`: `null` while `Validate()` reports an error (which is then in `FilterError`) or a date is unset ("Choose both dates.").
- `void Apply(FilterState state)` sets every field from a state without raising `Changed` per field, then raises it once. That's the pre-fill.
- `void Detach()` stops the debounce timer.

The constructor is `(CatalogService catalog, TimeProvider time, string automationPrefix)`, created by its owning view-model, not by DI. Its defaults are the *This week* preset and "all" for both catalogs.

### 2.2 `App/Controls/FilterBar.xaml(.cs)`

A `UserControl` holding the XAML moved from [EntriesWindow.xaml:21-59](src/NaggingTimeTracker.App/Windows/EntriesWindow.xaml#L21-L59), bound to a `FilterBarViewModel` `DataContext`.

Every control's `AutomationProperties.AutomationId` is bound as `{Binding AutomationPrefix, StringFormat={}{0}.PresetCombo}` and so on. That keeps the Phase 3 ids (`EntriesWindow.PresetCombo`, `.FromDate`, `.ToDate`, `.CustomerFilter`, `.TaskTypeFilter`, `.SearchBox`, `.FilterError`) unchanged and gives the Export window `ExportWindow.*`.

### 2.3 `EntriesViewModel` / `EntriesWindow` changes

- **View-model:**
  - `EntriesViewModel` gets `public FilterBarViewModel Filter { get; }` (prefix `"EntriesWindow"`).
  - It subscribes `Filter.Changed += (_, _) => Reload()`.
  - `Reload()` starts with `Filter.RefreshOptions()`. Then, if `Filter.Current is not { } state`, it returns; otherwise it uses `state.ToEntryFilter(tz)`. The rest of `Reload()` is unchanged.
  - `Detach()` also calls `Filter.Detach()`.
- **Window:** `EntriesWindow.xaml` replaces the filter-bar block with `<controls:FilterBar DataContext="{Binding Filter}" DockPanel.Dock="Top" />`.
- **New Export button:** after **Delete**, the button **Export…** (`EntriesWindow.ExportButton`) calls `ExportCommand`, which is `windows.ShowExport(Filter.Current)`. `EntriesViewModel` gets `WindowManager` injected. The button is disabled while `Filter.Current` is `null`: `ExportCommand.NotifyCanExecuteChanged()` is called from the `Filter.Changed` handler and whenever `Filter.FilterError` changes, since an invalid range doesn't raise `Changed`.

---

## 3. Export window

### 3.1 `App/ViewModels/ExportViewModel.cs`

Constructor: `(IEntryRepository entries, CatalogService catalog, TimeProvider time)`. It owns `Filter` (prefix `"ExportWindow"`) and `IncludeRunning` (`bool`, default `false`).

**Preview:**

- On `Filter.Changed`, on `IncludeRunning` changes and when the window opens, it runs `QueryViews(state.ToEntryFilter(tz))`.
- It drops running rows unless `IncludeRunning` is set, keeps the list, and sets `PreviewText`:
  - *"12 entries will be exported."* (or *"1 entry will be exported."*)
  - *"12 entries will be exported (2 running not included)."*
  - *"No entries match these filters."*
- `CanExport` is `rows.Count > 0 && Filter.Current is not null`.
- The window doesn't subscribe to `RunningEntriesChanged`, so the preview can be out of date if a timer starts or stops while it's open. `Export()` re-queries, and the result text reports the count actually written.
- The query is the same one the Entries window runs, so it's cheap to repeat. Showing the count before writing makes an empty or unexpectedly large export visible up front.

**`Export()`, called with the window as dialog owner:**

1. Re-runs the preview query, so the file reflects the data at the moment of export.
2. Shows a `Microsoft.Win32.SaveFileDialog`:
   - `FileName = state.DefaultFileName()`;
   - `DefaultExt = ".csv"`, `AddExtension = true`;
   - `Filter = "CSV files (*.csv)|*.csv"`;
   - `OverwritePrompt = true` (the dialog asks before replacing an existing file).
3. On OK, writes to a temp file next to the target, `{path}.tmp`, using `CsvExporter.Write(stream, rows, time.NowUtc(), tz)`, then calls `File.Move(tmp, path, overwrite: true)`. A failure halfway can't leave a truncated timesheet in place of a good one.
4. **Errors:** an `IOException` or `UnauthorizedAccessException` (typically the file is open in Excel) deletes the temp file if it exists. It then sets `Error = "Couldn't write the file: {ex.Message}"`. The window stays open.
5. **Success:** sets `LastExportPath = path` and `ResultText = "Exported {n} entries to {file name}."`. That shows the result row with **Open file** and **Open folder**.

**Opening the result:**

- **`OpenFile`:** `Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })`.
- **`OpenFolder`:** `Process.Start("explorer.exe", $"/select,\"{path}\"")`, which opens Explorer with the file selected.

`ApplyPrefill(FilterState?)` calls `Filter.Apply(state)` when the state isn't `null`. `IncludeRunning` is kept as it is.

### 3.2 `App/Windows/ExportWindow.xaml(.cs)`

- **Window:** `Title="Export CSV — Nagging Time Tracker"`, `Width=820`, `SizeToContent=Height`, `ResizeMode=CanMinimize`, `ShowInTaskbar=True`, `CenterScreen`.
- **Layout,** top to bottom:
  1. `<controls:FilterBar DataContext="{Binding Filter}" />`.
  2. A **CheckBox** *Include running entries* (`ExportWindow.IncludeRunning`). Its tooltip reads *"Running entries are exported with a blank stop and the duration so far."*
  3. The preview text (`ExportWindow.Preview`).
  4. The red error text (`ExportWindow.Error`).
  5. A right-aligned **Export…** button (`ExportWindow.ExportButton`, `IsDefault`, bound to `CanExport`).
  6. The result row, visible after a successful export: the result text (`ExportWindow.Result`) and the buttons **Open file** (`ExportWindow.OpenFileButton`) and **Open folder** (`ExportWindow.OpenFolderButton`).
- **On `Closed`:** `vm.Detach()`.

### 3.3 `WindowManager.ShowExport(FilterState? prefill = null)`

The same show-or-activate pattern as the other windows. When `prefill` isn't `null`, it's applied **even if the window is already open**: choosing *Export…* in the Entries window should export what's on that screen. Opening from the tray or quick panel (no prefill) leaves an already-open Export window's filters alone.

---

## 4. Entry points and wiring

- **Tray menu** ([TrayHost.cs:181-182](src/NaggingTimeTracker.App/Tray/TrayHost.cs#L181-L182)): insert **Export CSV…** (`TrayMenu.Export`, calling `windows.ShowExport()`) between *All entries…* and *Settings…*, matching parent §4.2's order.
- **Quick-panel footer** ([QuickPanelWindow.xaml:196-201](src/NaggingTimeTracker.App/Windows/QuickPanelWindow.xaml#L196-L201)): add the hyperlink *Export…* (`QuickPanel.ExportLink`) between the two existing links, bound to a new `OpenExportCommand` on `QuickPanelViewModel`.
- **`--show export`:** a new `case ShowTarget.Export: windows.ShowExport(); break;` in `App.OnStartup` ([App.xaml.cs:66-78](src/NaggingTimeTracker.App/App.xaml.cs#L66-L78)).
- **DI** ([App.xaml.cs:110-115](src/NaggingTimeTracker.App/App.xaml.cs#L110-L115)): register `ExportViewModel` and `ExportWindow` as transient.

---

## 5. Tests

### 5.1 Unit tests (`tests/NaggingTimeTracker.Tests/`)

**`Core/CsvExporterTests.cs`** (new). Each test writes to a `MemoryStream`, then reads the bytes and a CsvHelper `CsvReader` (invariant) back:

- **Header:** the first line is exactly `entry_id,date,start,stop,duration_hours,duration_minutes,customer,task_type,external_link,notes,continued_from_id,overlap_reason`. The file starts with the BOM `EF BB BF`, and lines end in `\r\n`.
- **Quoting:** notes containing a comma, a quote and an embedded newline round-trip through the reader unchanged.
- **Culture:** under `CultureInfo.CurrentCulture = de-DE`, 75 minutes → `1.25` (not `1,25`) and `75`.
- **Rounding:** 90 s → `0.03` / `2`; 89 s → `0.02` / `1`; 0.5 min → `0.01` / `1`, rounded half away from zero.
- **DST:** in `America/New_York`, an entry from 2026-03-08 01:30 to 03:30 local (06:30Z–07:30Z) → start `2026-03-08 01:30`, stop `2026-03-08 03:30`, `1.00` hours, `60` minutes, because the clock change is inside the entry.
- **Running entry:** blank stop, and the duration measured to `nowUtc`.
- **Optional columns:** blank `external_link`, `continued_from_id` and `overlap_reason` when unset; the id is written when set.
- **Order:** input in reverse start order is written sorted by start.
- **Formula escaping:**
  - notes `=SUM(A1)` → `'=SUM(A1)`;
  - `+1`, `-x`, `@x` → `'+1`, `'-x`, `'@x`;
  - a link `=HYPERLINK("x")` is escaped too;
  - notes `a=b` (not leading) are unchanged.
- **Empty input:** the header only.

**`Core/FilterStateTests.cs`** (new):

- `Validate` returns `null` for a valid range and the error for to < from.
- `ToEntryFilter` gives the half-open UTC range (it delegates to `LocalDateRange`) and carries the ids and text.
- `DefaultFileName` is `timesheet-2026-03-02_to_2026-03-08.csv`.
- `ToEntryFilter` on an invalid state throws `InvalidOperationException`.

**`Core/StartupOptionsTests.cs`** (change): `--show export` parses. The rejection case becomes `--show reports`.

### 5.2 Manual verification (scripted with UI Automation, as in Phases 2–3)

The Phase 3 script ([plan-phase3-entries-editor.md](plan-phase3-entries-editor.md) §7.2) is re-run first, unchanged. That's the regression check for the filter-bar extraction, because every Phase 3 filter id must still resolve. Then these Phase 4 checks run against a throwaway `--data-dir`. The `SaveFileDialog` is driven by keyboard rather than UI Automation patterns. Phase 3 found that Win32 dialog buttons appear as plain panes to the managed UIA client. The dialog opens with focus in its *File name* box, so the script sends `^a`, the full path and `{ENTER}` with `SendKeys` (parent §10 types the path the same way). The overwrite confirmation is answered with `%y` (Alt+Y).

1. `--show export` on a fresh DB → *"No entries match these filters."* and **Export…** disabled.
2. Seed two stopped entries and one running one through the Entries window, using the Phase 3 steps. One stopped entry has notes `=SUM(A1)` and a note containing `a, "b"` plus a newline.
3. **Export…** from the Entries window, with the customer filter set to one customer → the Export window opens with the **same** customer selected and the same dates, and the preview counts only that customer's stopped entries.
4. Clear the customer filter. The preview says *"2 entries will be exported (1 running not included)."* Tick *Include running entries* → *"3 entries…"*.
5. **Export…** → the save dialog proposes `timesheet-<from>_to_<to>.csv` → type `%TEMP%\ntt-export\out.csv` → Enter → *"Exported 3 entries to out.csv."*, and **Open file / Open folder** are visible.
6. Read `out.csv` from the script:
   - it starts with the BOM, and the header matches §1.1;
   - it has 3 records;
   - the running record has a blank stop;
   - the notes field reads `'=SUM(A1)`;
   - the quoted note round-trips.
7. **Overwrite:** export again to the same path → the overwrite prompt appears → Yes → the file is replaced, with no `.tmp` left behind.
8. **File locked:** lock `out.csv` with an exclusive `FileStream` from the script, then export to it → the red *"Couldn't write the file: …"* error shows, the window stays open, and no `.tmp` is left. Release the lock.
9. **Single window, and the pre-fill re-applies:** with the Export window open, change the Entries window's preset to *Today* and click **Export…** again → the one Export window shows *Today*.
10. The quick-panel footer *Export…* link opens or activates the Export window.

The tray-menu item needs a person to click (the same limitation as before).

---

### Implementation notes

- **CsvHelper 34 API names.** CsvHelper 34 replaced the configuration API the draft named. The exporter uses `CsvWriter.Create(stream, new CsvWriterOptions { FormatProvider, NewLine = "
", Injection = CsvInjection.Escape, WriteByteOrderMark = true }, leaveOpen: true)`, `WriteHeader`, `WriteField` and `EndRecord()`. CsvHelper writes the BOM itself, so there's no `StreamWriter`. The tests read files back with `CsvReader.Create(…)` and `reader[i].ToString()`.
- **The Export window is 1000 px wide,** like the Entries window. At 820 px the shared filter bar wrapped the search box onto a second line.
- **A stale export error clears** on any filter change (`ExportViewModel.Refresh`). The last-export result row stays, because it describes the file that was actually written.
- **A locked target** surfaces as `UnauthorizedAccessException` ("Access to the path is denied.") from `File.Move`, which the handler already catches alongside `IOException`.
- **The save dialog was driven by keyboard** in the UI checks (`SetForegroundWindow` on the dialog, then `SendKeys`), and that worked reliably.

## 6. Deviations from the parent (fold into [plan-time-tracker-v1.md](plan-time-tracker-v1.md))

1. **CSV formula injection is escaped** (stakeholder decision). Text cells starting with `=`, `+`, `-`, `@`, tab or CR get a leading `'`. Parent §9 says notes are exported "as stored".
2. **`CsvExporter.Write` takes a `TimeZoneInfo`** in addition to the parent's `(Stream, IEnumerable<EntryView>, DateTimeOffset nowUtc)`.
3. **The Export window shows a preview count and disables Export when nothing matches.** It writes through a temp file and replaces the target. Neither is in parent §9.
4. **The pre-fill also re-applies to an already-open Export window** when it's opened from the Entries window.
5. **The filter bar is shared** (`FilterBarViewModel`, `Controls/FilterBar.xaml`, `Core/Entries/FilterState.cs`). New names; parent §9 only says "same presets as §5".

---

## Steps

- [x] Core: `Export/CsvExporter.cs` (§1.1)
- [x] Core: `Entries/FilterState.cs` (§1.2)
- [x] Core: `ShowTarget.Export` + `ParseShow` in [StartupOptions.cs](src/NaggingTimeTracker.Core/Startup/StartupOptions.cs) (§1.3)
- [x] Tests: `Core/CsvExporterTests.cs`, `Core/FilterStateTests.cs`, and the change to `Core/StartupOptionsTests.cs` (§5.1)
- [x] App: `ViewModels/FilterBarViewModel.cs`, `Controls/FilterBar.xaml(.cs)` (§2.1–2.2)
- [x] App: refactor [EntriesViewModel.cs](src/NaggingTimeTracker.App/ViewModels/EntriesViewModel.cs) and [EntriesWindow.xaml](src/NaggingTimeTracker.App/Windows/EntriesWindow.xaml) onto the shared filter bar, and add **Export…** (§2.3)
- [x] App: `ViewModels/ExportViewModel.cs`, `Windows/ExportWindow.xaml(.cs)`, and `ShowExport` in [WindowManager.cs](src/NaggingTimeTracker.App/Windows/WindowManager.cs) (§3)
- [x] App: the tray menu item, the quick-panel footer link and command, the `--show export` case and the DI registrations (§4)
- [x] `dotnet build` (zero warnings) and `dotnet test --filter "Category!=UI"` (all green)
- [x] Re-run the Phase 3 UI Automation script (regression for the filter-bar extraction), then the Phase 4 checks from §5.2; record the results in the commit message
- [x] Update [README.md](README.md): the status line (Phases 1–4, export); the `--show` table row (`export`); mention exporting from the Entries window; add a Conventions bullet on formula escaping in exports
- [x] Tick the parent's Phase 4 checklist in [plan-time-tracker-v1.md](plan-time-tracker-v1.md), fold in the §6 deviations (§9), and flip this doc's Status to Shipped
- [x] Commit "Phase 4 — CSV export"

---

## Non-goals

- **No other export formats** (XLSX, JSON) and no column selection or reordering. Parent §9 fixes the CSV columns.
- **No rounding to billing increments, rates or totals rows.** Parent Non-goals: *"Export gives raw durations."*
- **No CSV import** (parent Non-goals).
- **No remembered export folder or filter between sessions.** The save dialog uses its own default folder, and the filters reset when the window reopens.
- **No scheduled or automatic exports.**
- **No semicolon-separated variant.** Excel in locales whose list separator is `;` (e.g. de-DE) opens a comma CSV as one column. The fix there is Excel's *Data → From Text/CSV* import. Parent §9 fixes the delimiter as a comma.
- **No progress UI.** A local export of even years of entries is a single fast query and write.
- **No UI test project** (Phase 8). The AutomationIds are set now.
