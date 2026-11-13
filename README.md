# Nagging Time Tracker

A Windows system-tray time tracker that nags you to keep your timesheet honest.

> **Status:** in development. Phase 1 of 9 (project scaffold and data layer) has shipped; there is
> no tray, UI or installer yet. The full v1 design lives in
> [plan-time-tracker-v1.md](plan-time-tracker-v1.md) (phases in §12), and Phase 1's detailed plan
> in [plan-phase1-scaffold-data.md](plan-phase1-scaffold-data.md). This README gains usage and
> install instructions as those phases land.

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

Right now the app is a placeholder. It creates and migrates
`%LOCALAPPDATA%\NaggingTimeTracker\tracker.db`, then exits with code 0. If anything fails, it
shows an error box and exits with code 1. That folder is your real data location, and later
phases keep using it. Delete it if you want to start from an empty database.

### Layout

| Path | What it is |
|---|---|
| `src/NaggingTimeTracker.Core` | Domain records, time helpers, repository interfaces. No EF Core, no WPF. |
| `src/NaggingTimeTracker.Data` | EF Core / SQLite: `TrackerDbContext`, entity configurations, repositories, migrations. |
| `src/NaggingTimeTracker.App` | WPF app and composition root (currently the migrate-and-exit placeholder). |
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
