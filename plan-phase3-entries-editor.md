# Phase 3 — Entries Window, Editor and Catalog Settings (implementation plan)

**Status:** Shipped — 1 commit. Build clean, 217/217 tests green, 34 scripted UI Automation checks of §7.2 pass (reported as 36 in the Phase 3 commit message; the script has 34 checks), and the tray-menu items were checked by hand.

**Sequencing note:** this doc expands **Phase 3** of [plan-time-tracker-v1.md](plan-time-tracker-v1.md) (§12, "Phase 3 — Entries window + editor") into implementable detail. It builds on Phase 2 ([plan-phase2-tracking-tray.md](plan-phase2-tracking-tray.md)), which has shipped (commit `3c86985`). The parent plan stays the source of truth for behaviour: §5 (entries window, editor, manage lists), §4.1 (amber running rows) and §1 (AutomationIds, `--show`). Deviations are listed in §8 of this doc and get folded back into the parent once this doc is approved. Phase 4 (CSV export) reuses the filter bar and the window manager from this phase.

## Overview

Phase 3 adds the three things that make recorded time reviewable and correctable:

- **The Entries window:** filter, list, total, then edit / continue / delete / add.
- **The modal entry editor:** for edits and for manual entries of untracked time.
- **A Settings window** with *Customers* and *Task types* tabs, for rename and archive. Its other tabs arrive in Phases 5–7.

The tray menu, the quick-panel footer and `--show` gain the matching entry points.

**What exists today (confirmed by reading the code):**

