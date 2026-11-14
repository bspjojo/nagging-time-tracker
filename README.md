# Nagging Time Tracker

A Windows system-tray time tracker that nags you to keep your timesheet honest.

> **Status:** in development. Phases 1–4 of 9 have shipped:
>
> - **Tray:** start, stop and continue timers, including parallel ones.
> - **Entries window:** review, filter, edit, delete and add entries.
> - **CSV export:** of the filtered entries, from the Entries window or the tray.
> - **Settings:** rename or archive customers and task types.
>
> There are no reminders, idle detection or installer yet. The full v1 design lives in
> [plan-time-tracker-v1.md](plan-time-tracker-v1.md) (phases in §12). Each shipped phase has a
> detailed plan: [Phase 1](plan-phase1-scaffold-data.md), [Phase 2](plan-phase2-tracking-tray.md),
> [Phase 3](plan-phase3-entries-editor.md), [Phase 4](plan-phase4-csv-export.md). This README gains usage and install instructions as
> those phases land.

## What v1 will do

- **Track time from the tray.** Start, stop, continue and edit entries. Each entry has a
  customer, a task type (Admin, Training, Dev Work, Meetings, …), an optional external task
  link, and notes.
- **Run several timers at once, but only on purpose.** Starting a second timer asks for
  confirmation and an optional reason. The tray icon shows clearly when more than one is running.
- **Nag you:**
  - when nothing is being tracked during your work hours (overnight shifts are supported)
  - with a periodic "Still working on X?" check-in while a timer runs
  - with custom reminders at set times on chosen weekdays

  Nags hold off while you're presenting or using a full-screen app.
- **Detect idle time.** When you come back after being away, it asks whether to keep the idle
  time or discard it.
- **Export to CSV** for timesheets and invoicing elsewhere.
- **Keep everything local.** Data is stored in a SQLite database in your Windows profile. There
  are no accounts, no sync and no telemetry.

## Tech

C# / WPF on .NET 10, SQLite via EF Core, and a WiX v5 MSI installer that lets you install
just for yourself (no admin rights) or for all users.

## Development

### Prerequisites

- Windows (the App project targets `net10.0-windows`).
- .NET SDK 10.0.400 or a later 10.0.x feature band. [global.json](global.json) pins it, so an
  older or newer major SDK installed alongside it isn't picked up by accident.

Then restore the local tools once, which installs the pinned `dotnet-ef` from
[.config/dotnet-tools.json](.config/dotnet-tools.json):

```powershell
dotnet tool restore
```

### Build, test, run

```powershell
dotnet build                              # zero warnings: warnings are errors (Directory.Build.props)
dotnet test --filter "Category!=UI"       # unit + integration tests
dotnet run --project src/NaggingTimeTracker.App
```

Use the `Category!=UI` filter from now on. The automated UI tests that arrive in Phase 8 need
an interactive desktop and are excluded by it.

The app runs in the notification area; it has no main window.

- **Left-click** the icon to open the quick panel, where you start, stop and continue timers.
- **Right-click** it for the menu: *All entries…*, *Settings…* and **Exit** live there. *All
  entries…* and *Settings…* are also links at the bottom of the quick panel.
- **One instance only:** launching it again just opens the running instance's panel.

Its data lives in `%LOCALAPPDATA%\NaggingTimeTracker\tracker.db`, your real data location.

To try things without touching that data, point it at a scratch folder:

```powershell
dotnet run --project src/NaggingTimeTracker.App -- --data-dir $env:TEMP\ntt-dev --show quick-panel
```

| Switch | Effect |
|---|---|
| `--data-dir <path>` | Use `<path>` instead of `%LOCALAPPDATA%\NaggingTimeTracker`. Each data folder gets its own single-instance lock, so this runs alongside your normal instance. |
| `--show quick-panel` / `entries` / `settings` / `export` | Open that window once startup finishes. |
| `--no-background` | Accepted, but has no effect yet. From Phase 5 it keeps reminders and idle detection off, for UI tests. |

An unknown switch shows an error box and exits with code 2. A startup failure shows the error
and exits with code 1.

### Layout

| Path | What it is |
|---|---|
| `src/NaggingTimeTracker.Core` | Domain records, services (tracking, catalog, settings), time helpers, repository interfaces. No EF Core, no WPF. |
| `src/NaggingTimeTracker.Data` | EF Core / SQLite: `TrackerDbContext`, entity configurations, repositories, migrations. |
| `src/NaggingTimeTracker.App` | WPF tray app and composition root: tray, quick panel, Entries window, entry editor, Settings window, dialogs, single-instance handling. `Windows/WindowManager.cs` keeps each non-modal window to a single instance. |
| `tools/make-icons.ps1` | Regenerates the tray icons in `src/NaggingTimeTracker.App/Assets/tray/`. Run it with Windows PowerShell 5.1 (`powershell -NoProfile -File tools/make-icons.ps1`), then commit the output. |
| `tests/NaggingTimeTracker.Tests` | xUnit tests against a real SQLite file per test (`TestDb.cs`). |

