# Nagging Time Tracker

A Windows system-tray time tracker that nags you to keep your timesheet honest.

> **Status:** v1 is complete; all 9 phases have shipped:
>
> - **Tray:** start, stop and continue timers, including parallel ones.
> - **Entries window:** review, filter, edit, delete and add entries.
> - **CSV export:** of the filtered entries, from the Entries window or the tray.
> - **Reminders:** nags when you are not tracking during work hours, a periodic "Still working on
>   …?" check-in, and your own scheduled reminders. They're set up in Settings.
> - **Idle detection:** when you come back from being away (no input, locked or asleep) with a
>   timer running, it asks whether to keep, discard and continue, or discard and stop the idle
>   time. It asks the same at startup if the app wasn't running.
> - **Settings:** start with Windows, open the data folder, and rename or archive customers
>   and task types.
> - **Backups:** a copy of the database each day you use the app; the newest 7 are kept in
>   `backups\` next to `tracker.db`.
> - **Automated UI tests** of the main windows (`tests/NaggingTimeTracker.UiTests`).
> - **Installer:** an MSI for just you or all users; see [Install](#install).
>
> The full v1 design lives in
> [plan-time-tracker-v1.md](plan-time-tracker-v1.md) (phases in §12). Each shipped phase has a
> detailed plan: [Phase 1](plan-phase1-scaffold-data.md), [Phase 2](plan-phase2-tracking-tray.md),
> [Phase 3](plan-phase3-entries-editor.md), [Phase 4](plan-phase4-csv-export.md), [Phase 5](plan-phase5-reminders.md),
> [Phase 6](plan-phase6-idle-detection.md),
> [Phase 7](plan-phase7-polish.md), [Phase 8](plan-phase8-ui-tests.md),
> [Phase 9](plan-phase9-installer.md).

## What it does

- **Tracks time from the tray.** Start, stop, continue and edit entries. Each entry has a
  customer, a task type (Admin, Training, Dev Work, Meetings, …), an optional external task
  link, and notes.
- **Runs several timers at once, but only on purpose.** Starting a second timer asks for
  confirmation and an optional reason. The tray icon shows clearly when more than one is running.
- **Nags you:**
  - when nothing is being tracked during your work hours (overnight shifts are supported)
  - with a periodic "Still working on X?" check-in while a timer runs
  - with custom reminders at set times on chosen weekdays

  Nags hold off while you're presenting or using a full-screen app.
- **Detects idle time.** When you come back after being away, it asks whether to keep the idle
  time or discard it.
- **Exports to CSV** for timesheets and invoicing elsewhere.
- **Keeps everything local.** Data is stored in a SQLite database in your Windows profile. There
  are no accounts, no sync and no telemetry.

## Tech

C# / WPF on .NET 10, SQLite via EF Core, and a WiX v5 MSI installer that lets you install
just for yourself (no admin rights) or for all users.

## Install

Download the installer from **https://bspjojo.github.io/nagging-time-tracker/** (or any version
from [Releases](https://github.com/bspjojo/nagging-time-tracker/releases)) and run
`NaggingTimeTracker-<version>-x64.msi`. To build it yourself, see `tools\build-installer.ps1` below.

- **Install** installs just for you: no admin rights, into
  `%LOCALAPPDATA%\Programs\NaggingTimeTracker\`.
- **Advanced** offers *for all users* instead: it asks for admin rights and installs into
  `C:\Program Files\NaggingTimeTracker\`.
- The builds aren't signed yet, so SmartScreen may say *Windows protected your PC*. Choose
  *More info → Run anyway*.
- **Upgrading:** run a newer MSI of the same scope. It closes the running app first; running
  timers are kept. To switch between just you and all users, uninstall first.
- **Your data** is per user, whichever scope you chose: `%LOCALAPPDATA%\NaggingTimeTracker\`
  (`tracker.db`, and `backups\` with the newest 7 daily copies). Uninstalling keeps it; delete
  the folder yourself to remove it.
- **Uninstall** from *Settings → Apps*. It also removes your own *Start with Windows* entry (an
  upgrade keeps it). After an all-users uninstall, other users' *Start with Windows* entries stay
  until they're removed by hand.

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
dotnet test tests/NaggingTimeTracker.UiTests  # UI tests (FlaUI); needs an unlocked desktop
dotnet run --project src/NaggingTimeTracker.App
powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-installer.ps1   # → artifacts\NaggingTimeTracker-<v>-x64.msi
```

`build-installer.ps1` publishes a self-contained single-file exe and builds the MSI with WiX
(restored from NuGet; nothing to install). `-Version 1.0.1` builds another version without
editing the csproj, e.g. to test an upgrade. To check an installer in Windows Sandbox (install,
upgrade while running, downgrade refused, uninstall, all users), build two versions and run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer-checks\run-sandbox.ps1 `
  -Msi artifacts\NaggingTimeTracker-1.0.0-x64.msi -UpgradeMsi artifacts\NaggingTimeTracker-1.0.1-x64.msi
```

It takes 10–15 minutes, opens and closes the Sandbox window on its own, and ends with
`DONE failed=N`. `-Interactive` just opens Sandbox with the MSI mapped in, for clicking
through the installer by hand. It needs Windows Sandbox enabled and `python` on `PATH`.

### Releasing

1. Bump `<Version>` in `src/NaggingTimeTracker.App/NaggingTimeTracker.App.csproj` and commit.
2. Tag that commit and push the tag:

   ```powershell
   git tag v1.0.1
   git push origin v1.0.1
   ```