- **`TrackingService`** already has everything the editor and grid call:
  - `Save(Entry)` with its guards ([TrackingService.cs:164](src/NaggingTimeTracker.Core/Services/TrackingService.cs#L164)), plus `Delete` and `Continue`.
  - `RunningEntriesChanged` is raised by every change, including `Save` and `Delete`.
- **The UI flows already exist.** `TrackingActions.Continue(id, owner)` handles the confirm-and-retry dialog ([TrackingActions.cs](src/NaggingTimeTracker.App/Tracking/TrackingActions.cs)), and the Entries window reuses it.
- **Repository queries:** `IEntryRepository.QueryViews`, `GetOverlapping` and `GetOverlappingIds` exist and are tested ([Repositories.cs:59-69](src/NaggingTimeTracker.Core/Abstractions/Repositories.cs#L59-L69)). `LocalDateRange.ToUtcRange` converts an inclusive local date range ([LocalDateRange.cs](src/NaggingTimeTracker.Core/Time/LocalDateRange.cs)).
- **`CatalogService`** has `GetActive…`, `GetOrCreate…`, `Rename…` (returning `NameTaken`), `Set…Archived` and `GetNames()` ([CatalogService.cs:25-43](src/NaggingTimeTracker.Core/Services/CatalogService.cs#L25-L43)). It has **no "all items, archived included" accessor**, which the filter dropdowns and the Settings lists need (§1.4).
- **`--show`** only accepts `quick-panel` ([StartupOptions.cs:68-73](src/NaggingTimeTracker.Core/Startup/StartupOptions.cs#L68-L73)), and `StartupOptionsTests` asserts that `--show entries` is rejected. That test changes in this phase (§5).
- **The tray menu** is built in [TrayHost.cs:160-180](src/NaggingTimeTracker.App/Tray/TrayHost.cs#L160-L180): Start new, Stop all, Continue last, separator, Pause ▸, separator, Exit. The quick panel has no footer yet; the Recent `ItemsControl` ends at [QuickPanelWindow.xaml:189](src/NaggingTimeTracker.App/Windows/QuickPanelWindow.xaml#L189), just before the panel's closing `</StackPanel>`.
- **DI** registers services and windows in [App.xaml.cs:95-108](src/NaggingTimeTracker.App/App.xaml.cs#L95-L108). The `--show` dispatch is at [App.xaml.cs:66-69](src/NaggingTimeTracker.App/App.xaml.cs#L66-L69).

**Decisions already made (givens):** the parent's §5 behaviour; Phase 1–2 conventions (synchronous repositories, `TimeProvider` + `NowUtc()`, `TrackingService` as the only mutator of entries, `TrackingActions` for confirmation UI); AutomationIds on every control (parent §1).

**Commit granularity:** one commit, "Phase 3 — entries window, editor, catalog settings", per the parent's one-commit-per-phase rule.

---

## 1. Core additions (pure, unit-tested)

### 1.1 `Core/Time/DatePresets.cs`

```csharp
public enum DatePreset { Today, ThisWeek, LastWeek, ThisMonth, Custom }

public static (DateOnly From, DateOnly ToInclusive) Range(DatePreset preset, DateOnly today);
```

- **Today:** `(today, today)`.
- **ThisWeek:** Monday of `today`'s week through the Sunday after it (weeks start on Monday, per parent §5).
- **LastWeek:** the 7 days before that Monday.
- **ThisMonth:** the 1st through the last day of `today`'s month.
- **Custom:** throws `ArgumentException`. Custom ranges come from the two date pickers, not from here.

`today` is the caller's local date, computed as `DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.NowUtc(), time.LocalTimeZone).DateTime)`. That expression is also added as `Clock.TodayLocal(this TimeProvider tp)` in [Clock.cs](src/NaggingTimeTracker.Core/Time/Clock.cs), because the Entries window, the editor defaults and `TrayHost.NextLocalMidnight` all need it; the last one switches to it.

### 1.2 `Core/Time/LocalTime.cs`

Converts a local date and an `HH:mm` text into UTC. Used by the editor.

```csharp
public static bool TryParseTime(string? text, out TimeOnly time);   // "9:05", "09:05", "9.05" → 09:05; trims
public static LocalToUtcResult ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo tz);
public sealed record LocalToUtcResult(DateTimeOffset? Utc, string? Error);
```

- **`TryParseTime`:** accepts `H:mm` / `HH:mm` and `.` as the separator, with hours 0–23 and minutes 0–59, using `CultureInfo.InvariantCulture`. Anything else returns `false`. The editor's time boxes are 24-hour regardless of culture.
- **`ToUtc`:**
  - A time that doesn't exist because of a clock change (`tz.IsInvalidTime`) returns the error *"That time doesn't exist on this date (clocks went forward)."*
  - An ambiguous time (clocks went back) resolves to the **earlier** instant, the larger offset, matching `LocalDateRange`.
  - The result is truncated to whole milliseconds (it already is, since seconds are zero).

### 1.3 Formatting and link helpers

Add these to [DurationFormat.cs](src/NaggingTimeTracker.Core/Formatting/DurationFormat.cs):

- `HoursMinutes(TimeSpan d)` → `"1:05"`, `"0:00"`, `"26:30"`, using whole minutes, truncated, with negative clamped to zero. Used for the grid's Duration column and the total.

Add `Core/Formatting/EntryText.cs`:

- `FirstLine(string notes)` returns the text before the first `\r` or `\n`, trimmed.
- `IsWebUrl(string? link, out Uri? uri)` is `Uri.TryCreate(link, UriKind.Absolute, out uri)` with the scheme `http` or `https`. It's used to decide whether the grid's Link cell is a hyperlink and whether the editor shows the *"Not a URL — saved as plain text"* hint.
- `StopSuffix(DateOnly startLocalDate, DateOnly stopLocalDate)` returns `""` on the same day, `" (+1)"` for the next day, and `" (+N)"` beyond that. The Stop column shows a cross-midnight entry as `06:00 (+1)`.

### 1.4 `CatalogService` additions

Add `IReadOnlyList<Customer> GetAllCustomers()` and `IReadOnlyList<TaskType> GetAllTaskTypes()` to [CatalogService.cs](src/NaggingTimeTracker.Core/Services/CatalogService.cs). They're pass-throughs to `GetAll()`, which is ordered by name with archived items included. The filter dropdowns and Settings lists use them.

### 1.5 `--show` targets

`ShowTarget` gains `Entries` and `Settings`, and `ParseShow` accepts `entries` and `settings`. The error text becomes *"Valid values: quick-panel, entries, settings."* `export` stays invalid until Phase 4.

---

## 2. Window manager

`App/Windows/WindowManager.cs`, a singleton. It owns the app's non-modal windows so that each one exists at most once (parent §5: *"one instance at most (re-activated if already open)"*).

```csharp
public void ShowEntries();
public void ShowSettings(SettingsTab tab = SettingsTab.Customers);
```

- **Show or activate.** Each method resolves the window from DI the first time, or when the previous instance has been closed (tracked with the window's `Closed` event, which sets the field back to `null`). Then it `Show()`s it, sets `WindowState = Normal` if it was minimised, and calls `Activate()`.
- **Closing really closes.** These are ordinary windows (with `ShowInTaskbar=True`), unlike the quick panel. Closing disposes their view-model subscriptions (§3.5), and the next open builds them fresh.
- **Who calls it:** the tray menu, the quick-panel footer and `--show` all go through it. The single-instance signal keeps opening the quick panel. Phase 4 adds `ShowExport(EntryFilter? prefill)` here.

`TrayHost` and `QuickPanelViewModel` get `WindowManager` injected. `App.OnStartup` dispatches `--show entries` / `--show settings` to it.

---

## 3. Entries window

### 3.1 `App/ViewModels/EntriesViewModel.cs`

Constructor: `(IEntryRepository entries, TrackingService tracking, CatalogService catalog, TrackingActions actions, TimeProvider time)`.

**Filter state:**

- `Preset` (a `DatePreset`; the default is `ThisWeek`).
- `FromDate` and `ToDate` (`DateTime?` for the `DatePicker` bindings). They're editable only when `Preset == Custom`. Choosing Custom pre-fills them with the range of the preset that was active, so the user adjusts rather than starts from nothing.
- `CustomerFilter` / `TaskTypeFilter`, of type `FilterOption?`, where `FilterOption(long? Id, string Label)`. The first item is `(null, "All customers")` / `(null, "All task types")`. After it come all items from `GetAll…()`, and archived ones are labelled `"{name} (archived)"`.
- `SearchText`.

**When the grid reloads:**

- Any filter change calls `Reload()`.
- `SearchText` changes are debounced by 300 ms with a `DispatcherTimer`, so typing doesn't run a query per keystroke.
- A `ToDate` before `FromDate` doesn't reload. It shows the inline error *"The 'to' date is before the 'from' date."*

**`Reload()`:**

1. Builds `EntryFilter` from `LocalDateRange.ToUtcRange(from, to, time.LocalTimeZone)` and the filters.
2. Reads `QueryViews(filter)` and `GetOverlappingIds(FromUtc, ToUtc, now)`. That's one self-join query for the ⧉ column, not one per row.
3. Reads `tracking.GetRunning().Count` for the amber rule.
4. Rebuilds `Rows` (an `ObservableCollection<EntryRowViewModel>`) **newest first**. `QueryViews` returns oldest first (the export's order), so the view-model reverses it. The selection is kept by id when that row is still present.
5. Refreshes the customer and task-type filter options from `GetAll…()`, keeping the selected option by id. That way a customer created in the editor or quick panel appears in the filter without reopening the window.
6. Sets `TotalText = DurationFormat.HoursMinutes(sum of row durations)` and `RowCountText = "{n} entries"`.

**Live refresh:**

- The view-model subscribes to `tracking.RunningEntriesChanged` → `Reload()`.
- While any row is running, a 30-s `DispatcherTimer` recomputes the running rows' durations and the total from `now`, without re-querying.

**`EntryRowViewModel`:** immutable, built from `EntryView` + `now` + `tz` + `overlapIds` + `parallel`.

| Property | Value |
|---|---|
| `Date` | local start date, `d` format (current culture) |
| `Start` | local start time, `t` format |
| `Stop` | local stop time (`t`) + `StopSuffix`; empty when running |
| `IsRunning` | `StopUtc is null` |
| `Duration` | `HoursMinutes((Stop ?? now) - Start)` |
| `Customer`, `TaskType` | names from the view |
| `Link`, `LinkUri` | the stored link; `LinkUri` is set only when `IsWebUrl` |
| `NotesFirstLine` | `FirstLine(notes)`; the full notes are the cell tooltip |
| `IsContinuation` | `ContinuedFromId is not null` (↪) |
| `Overlaps` | `overlapIds.Contains(Id)` (⧉) |
| `OverlapToolTip` | `OverlapReason`, or *"Overlaps another entry"* when there is no reason |
| `HighlightParallel` | `IsRunning && parallel` (the global running count ≥ 2), giving the amber row background from parent §4.1 |

### 3.2 Commands

Each command uses `Host.Owner` (the window) as the dialog owner. The window doesn't hide on deactivation, so no modal guard is needed.

- **`EditCommand`** (also double-click, and Enter on a row): opens `EntryEditorDialog` for `row.Id` (§4), then `Reload()`.
- **`NewEntryCommand`:** opens the editor in create mode, then `Reload()`.
- **`ContinueCommand`:** `actions.Continue(row.Id, owner)`. The grid refreshes through the event.
- **`DeleteCommand`:** a `MessageBox` (Yes/No, Warning icon): *"Delete the entry {Date} {Start}–{Stop}, {Customer} · {TaskType} ({Duration})?"*. For a running entry the text ends *"(running)"* instead of a stop time. On Yes: `tracking.Delete(row.Id)`.

The commands that act on a row are disabled when no row is selected.

### 3.3 `App/Windows/EntriesWindow.xaml(.cs)`

- **Window:** `Title="Entries — Nagging Time Tracker"`, `Width=1000`, `Height=600`, `MinWidth=700`, `MinHeight=360`, `WindowStartupLocation=CenterScreen` and `ShowInTaskbar=True`.
- **Filter bar** (top, in a `WrapPanel`): the preset `ComboBox` (*Today / This week / Last week / This month / Custom*), the From and To `DatePicker`s, the customer and task-type `ComboBox`es, the search `TextBox` (with *"Search notes and links"* as placeholder text in a `TextBlock` overlay), and the inline filter error text.
- **`DataGrid`:** `IsReadOnly=True`, `SelectionMode=Single`, `AutoGenerateColumns=False`, `CanUserSortColumns=False` (always newest first by start), `HeadersVisibility=Column`.
  - **Columns:** Date, Start, Stop (a running row shows a small amber-bordered "running" badge), Duration (right-aligned), Customer, Task type, Link, Notes, ↪ and ⧉.
  - **Link** is a `Hyperlink` when `LinkUri` is set and plain text otherwise. Clicking the hyperlink calls `Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })`.
  - **↪ and ⧉** are narrow; their headers have tooltips (*"Continued from another entry"*, *"Overlaps another entry"*).
  - **Row style:** a `DataTrigger` on `HighlightParallel` sets the background to `ParallelBackgroundBrush` (already in [App.xaml](src/NaggingTimeTracker.App/App.xaml)).
  - **Interaction:** `MouseDoubleClick` on a row → `EditCommand`. `Enter` → `EditCommand` and `Delete` → `DeleteCommand` are handled in the grid's `PreviewKeyDown` with `e.Handled = true`. Otherwise `DataGrid` consumes Enter to move to the next row.
- **Footer** (bottom, in a `DockPanel`): the buttons **New entry…**, **Edit…**, **Continue** and **Delete** on the left; *"{n} entries · Total {h:mm}"* on the right.
- **On `Closed`:** the view-model unsubscribes from `RunningEntriesChanged` and stops its timers (`EntriesViewModel.Detach()`). A closed window must not keep querying.

**AutomationIds:** `EntriesWindow`, `EntriesWindow.PresetCombo`, `.FromDate`, `.ToDate`, `.CustomerFilter`, `.TaskTypeFilter`, `.SearchBox`, `.FilterError`, `.Grid`, `.NewButton`, `.EditButton`, `.ContinueButton`, `.DeleteButton`, `.Summary`.

### 3.4 DI

Register `EntriesViewModel` and `EntriesWindow` as transient, since a new pair is created each time the window is reopened, and `WindowManager` as a singleton.

### 3.5 View-model lifetime

The view-models subscribe to singleton events (`RunningEntriesChanged`), so a closed window would leak and keep reloading. That's why `Detach()` is called from `Closed` (§3.3). The same rule applies to `SettingsViewModel` (§5), which subscribes to nothing in this phase but gets the same `Detach()` hook for Phases 5–7.

---

## 4. Entry editor

### 4.1 `App/ViewModels/EntryEditorViewModel.cs`

It's constructed per dialog, not through DI, by a small factory `EntryEditorFactory` (a singleton holding the services). Its `Create(long? entryId)` loads the entry fresh with `GetById`, so the edit starts from the stored row, not from a grid row that may be stale.

**Fields:**

- `StartDate`/`StartTime`, `StopDate`/`StopTime`: a `DateTime?` for each `DatePicker` and a `string` for each time box.
- `CustomerText`/`TaskTypeText` with the active-name lists.
- `ExternalLink` and `Notes` (multiline).
- `OverlapReason`.
- Read-only: `ContinuedFromText`, `IsRunningEntry` and `Title` (*"Edit entry"* / *"New entry"*).

**Initial values:**

- **Edit:** the local date and time of `StartUtc`/`StopUtc`. A running entry has an empty stop and the hint *"Running — leave Stop empty to keep it running."*
- **New:**
  - Stop is now, floored to the quarter hour, and start is one hour before that, both local.
  - Customer and type come from `GetLatestStarted()`, as in the quick panel.
  - A brand-new database leaves both empty.

**Resolving the customer and task type on Save:**

- If the typed text equals (case-insensitively, after trimming) the **current** entry's customer name, the existing `CustomerId` is kept **without** calling `GetOrCreateCustomer`. Otherwise `GetOrCreateCustomer(text)` is used. Task type works the same way.
- **Why:** `GetOrCreate` un-archives a match ([CatalogService.cs](src/NaggingTimeTracker.Core/Services/CatalogService.cs) `GetOrCreate`). Without this rule, fixing a typo in the notes of an old entry whose customer was archived would silently bring the customer back into every picker.
- Empty text gives the blocking error *"Customer and task type are required."*

**Validation** runs on every field change and again in `Save`:

- **Blocking errors** (red text, shown live as fields change). **Save stays enabled.** Clicking it with a blocking error shows the error and leaves the dialog open, which is exactly what parent §10's `EntriesEditorTests` clicks through:
  - Each date/time pair must parse (`LocalTime.TryParseTime`), with a date set: *"Start time must be like 09:30."*
  - The local times must exist: *"That time doesn't exist on this date (clocks went forward)."*
  - Stop must be after start: *"Stop must be after start."*
  - A new entry needs a stop: *"A new entry needs a stop time."*
  - A stopped entry can't be re-opened by clearing Stop: *"Stop can only be empty while the entry is running."*
- **Non-blocking:**
  - The link hint *"Not a URL — saved as plain text"*, when the link is non-empty and not `IsWebUrl`.
  - The overlap warning *"Overlaps {n} other entries"*, when overlaps exist and the reason is empty.

**Overlaps:**

- **When the query runs:** every time start and stop both parse and are valid. It calls `GetOverlapping(startUtc, stopUtc /* null when running */, excludeId: entry.Id, or `null` for a new entry, now)`.
- **`HasOverlaps`** controls whether the *Overlap reason* row is visible.
- **No overlaps:** the reason box is hidden, and the stored reason is **kept** as it is. Hiding a field must not delete data.
- **The query is local SQLite** and cheap, so there's no debounce. It only runs once both times parse.

**`Save()` → `bool`** (`true` closes the dialog):

1. Builds `Entry` from the stored row (`with` the edited fields), or a new one with `Id = 0`. `OverlapReason` is the trimmed text, or `null` when it's blank.
2. Calls `tracking.Save(entry)`, which trims the link, sets the timestamps and validates again.
3. **Errors:**
   - **`InvalidOperationException`** when Stop is empty and the stored row is already stopped (the entry was stopped from the tray while the dialog was open). The stop fields are filled from the stored row and the inline error reads *"This entry was stopped at {t} while you were editing. Check the stop time and save again."* Returns `false`.
   - **`InvalidOperationException`** when the entry no longer exists: a `MessageBox` *"This entry was deleted while you were editing it."*, then returns `true` so the dialog closes.
   - **`ArgumentException`** can't happen once validation has passed; if it does, it is shown as the inline error.

**Continued from:** when `ContinuedFromId` resolves (through `GetById`), the read-only link reads *"Continued from: {d} {t} {customer} · {type}"*. Clicking it opens a **second** `EntryEditorDialog` for that entry, owned by this one. When it closes, this editor re-runs the overlap query. A source that has since been deleted has a `null` id (`ON DELETE SET NULL`), so no link is shown.

### 4.2 `App/Windows/EntryEditorDialog.xaml(.cs)`

- **Window:** modal, `SizeToContent=Height`, `Width=480`, `ResizeMode=NoResize`, `ShowInTaskbar=False`, `WindowStartupLocation=CenterOwner`. It is **not** `Topmost`; it sits above its owner window.
- **Form:** a two-column grid with these rows:
  - Start (`DatePicker` + time `TextBox`, `Width=60`);
  - Stop (the same, plus the running hint);
  - Customer and Task type (editable combos, as in the quick panel);
  - Link (+ hint);
  - Notes (`AcceptsReturn=True`, `Height=80`, vertical scroll);
  - the *Continued from* link (`Hyperlink` in a `TextBlock`);
  - Overlap reason (visible only with `HasOverlaps`) + warning.

  Then the red error text, and the buttons **Save** (`IsDefault`, always enabled) and **Cancel** (`IsCancel`).
- **Enter in Notes** inserts a newline, because the box has `AcceptsReturn=True`. Enter in any other field saves.

**AutomationIds:** `EntryEditor`, `EntryEditor.StartDate`, `.StartTime`, `.StopDate`, `.StopTime`, `.RunningHint`, `.CustomerCombo`, `.TaskTypeCombo`, `.LinkBox`, `.LinkHint`, `.NotesBox`, `.ContinuedFromLink`, `.OverlapReasonBox`, `.OverlapWarning`, `.Error`, `.SaveButton`, `.CancelButton`.

---

## 5. Settings window (Customers and Task types)

### 5.1 `App/ViewModels/SettingsViewModel.cs` + `CatalogListViewModel.cs`

`CatalogListViewModel` is generic over the kind, with two instances: Customers and Task types. Each holds:

- `Items` (`ObservableCollection<CatalogItemRow>`, where `CatalogItemRow(long Id, string Name, bool Archived)`), loaded from `GetAll…()`;
- `Selected`;
- `RenameCommand`;
- `ToggleArchiveCommand`, labelled *Archive* or *Unarchive* to match the selection;
- `ShowArchived` (a `bool`, default `true`). Archived rows are shown greyed with *"(archived)"*, and the checkbox *"Show archived"* hides them.

**Rename:**

- Opens `RenameDialog` (§5.3), prefilled with the current name.
- On OK it calls `catalog.Rename…(id, text)`:
  - `NameTaken` → the dialog stays open with the parent's message *"A customer named '{name}' already exists."* (or *"…task type…"*).
  - `NotFound` → it closes and reloads (the item vanished, which can't really happen without deletes).
  - `Renamed` → it closes and reloads.
- An empty name is rejected inside the dialog (OK disabled).

**Archive / unarchive:** `catalog.Set…Archived(id, !archived)`, then reload, keeping the selection.

**Effects elsewhere:** after a rename or archive, the open Entries window and the quick panel show the new names and pickers on their next reload. The Entries window reloads on its own triggers; the quick panel reloads every time it's shown. No cross-window event is added (see Non-goals).

### 5.2 `App/Windows/SettingsWindow.xaml(.cs)`

- **Window:** `Title="Settings — Nagging Time Tracker"`, `Width=560`, `Height=440`, `ShowInTaskbar=True`, `CenterScreen`.
- **Content:** a `TabControl` with two tabs, *Customers* and *Task types*. Phases 5–7 add tabs here, so the `TabControl` is the window's root content now. Each tab has a `ListBox` (name, greyed when archived), the *Show archived* checkbox, and the buttons **Rename…** and **Archive** / **Unarchive**.
- **`SettingsTab`** (`Customers`, `TaskTypes`) is selected by `WindowManager.ShowSettings(tab)`.

**AutomationIds:** `SettingsWindow`, `SettingsWindow.Tabs`, `.CustomersTab`, `.CustomersList`, `.CustomersShowArchived`, `.CustomerRenameButton`, `.CustomerArchiveButton`, `.TaskTypesTab`, `.TaskTypesList`, `.TaskTypesShowArchived`, `.TaskTypeRenameButton`, `.TaskTypeArchiveButton`.

### 5.3 `App/Windows/RenameDialog.xaml(.cs)`

A small modal: the label *"New name"*, a `TextBox`, inline error text, and **OK** (`IsDefault`, disabled when the trimmed text is empty) / **Cancel** (`IsCancel`). It is constructed as `new RenameDialog(title, currentName, Func<string, string?> tryRename)`. On OK it calls `tryRename(text)`: an `null` result closes the dialog, and a non-null string is shown as the error.

**AutomationIds:** `RenameDialog`, `RenameDialog.NameBox`, `.Error`, `.OkButton`, `.CancelButton`.

---

## 6. Entry points

- **Tray menu** ([TrayHost.cs:160-180](src/NaggingTimeTracker.App/Tray/TrayHost.cs#L160-L180)): after Pause ▸'s separator, insert **All entries…** (`TrayMenu.AllEntries`) and **Settings…** (`TrayMenu.Settings`), then a separator, then Exit. This matches parent §4.2's order. *Export CSV…* goes between them in Phase 4.
- **Quick-panel footer:** after the Recent section in [QuickPanelWindow.xaml](src/NaggingTimeTracker.App/Windows/QuickPanelWindow.xaml), add a top-bordered `StackPanel` (horizontal, right-aligned) with two `Hyperlink`s, *All entries…* (`QuickPanel.AllEntriesLink`) and *Settings…* (`QuickPanel.SettingsLink`). They are bound to `OpenEntriesCommand` / `OpenSettingsCommand` on `QuickPanelViewModel`, which call `WindowManager`. The panel hides itself when the new window activates (its normal `Deactivated` path).
- **`--show`:** `App.OnStartup` maps `ShowTarget.Entries` / `Settings` to `WindowManager.ShowEntries()` / `ShowSettings()`.

---

## 7. Tests

### 7.1 Unit tests (`tests/NaggingTimeTracker.Tests/`)

| File | Cases |
|---|---|
| `Core/DatePresetsTests.cs` | Today. ThisWeek from a Wednesday, a Monday (starts that day) and a Sunday (the Monday 6 days before). LastWeek across a month boundary. ThisMonth in February 2028 (a leap year, ends on the 29th) and December (ends on the 31st). Custom throws. |
| `Core/LocalTimeTests.cs` | `TryParseTime` accepts `9:05`, `09:05`, `9.05` and ` 23:59 `, and rejects `24:00`, `9:60`, `905`, `""`, `null` and `ab:cd`. `ToUtc` in `America/New_York`: a normal day; 2026-03-08 02:30 → the "doesn't exist" error; 2026-11-01 01:30 → the earlier instant (05:30Z, not 06:30Z). |
| `Core/DurationFormatTests.cs` (add) | `HoursMinutes`: 0 → `0:00`, 65 min → `1:05`, 26.5 h → `26:30`, 59 s → `0:00`, negative → `0:00`. |
| `Core/EntryTextTests.cs` | `FirstLine` with `\n`, `\r\n`, no newline, leading blank. `IsWebUrl`: `https://x.test/a` yes, `http://x` yes, `JIRA-123` no, `ftp://x` no, `www.x.com` no, `""`/`null` no. `StopSuffix`: same day, +1, +3. |
| `Core/CatalogServiceTests.cs` (add) | `GetAll…` includes archived items, ordered by name. |
| `Core/StartupOptionsTests.cs` (change) | `--show entries` and `--show settings` now parse. `--show export` is the "not valid yet" case. |
| `Core/ClockTests.cs` (add) | `TodayLocal`: 2026-03-02T03:00Z in `America/New_York` → 2026-03-01. |

### 7.2 Manual verification (scripted with UI Automation, as in Phase 2)

The same approach as [plan-phase2-tracking-tray.md](plan-phase2-tracking-tray.md) §8: a scratch PowerShell script drives the app against a throwaway `--data-dir`, finds controls by AutomationId, and records the results in the commit message. Owned dialogs are looked up under their owner window (Phase 2's implementation note).

1. `--show entries` on a fresh database → an empty grid, *0 entries · Total 0:00*.
2. **New entry…** → editor. Set stop before start → click Save → the blocking error shows and the dialog stays open (parent §10 `EntriesEditorTests`). Fix it, type the customer `Acme` and the type `Research` → Save → one row, and `Research` now exists as a task type.
3. **Overlaps:** a second manual entry overlapping the first → the overlap reason box appears with the warning. Save without a reason → both rows show ⧉. Edit one and add the reason → ⧉ tooltip shows it.
4. **Notes:** double-click a row → change the notes → Save → the grid shows the new first line.
5. **Continue:** select a row → **Continue** → a new running row with the "running" badge and ↪.
6. **Parallel:** start a second timer from the quick panel with *Run in parallel* → both running rows turn amber in the open Entries window, without manual refresh.
7. **Delete** → confirm → the row disappears.
8. **Filters:** switch to *Today*, *Last week* and Custom (to before from → the inline error, no reload). The customer filter lists archived customers with *(archived)*.
9. **Settings** (`--show settings`): rename `Acme` → `acme corp` → the grid shows the new name. Rename to an existing name in a different case → the error stays in the dialog. Archive `Research` → it's gone from the quick-panel picker but still shown on its entries.
10. **Archived customer:** edit an entry whose customer is archived → change only the notes → Save → the customer is **still archived** (§4.1 resolution rule).
11. **Single window:** *All entries…* twice (tray menu or footer) → a single Entries window, activated.

Tray-menu items need a person to click (Phase 2's limitation). The footer links and `--show` cover the same `WindowManager` code.

---

### Implementation notes

- **Rows have accessible names.** WPF exposes an item's view-model `ToString()` as its UI Automation name, which is useless to a screen reader: the Settings list items read as `CatalogItemRow { Id = 1, … }`. The quick-panel rows, the Entries grid rows (`EntryRowViewModel.AccessibleName`) and the Settings list items now set `AutomationProperties.Name` in their container style. The README records this as a convention.
- **Settings tab ids come from the view-model.** `TabControl` renders the selected tab's content outside the `TabItem`, so the shared tab template can't read an id prefix from the tab. `CatalogListViewModel.AutomationPrefix` supplies it instead.
- **UI Automation quirks Phase 8 will hit:**
  - A `DataGrid` column header is listed twice (header and text), so the ↪/⧉ counts include 2 for the header.
  - Win32 `MessageBox` buttons appear as panes with no Invoke pattern to the managed UIA client. The script clicks them with `BM_CLICK` on the button's window handle. FlaUI's UIA3 client may expose them properly; check before copying the workaround.
- **Running rows re-query every 30 s.** The view-model reloads rather than recomputing durations in place. It's simpler, and the query is local and cheap.
- **The filter bar was narrowed** so it fits on one line at the default 1000 px width.

## 8. Deviations from the parent (fold into [plan-time-tracker-v1.md](plan-time-tracker-v1.md))

1. **`CatalogService.GetAllCustomers/GetAllTaskTypes`** are new (parent §3).
2. **The editor keeps the entry's current customer/type id when the text still names it,** rather than resolving through `GetOrCreate…`, so archived items aren't un-archived by unrelated edits (parent §5 says "resolved through `CatalogService` on Save").
3. **An overlap reason is preserved when the reason box is hidden** (no overlaps any more). Parent §5 doesn't say what happens to it.
4. **`WindowManager`** is a new name for the "one instance at most" rule (parent §5), and `Clock.TodayLocal` is a new helper.
5. **Entries grid:** newest first by start (stakeholder decision; the export stays oldest first), and columns aren't user-sortable. Stop shows `(+N)` for a cross-midnight entry. The default preset is *This week*.
6. **Editor time boxes are always 24-hour `HH:mm`.** Dates and the grid follow the Windows culture (parent Non-goals: *"dates and times follow the current Windows culture in the UI"*). The editor's typed times are fixed-format so parsing is unambiguous.

---

## Steps

- [x] Core: `Time/DatePresets.cs`, `Time/LocalTime.cs`, `Clock.TodayLocal` in [Clock.cs](src/NaggingTimeTracker.Core/Time/Clock.cs) (§1.1–1.2)
- [x] Core: `DurationFormat.HoursMinutes` in [DurationFormat.cs](src/NaggingTimeTracker.Core/Formatting/DurationFormat.cs), new `Formatting/EntryText.cs` (§1.3)
- [x] Core: `GetAllCustomers` / `GetAllTaskTypes` in [CatalogService.cs](src/NaggingTimeTracker.Core/Services/CatalogService.cs) (§1.4)
- [x] Core: `ShowTarget.Entries/Settings` + `ParseShow` in [StartupOptions.cs](src/NaggingTimeTracker.Core/Startup/StartupOptions.cs) (§1.5)
- [x] Tests: the four new files and the four changed ones from §7.1
- [x] App: `Windows/WindowManager.cs` (§2); switch `TrayHost.NextLocalMidnight` to `Clock.TodayLocal`
- [x] App: `ViewModels/EntriesViewModel.cs`, `ViewModels/EntryRowViewModel.cs`, `ViewModels/FilterOption.cs`, `Windows/EntriesWindow.xaml(.cs)` (§3)
- [x] App: `ViewModels/EntryEditorViewModel.cs`, `ViewModels/EntryEditorFactory.cs`, `Windows/EntryEditorDialog.xaml(.cs)` (§4)
- [x] App: `ViewModels/SettingsViewModel.cs`, `ViewModels/CatalogListViewModel.cs`, `Windows/SettingsWindow.xaml(.cs)`, `Windows/RenameDialog.xaml(.cs)` (§5)
- [x] App: tray menu items in [TrayHost.cs](src/NaggingTimeTracker.App/Tray/TrayHost.cs), quick-panel footer in [QuickPanelWindow.xaml](src/NaggingTimeTracker.App/Windows/QuickPanelWindow.xaml) + commands in [QuickPanelViewModel.cs](src/NaggingTimeTracker.App/ViewModels/QuickPanelViewModel.cs), `--show` dispatch and DI registrations in [App.xaml.cs](src/NaggingTimeTracker.App/App.xaml.cs) (§6, §3.4)
- [x] `dotnet build` (zero warnings) and `dotnet test --filter "Category!=UI"` (all green)
- [x] Manual verification (§7.2), scripted with UI Automation; record the results in the commit message
- [x] Update [README.md](README.md): the status line (Phases 1–3); the `--show` table (`entries`, `settings`); the Layout row for App
- [x] Tick the parent's Phase 3 checklist in [plan-time-tracker-v1.md](plan-time-tracker-v1.md), fold in the §8 deviations (§3, §5), and flip this doc's Status to Shipped
- [x] Commit "Phase 3 — entries window, editor, catalog settings"

---

## Non-goals

- **No CSV export,** and no *Export…* button in the Entries window yet. That's Phase 4, which also adds `WindowManager.ShowExport` with the filter pre-fill (parent §9).
- **No other Settings tabs** (work hours, check-ins, reminders, idle, general). Those are Phases 5–7.
- **No bulk edit or multi-select delete.** `SelectionMode=Single`.
- **No column sorting, column reordering persistence, or remembered window size/filter.** The preset resets to *This week* each time the window opens.
- **No undo for Delete.** The confirmation is the safeguard; the daily backup arrives in Phase 7.
- **No cross-window "catalog changed" event.** Renames and archives show up in other windows on their next reload (§5.1).
- **No editing of `continued_from_id`.** It's display-only, and only `Continue` sets it.
- **No splitting or merging of entries.**
- **No UI test project** (Phase 8). The AutomationIds are set now.