### Database changes

The schema is owned by EF Core migrations in `src/NaggingTimeTracker.Data/Migrations/`. The
app applies them at startup with `Database.Migrate()`. To change the schema, edit the entity
configuration in `src/NaggingTimeTracker.Data/Configurations/`, then add a migration:

```powershell
dotnet ef migrations add <Name> --project src/NaggingTimeTracker.Data --output-dir Migrations
dotnet ef migrations script --project src/NaggingTimeTracker.Data   # review the SQL
```

- Always add a **new** migration. Never edit `InitialCreate` or any other migration that has
  already been committed.
- A model change without a migration fails loudly in two places: `MigrationTests` (via
  `HasPendingModelChanges()`) and app startup (EF Core throws on pending model changes). To
  check by hand, run
  `dotnet ef migrations has-pending-model-changes --project src/NaggingTimeTracker.Data`.
- `dotnet ef` uses a throwaway database in `%TEMP%` (`DesignTimeDbContextFactory`), never your
  real one. That's why `migrations list` shows migrations as "Pending".

### Conventions worth knowing before you change things

- **Timestamps are UTC epoch milliseconds in `INTEGER` columns.** EF's default `DateTimeOffset`
  mapping on SQLite can't translate comparisons or `ORDER BY` to SQL, so `EpochMsConverter`
  replaces it for every `DateTimeOffset` property. Code reads "now" through
  `TimeProvider.NowUtc()` (`Core/Time/Clock.cs`). It truncates to whole milliseconds, so a value
  in memory equals the stored one after a round-trip.
- **Local dates become UTC ranges in one place:** `Core/Time/LocalDateRange.cs`. The "to" date
  is inclusive, and an entry belongs to the local day it started on.
- **Repositories are synchronous** and create a short-lived `DbContext` per call. The SQLite
  driver has no real async I/O, so async would add ceremony without benefit.
- **Tests use real SQLite, not EF's InMemory provider.** The InMemory provider would ignore the
  `CHECK` constraint, the `NOCASE` collation and the foreign-key behaviour the tests cover.
- **Customers and task types are archived, never deleted.** Name matching is case-insensitive
  for ASCII only, because SQLite's `NOCASE` collation doesn't fold other characters.
- **`TrackingService` is the only code that starts, stops or continues entries.** UI code goes
  through `App/Tracking/TrackingActions.cs`, which adds the confirm-and-retry dialog for parallel
  timers and the Discard/Keep prompt after a single stop. Don't call the service directly from a
  window and then re-implement that flow.
- **The Entries and Export windows share one filter bar** (`App/Controls/FilterBar.xaml`,
  `FilterBarViewModel`, `Core/Entries/FilterState.cs`). Change filtering there, not in either
  window. Its control ids are `<Window>.<Control>`, taken from `FilterBarViewModel.AutomationPrefix`.
- **CSV exports escape formula-like text.** A cell starting with `=`, `+`, `-`, `@`, tab or CR gets a
  leading `'` (CsvHelper's `CsvInjection.Escape`), so Excel shows it as text instead of running it.
  Exports are written to `<file>.tmp` and then moved over the target, so a failed export never
  leaves a truncated file.
- **Give list and grid rows an accessible name.** WPF otherwise exposes a row's view-model
  `ToString()` (e.g. `CatalogItemRow { Id = 1, … }`) to screen readers and UI tests. Set
  `AutomationProperties.Name` in the item container style, as the quick panel, Entries grid and
  Settings lists do.
- **Windows that hide on deactivation must wrap their dialogs.** The quick panel hides itself when
  it loses focus, and opening a dialog makes it lose focus. So every dialog it opens runs inside
  `IModalHost.BeginModal()`.
- **Two H.NotifyIcon gotchas, both already handled in `App/Tray/TrayHost.cs`:**
  - `ForceCreate` must be called with `enablesEfficiencyMode: false`. Its default puts the whole
    process into Windows 11 efficiency mode, which throttles timers.
  - Setting `TaskbarIcon.Icon` disposes the previous `Icon`, so icons are loaded fresh on every
    change and never cached.
  - The rich `TrayToolTip` often doesn't open on Windows 11, which shows the native tooltip
    instead. So `ToolTipText` carries the same status lines, fitted to the native 127-character
    limit by `Core/Tray/TrayToolTipText.cs`.