The **Release** workflow (`.github/workflows/release.yml`) then checks that the tag matches
`<Version>`, runs the unit tests, builds the MSI with `tools\build-installer.ps1` and creates a
GitHub Release with it attached twice: `NaggingTimeTracker-<version>-x64.msi` and
`NaggingTimeTracker-x64.msi`. The download page links to the second through
`releases/latest/download/`, so it always serves the newest release without changing. Run the UI
tests and the Sandbox install check locally before tagging; CI has no desktop for them.

The download page is `site/index.html`, published by the **Pages** workflow
(`.github/workflows/pages.yml`) whenever `site/` changes on `main`. It needs *Settings → Pages →
Source: GitHub Actions*.

The `Category!=UI` filter leaves out the UI tests, so it's safe anywhere. The UI tests drive the
built app's windows on a temp `--data-dir`, one test at a time, and take about a minute. They
need an interactive, unlocked desktop. **Don't touch the mouse or keyboard while they run**: a
click elsewhere hides the quick panel. If the test run itself is killed partway, end any leftover
`NaggingTimeTracker` processes from the build folder in Task Manager. They only ever use temp
data folders.

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
| `--no-background` | No reminders, no idle detection and no startup downtime prompt, for UI tests. |

An unknown switch shows an error box and exits with code 2. A startup failure shows the error
and exits with code 1.

### Layout

| Path | What it is |
|---|---|
| `src/NaggingTimeTracker.Core` | Domain records, services (tracking, catalog, settings), time helpers, repository interfaces. No EF Core, no WPF. |
| `src/NaggingTimeTracker.Data` | EF Core / SQLite: `TrackerDbContext`, entity configurations, repositories, migrations. |
| `src/NaggingTimeTracker.App` | WPF tray app and composition root: tray, quick panel, Entries window, entry editor, Settings window, dialogs, single-instance handling. `Windows/WindowManager.cs` keeps each non-modal window to a single instance. |
| `tools/make-icons.ps1` | Regenerates the tray icons in `src/NaggingTimeTracker.App/Assets/tray/` and the exe's icon `Assets/app.ico` (16–256 px). Run it with Windows PowerShell 5.1 (`powershell -NoProfile -File tools/make-icons.ps1`), then commit the output. |
| `tools/ui-checks/` | Per-phase scripted UI Automation checks (Phases 2–7), run by hand on a desktop before each phase's commit. Not part of `dotnet test`; see [tools/ui-checks/README.md](tools/ui-checks/README.md). |
| `.github/workflows/` | `release.yml` (tag `v*` → GitHub Release with the MSI) and `pages.yml` (publishes `site/`). |
| `site/` | The download page on GitHub Pages: one static `index.html`, plus the app icon as `favicon.ico`. |
| `installer/` | WiX v5 MSI: `Package.wxs`, `License.rtf` (the installer's as-is notice). Not in the solution; built by `tools/build-installer.ps1`. |
| `tools/build-installer.ps1` | Publishes the app (`Properties/PublishProfiles/win-x64.pubxml`) and builds the MSI into `artifacts\` (git-ignored). |
| `tools/installer-checks/` | `run-sandbox.ps1` (host) and `guest-checks.ps1` (runs inside Windows Sandbox): the scripted install checks. |
| `tests/NaggingTimeTracker.Tests` | xUnit tests against a real SQLite file per test (`TestDb.cs`). |
| `tests/NaggingTimeTracker.UiTests` | xUnit + FlaUI tests that launch the built exe (`AppFixture.cs`) and drive the quick panel, Entries window, editor and export. Every class is `[Trait("Category", "UI")]`. |

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
- **Reminder rules are pure Core code; the App only ticks and shows windows.** `ReminderEngine`
  owns its in-memory bookkeeping (last fires, snoozes, check-ins), and the nag buttons call its
  methods. `NagQueue` holds the one-at-a-time, presentation and pruning rules. `ReminderHost`
  runs every 30 s **on the UI thread**, so nothing needs locking. Test reminder behaviour in
  `ReminderEngineTests` / `NagQueueTests` with `FakeTimeProvider`, not by waiting on the app.
- **Idle rules are pure Core code too.** `Core/Idle/IdleTracker.cs` is the away/return state
  machine (lock, sleep, the threshold), `Downtime` the startup check and `IdleResolver` the only
  code that rewrites entries for idle time. `App/Idle/IdleMonitor.cs` just polls
  `GetLastInputInfo` and forwards `SystemEvents`; test idle behaviour in `IdleTrackerTests`.
- **Start with Windows goes through `Core/Startup/RunAtLogin.cs`** behind `IRunKeyStore`
  (`App/Startup/RegistryRunKeyStore.cs` is the HKCU Run key), so its rules are unit-tested. At
  startup a Run value pointing at another exe is repointed at this one, but only by Release
  builds on the default data folder: a Debug build or a `--data-dir` instance never rewrites it
  on its own, so running the repo's build or the UI checks can't take over Windows startup.
- **Backups are `Data/BackupService.cs`**, run once per local day at startup with SQLite's
  online backup API. A failure is logged and retried next start; it never blocks startup.
- **Settings tabs are selected by `SettingsTab` tag, not position**, so a phase can add a tab
  anywhere. The Work hours, Check-ins and Idle tabs save each valid change as soon as a field loses
  focus; there is no Save button.
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
