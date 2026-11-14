# Nagging Time Tracker — v1 Windows Tray App

**Status:** In progress — Phase 1 shipped ([plan-phase1-scaffold-data.md](plan-phase1-scaffold-data.md)); Phase 2 shipped ([plan-phase2-tracking-tray.md](plan-phase2-tracking-tray.md)); Phase 3 shipped ([plan-phase3-entries-editor.md](plan-phase3-entries-editor.md)); Phase 4 shipped ([plan-phase4-csv-export.md](plan-phase4-csv-export.md)); Phase 5 shipped ([plan-phase5-reminders.md](plan-phase5-reminders.md)); Phase 6 shipped ([plan-phase6-idle-detection.md](plan-phase6-idle-detection.md)); Phase 7 shipped ([plan-phase7-polish.md](plan-phase7-polish.md)); Phases 8–9 not started.

The repo is greenfield: the only tracked file is [README.md](README.md) (UTF-8; a short project summary pointing at this plan, which Phase 9 extends with build/install docs), with a single `first commit`. Nothing exists to reuse; this plan defines the whole v1 app.

v1 is a Windows desktop time tracker that lives in the system tray. It records time entries locally, lets you start/stop/continue/edit them, nags you on a configurable schedule, detects idle time, and exports to CSV.

**Decisions already made by the stakeholder (givens, not open questions):**

- **Stack:** C# / WPF on **.NET 10** (SDK 10.0.400 is installed). SQLite for storage.
- **Local data only.** No network calls, no accounts, no telemetry, no sync.
- **Entry fields (minimum):** start date/time, stop date/time, Customer ("Category"), Task type (Admin, Training, Dev Work, Meetings, …), optional external task link, notes.
- **Customer and task type are required on every entry.** Typing a name that doesn't match an existing one creates it on the spot, wherever a picker appears (quick panel, editor). No placeholder customer is seeded.
- **Continue:** any entry can be continued, which creates a *new* entry linked to the original.
- **Concurrency:** more than one timer may run at once, but starting a second one **requires confirmation** and accepts an **optional justification**, stored on the entry. When more than one is running, that must be **easy to see** at a glance (§4.1).
- **Work schedules may cross midnight** (e.g. 22:00–06:00) (§6.1).
- **Installer:** a WiX v5 MSI. The user chooses **just me** (no admin) or **all users** in the installer. Builds are unsigned for now (§11).
- **Automated UI tests** are wanted where practical. They are included for the main windows (§10); the tray icon itself and timing-driven nags are covered by unit tests instead.
- **Reminders (all three kinds):**
  1. *Not tracking during work hours* — nag when no timer is running inside the configured work schedule.
  2. *Periodic check-in while tracking* — "Still working on X?" every N minutes.
  3. *Custom scheduled reminders* — user-defined title/message at set times on set weekdays.
- **Nags wait while you're presenting or a full-screen app is in front**, and show once you leave.
- **Idle detection:** yes. After N minutes with no input while a timer runs, prompt on return.

**Commit granularity:** one commit per phase (§12), in order. Each phase builds, passes tests, and leaves a runnable app. Reminders (Phase 5) and idle detection (Phase 6) come after the core data/tracking/editing/export loop is solid, because they are the riskiest parts (timers, OS events, focus-stealing windows) and the easiest to iterate on separately.

---

## 1. Solution layout

```
NaggingTimeTracker.sln
src/
  NaggingTimeTracker.Core/    net10.0          — models, services, rules, CSV; no WPF, no SQLite
  NaggingTimeTracker.Data/    net10.0          — SQLite repositories + migrations
  NaggingTimeTracker.App/     net10.0-windows  — WPF UI, tray, OS integration, DI composition root
tests/
  NaggingTimeTracker.Tests/   net10.0, xUnit   — references Core + Data; real SQLite on a temp file
  NaggingTimeTracker.UiTests/ net10.0-windows, xUnit + FlaUI — drives the built App exe (§10)
installer/
  NaggingTimeTracker.Installer.wixproj + Package.wxs — WiX v5 MSI (§11)
tools/
  make-icons.ps1              — generates the tray .ico files (§4)
  build-installer.ps1         — publish + build the MSI (§11)
Directory.Build.props         — Nullable=enable, TreatWarningsAsErrors=true, LangVersion=latest
.editorconfig
.gitignore                    — standard VisualStudio template (bin/, obj/, .vs/, *.user)
```

There is **one unit/integration test project**, plus the UI test project (§10). Service tests use the real repositories against a temp database rather than hand-written fakes. That's more faithful to production, and the DB is small enough to stay fast. `tests/NaggingTimeTracker.Tests/TestDb.cs` is an `IDisposable` fixture: it creates `%TEMP%\ntt-tests\<guid>.db`, runs `Database.Migrate()`, exposes an `IDbContextFactory<TrackerDbContext>`, and deletes the file on dispose (calling `SqliteConnection.ClearAllPools()` first so the file isn't locked). The EF Core InMemory provider is **not** used, because it ignores the `CHECK` constraint, collation and FK behaviour that the tests need to cover.

**Tooling:** `.config/dotnet-tools.json` (a local tool manifest) pins `dotnet-ef` to the same major version as the EF Core packages. Contributors run `dotnet tool restore`.

**NuGet packages** (latest stable at implementation time, pinned in each csproj):

| Package | Project | Purpose |
|---|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite` | Data | EF Core + SQLite provider (brings `Microsoft.Data.Sqlite` transitively, used for backups) |
| `Microsoft.EntityFrameworkCore.Design` | Data (`PrivateAssets=all`) | Design-time support for `dotnet ef migrations` |
| `CsvHelper` | Core | CSV writing (correct quoting of commas, quotes and newlines in notes) |
| `H.NotifyIcon.Wpf` | App | Tray icon + context menu |
| `CommunityToolkit.Mvvm` | App | `ObservableObject`, `RelayCommand`, source generators |
| `Microsoft.Extensions.Hosting` | App | DI container + `IHostedService` lifetime for background services |
| `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Microsoft.Extensions.TimeProvider.Testing` | Tests | Test framework + `FakeTimeProvider` |
| `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FlaUI.UIA3` | UiTests | UI Automation driver for WPF |
| `WixToolset.Sdk` (MSBuild SDK, v5), `WixToolset.UI.wixext`, `WixToolset.Util.wixext` | Installer | MSI build, install-scope dialog, close-running-app |

**App identity:** the App csproj sets `<AssemblyName>NaggingTimeTracker</AssemblyName>` (so the exe is `NaggingTimeTracker.exe`) and `<Version>` (e.g. `1.0.0`). The installer's `ProductVersion` reads that version.

**Automation IDs:** every interactive control in every window sets `AutomationProperties.AutomationId="<Window>.<Control>"`, e.g. `QuickPanel.CustomerCombo` or `QuickPanel.StartButton`. This applies from Phase 2 onward, so the UI tests (§10) never need to search by display text.

**Command-line switches** (`Core/Startup/StartupOptions.cs`, parsed in `App.OnStartup`; mainly for the UI tests, all optional). The parser and the single-instance name derivation (`Core/Startup/InstanceNames.cs`) live in Core so the `net10.0` test project can test them:

- `--data-dir <path>` — overrides the `%LOCALAPPDATA%\NaggingTimeTracker` root used by `DataPaths`. It also suffixes the single-instance names (§4) with the first 8 hex chars of a SHA-256 of the full path, so a test instance never collides with the user's real one.
- `--show quick-panel|entries|settings|export` — opens that window once startup finishes.
- `--no-background` — doesn't start `ReminderHost` or `IdleMonitor`, so nags and idle prompts can't interfere with a test.

**Time abstraction:** all services take `TimeProvider` and never call `DateTime.Now`/`UtcNow`. UTC↔local conversion always goes through `TimeProvider.LocalTimeZone`, so tests can pin a timezone (including a DST one) with `FakeTimeProvider.SetLocalTimeZone`.

**Host wiring:** `App.OnStartup` builds a Generic Host (`Host.CreateApplicationBuilder`), calls `AddTrackerData(...)`, registers every service as a singleton (and every window transient), then runs, in this order: `DatabaseInitializer` (migrate + WAL, §2) → `BackupService` (§2; a failure is logged and startup continues) → Run-key self-heal (§8; Release builds on the default data folder only) → `StartupDowntimeCheck` (§7.3, from Phase 6; skipped under `--no-background`) → `host.StartAsync()` → the `--show` window, if any. `App.OnExit` calls `host.StopAsync()`.

---

## 2. Data storage

**Location:** `%LOCALAPPDATA%\NaggingTimeTracker\tracker.db`, resolved with `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)`. The directory is created on first run.

**Time convention:** every timestamp is stored as an **`INTEGER` of Unix epoch milliseconds (UTC)**: `DateTimeOffset.ToUnixTimeMilliseconds()` to write and `DateTimeOffset.FromUnixTimeMilliseconds()` to read, which always gives offset `+00:00`. Comparisons, the `CHECK` constraint and range queries are plain integer comparisons. Values stay inspectable in ad-hoc SQL, e.g. `SELECT datetime(start_utc / 1000, 'unixepoch', 'localtime') FROM entries;`. Local time appears only in the UI and the CSV export.

**Millisecond precision:** storage truncates anything finer than 1 ms. To make an in-memory value equal its stored round-trip, every time Core reads "now" it goes through `Core/Time/Clock.cs`, `NowUtc(this TimeProvider tp)`, which returns `GetUtcNow()` truncated to whole milliseconds. Services never call `GetUtcNow()` directly.

**Why a converter and not EF's default `DateTimeOffset` mapping:** the EF Core SQLite provider can't translate `DateTimeOffset` comparisons or `ORDER BY` to SQL, so it would throw or evaluate them on the client. Instead, every `DateTimeOffset`/`DateTimeOffset?` entity property is mapped through `Data/Converters/EpochMsConverter.cs`, a `ValueConverter<DateTimeOffset, long>`. It is applied globally in `TrackerDbContext.ConfigureConventions` via `configurationBuilder.Properties<DateTimeOffset>().HaveConversion<EpochMsConverter>()`, and the same for `DateTimeOffset?`. EF then compares the `long` values in SQL.

**DbContext** (`Data/TrackerDbContext.cs`): `DbSet`s for `Customers`, `TaskTypes`, `Entries`, `Reminders` and `Settings`. Mapping lives in `Data/Configurations/*Configuration.cs` (`IEntityTypeConfiguration<T>`), which use snake_case table and column names exactly as in the schema below. The Core domain records (§12 Phase 1) stay free of EF attributes. The Data project maps them directly as entities. Navigation properties aren't needed; only foreign-key ids are used. The `settings` table is the one exception: it maps a Data-internal `Data/Entities/SettingRow.cs` (`Key`, `Value`), since Core only sees it through `ISettingsStore`. `ScheduledReminder.TimeLocal` is a `TimeOnly`, mapped in `ReminderConfiguration` through a `ValueConverter<TimeOnly, string>` that uses the format `"HH:mm"` with `CultureInfo.InvariantCulture`.

**Registration and lifetime:** `services.AddDbContextFactory<TrackerDbContext>(o => o.UseSqlite("Data Source=<path>;Foreign Keys=True"))`. The services are singletons living in a long-running WPF process, so repositories inject `IDbContextFactory<TrackerDbContext>` and create a short-lived context per operation (`await using var db = factory.CreateDbContext();`). No context is held across calls.

**WAL:** after `Migrate()` at startup, run `PRAGMA journal_mode=WAL;` once via `Database.ExecuteSqlRaw`. The setting persists in the database file.

### Schema

This is the schema the generated `InitialCreate` migration must produce. Check it with `dotnet ef migrations script`. It's shown as SQL for clarity, but it is produced by the entity configurations, not hand-written:

- `COLLATE NOCASE` → `.UseCollation("NOCASE")` on the `Name` properties, with `HasIndex(x => x.Name).IsUnique()`.
- `CHECK` → `ToTable(t => t.HasCheckConstraint("ck_entries_stop_after_start", "stop_utc IS NULL OR stop_utc > start_utc"))`.
- Partial index → `HasIndex(e => e.StopUtc).HasFilter("stop_utc IS NULL")`.
- Self-reference → `HasOne<Entry>().WithMany().HasForeignKey(e => e.ContinuedFromId).OnDelete(DeleteBehavior.SetNull)`.
- `customer_id` / `task_type_id` FKs → `OnDelete(DeleteBehavior.Restrict)` (customers and task types are archived, never deleted).

**Differences in EF's actual output (accepted; see [plan-phase1-scaffold-data.md](plan-phase1-scaffold-data.md) §5):** every integer primary key is `AUTOINCREMENT`, so ids are never reused; name uniqueness is enforced by unique indexes `ux_customers_name` / `ux_task_types_name`, not inline `UNIQUE`; there are no `DEFAULT` clauses, because EF writes every column on insert, so ad-hoc SQL inserts must supply `archived`, `enabled` and `notes`; the `Restrict` FKs emit `ON DELETE RESTRICT`; and EF adds indexes on the three FK columns.

```sql
CREATE TABLE customers (
  id          INTEGER PRIMARY KEY,
  name        TEXT NOT NULL COLLATE NOCASE UNIQUE,
  archived    INTEGER NOT NULL DEFAULT 0,
  created_utc INTEGER NOT NULL
);

CREATE TABLE task_types (
  id          INTEGER PRIMARY KEY,
  name        TEXT NOT NULL COLLATE NOCASE UNIQUE,
  archived    INTEGER NOT NULL DEFAULT 0,
  created_utc INTEGER NOT NULL
);

CREATE TABLE entries (
  id                INTEGER PRIMARY KEY,
  customer_id       INTEGER NOT NULL REFERENCES customers(id),
  task_type_id      INTEGER NOT NULL REFERENCES task_types(id),
  start_utc         INTEGER NOT NULL,
  stop_utc          INTEGER NULL,         -- NULL = currently running
  external_link     TEXT NULL,
  notes             TEXT NOT NULL DEFAULT '',
  continued_from_id INTEGER NULL REFERENCES entries(id) ON DELETE SET NULL,
  overlap_reason    TEXT NULL,            -- optional justification when run alongside another
  created_utc       INTEGER NOT NULL,
  updated_utc       INTEGER NOT NULL,
  CHECK (stop_utc IS NULL OR stop_utc > start_utc)
);
CREATE INDEX ix_entries_start   ON entries(start_utc);
CREATE INDEX ix_entries_running ON entries(stop_utc) WHERE stop_utc IS NULL;

CREATE TABLE reminders (               -- custom scheduled reminders only (§6.3)
  id             INTEGER PRIMARY KEY,
  title          TEXT NOT NULL,
  message        TEXT NOT NULL DEFAULT '',
  days_mask      INTEGER NOT NULL,     -- bit n = (DayOfWeek)n, i.e. bit 0 = Sunday … bit 6 = Saturday
  time_local     TEXT NOT NULL,        -- 'HH:mm', local wall-clock time
  enabled        INTEGER NOT NULL DEFAULT 1,
  last_fired_utc INTEGER NULL
);

CREATE TABLE settings (
  key   TEXT PRIMARY KEY,
  value TEXT NOT NULL
);
```

**Seed data:** `TaskTypeConfiguration` uses `HasData` to seed ids 1–5 (`Admin`, `Training`, `Dev Work`, `Meetings`, `Support`), each with the constant `CreatedUtc = DateTimeOffset.FromUnixTimeMilliseconds(1767225600000)` (2026-01-01T00:00:00Z). `HasData` needs deterministic values, so it can't use "now". User-added task types get ids from 6 upward, because SQLite `INTEGER PRIMARY KEY` continues from the max id.

Customers and task types are **archived, never deleted**: archiving hides them from pickers, but historical entries still resolve their names.

### Migrations

EF Core migrations live in `Data/Migrations/` and are generated with `dotnet ef migrations add <Name> --project src/NaggingTimeTracker.Data`. `Data/DesignTimeDbContextFactory.cs` (`IDesignTimeDbContextFactory<TrackerDbContext>`) points at a throwaway `design-time.db`, so the tool never needs the App project or the real user database. At startup, `App` calls `db.Database.Migrate()` before anything else touches the DB. `EnsureCreated()` is never used, because it bypasses migrations.

Schema changes after v1 are always a new migration, never an edit to `InitialCreate`.

### Backups

`Data/BackupService.cs` (`BackupService(dbPath, backupDir, ISettingsStore, TimeProvider)`, `RunIfDue()`): on startup, if `settings['last_backup_date']` isn't today's **local** date, it copies the DB with `SqliteConnection.BackupDatabase` (plain `Microsoft.Data.Sqlite` connections outside EF, `Pooling=False`) to `backups\tracker-yyyy-MM-dd.db.tmp`, then moves it over `tracker-yyyy-MM-dd.db`, so a crash never leaves a truncated backup. It then deletes all but the newest 7 files named exactly `tracker-yyyy-MM-dd.db` (other files in the folder are never touched) and sets `last_backup_date`. It runs under `--no-background` too. A failure throws; `App.OnStartup` logs it and carries on, and the next start retries. As built: [plan-phase7-polish.md](plan-phase7-polish.md) §1.

### Repositories

The interfaces live in `Core/Abstractions/`, so Core and its services never reference EF Core. Their implementations live in `Data/Repositories/`; they are LINQ over `TrackerDbContext`, use `AsNoTracking()` for reads, and return Core records:

- `ICustomerRepository`, `ITaskTypeRepository` — `GetActive()` (non-archived, by name), `GetAll()`, `FindByName(name)` (case-insensitive), `Insert`, `Rename`, `SetArchived`.
- `IEntryRepository`:
  - `GetRunning()`
  - `GetById(id)`
  - `GetLatestStarted()` — the most recently **started** entry, running or stopped (ties broken by the higher id). Used for the start form's defaults (§3).
  - `GetRecentStopped(n)` — the `n` stopped entries with the latest `stop_utc`, newest first.
  - `GetRecentDistinct(n)` — for the quick panel's Recent section (§4.2): the newest stopped entry of each distinct `(customer_id, task_type_id, external_link)` combination, newest `stop_utc` first, at most `n`. Implemented as: load the 200 most recent stopped entries ordered by `stop_utc DESC`, de-duplicate in memory by that key keeping the first, then take `n`.
  - `Query(EntryFilter)` — `EntryFilter` holds `FromUtc`/`ToUtc` (half-open range on `start_utc`), optional customer id, optional task type id, and optional text (`LIKE` on `notes` or `external_link`). Callers convert local-date presets to UTC day boundaries with `TimeProvider.LocalTimeZone`. A UI "to" date is **inclusive**, so `ToUtc` is the start of the local day *after* it. An entry belongs to the local day it **started** on, even if it spans midnight. This logic lives in one helper, `Core/Time/LocalDateRange.cs`, `ToUtcRange(DateOnly from, DateOnly toInclusive, TimeZoneInfo tz) → (FromUtc, ToUtc)`, which both the Entries and Export windows use.
  - `QueryViews(EntryFilter)` — the same filter as `Query`, projected to `EntryView` (the entry plus its customer and task type names, including archived ones), ordered by `start_utc`. Used by the Entries grid and the CSV export (§9).
  - `GetOverlapping(startUtc, stopUtc?, excludeId?, nowUtc)` — rows where `e.StartUtc < (stopUtc ?? nowUtc) && (e.StopUtc ?? nowUtc) > startUtc` (EF translates `??` to `COALESCE`), excluding `excludeId`. A running entry, or a null `stopUtc` (the editor on a running entry), is treated as ending now. Entries that only touch at a boundary don't overlap.
  - `GetOverlappingIds(fromUtc, toUtc, nowUtc)` — the ids of entries starting in the range that overlap any other entry (same overlap predicate as `GetOverlapping`, as a self-join).
  - `Insert`, `Update`, `UpdateNotes(id, notes, updatedUtc)` (writes only those two columns), `Delete`
- `IReminderRepository` — CRUD + `SetLastFired(id, utc)`.
- `ISettingsStore` — `Get(key)`, `Set(key, value)`.

---

## 3. Core domain and tracking service

`Core/Services/TrackingService.cs` is the **only** code that starts, stops or continues entries. The tray, windows, nags and the idle resolver all call it.

**Concurrency rule:** a start or continue **requires confirmation exactly when it would increase the number of running entries** and at least one entry is already running. A caller that has confirmed passes `confirmedConcurrent: true` (and an optional `overlapReason`).

| Method | Behaviour |
|---|---|
| `Start(StartRequest req)` | `StartRequest(customerId, taskTypeId, externalLink?, notes, confirmedConcurrent = false, overlapReason = null)`. If confirmation is needed and not given, returns `StartResult.NeedsConfirmation(running)` and inserts nothing. Otherwise inserts an entry with `start_utc = now`, `stop_utc = NULL`, `external_link` trimmed (blank → `NULL`), and `overlap_reason = req.overlapReason` (only when other entries are running; otherwise `NULL`). Returns `StartResult.Started(entry)`. |
| `Stop(entryId)` | Sets `stop_utc = max(now, start + 1 s)` (the clamp protects the `CHECK` against clock changes). Returns `StopResult { Entry, TooShort }`, with `TooShort = duration < 1 min`. The UI offers **Discard** (→ `Delete`) or **Keep** only after an explicit **single** stop: a quick-panel row's **Stop**, or the check-in nag's **Stop** / **Stop & add note** / **Switch…**. Bulk stops (`StopAll`, `StopAllExcept`, "Stop others & start", idle resolution) ignore `TooShort` and keep the entries. |
| `StopAll()` | `Stop` on every running entry; returns the results. |
| `StopAllExcept(entryId)` | `Stop` on every running entry except `entryId` (the quick panel's **Keep only this**, §4.1); returns the results. |
| `UpdateNotes(entryId, notes)` | Writes only `notes` and `updated_utc` via `IEntryRepository.UpdateNotes`, so a stale in-memory copy can never overwrite `stop_utc` (the quick panel's inline notes box uses it). |
| `StopAt(entryId, stopUtc)` | Throws `ArgumentOutOfRangeException` if `stopUtc ≤ start_utc`. Used by idle resolution (§7). |
| `Continue(entryId, confirmedConcurrent = false, overlapReason = null)` | Creates a new entry copying `customer_id`, `task_type_id` and `external_link` from the source, with `notes = ''` and `continued_from_id = source.id`. **If the source is running**, it is stopped first, the running count doesn't grow, so no confirmation is needed, and the source's `overlap_reason` is copied (when `overlapReason` is null). **If the source is stopped**, the concurrency rule above applies. Same `StartResult` return type. |
| `ContinueLast()` | `Continue` on `GetRecentStopped(1)`; returns `StartResult.NothingToContinue` if there are no stopped entries. |
| `Delete(entryId)` | Deletes the entry (`continued_from_id` references become `NULL` via FK). |
| `Save(Entry)` | Used by the editor (§5). Throws `ArgumentException` unless `stop > start` when stop is set. Throws `InvalidOperationException` when a new entry (id 0) has no stop, or when an existing entry's stop is null but the stored row is already stopped (it was stopped elsewhere while the editor was open). Trims `external_link` (blank → `NULL`), sets `updated_utc`, then updates or inserts. |

**Return shapes** (settled in [plan-phase2-tracking-tray.md](plan-phase2-tracking-tray.md) §2.2):
- `Stop` returns `StopResult?`: `null` for a missing id, and a no-op (no write, no event) for an already-stopped entry.
- `StopAt` returns `Entry?`: `null` if the entry is missing or not running.
- `UpdateNotes` and `Delete` return `bool`.
- `ContinueLast` takes the same `confirmedConcurrent` / `overlapReason` parameters as `Continue`.

Every method that changed a row raises `RunningEntriesChanged`. Bulk stops raise it once; no-ops don't raise it. The tray, the quick panel and the reminder engine subscribe to it.

`Core/Services/CatalogService.cs` — `GetOrCreateCustomer(name)` and `GetOrCreateTaskType(name)`. They trim the name and reject empty input with `ArgumentException`. A case-insensitive match is returned (un-archiving it if it was archived); otherwise a new row is inserted. Every picker (§4, §5) resolves its typed text through these at submit time, so typing a new customer name simply creates it. For the filters and Settings lists it has `GetAllCustomers()` / `GetAllTaskTypes()` (archived included, by name). For the Settings lists (§5) it also has `RenameCustomer(id, newName)` / `RenameTaskType(id, newName)`, which trim, reject empty input, and return `RenameResult.NameTaken` when another row already has that name (case-insensitive) instead of letting the unique index throw. It also has `SetCustomerArchived(id, bool)` / `SetTaskTypeArchived(id, bool)`.

**Defaults when starting:** the start form pre-fills customer and task type from the most recently **started** entry. On a fresh DB both fields start empty.

---

## 4. Tray icon and quick panel

`App/Tray/TrayHost.cs` owns an `H.NotifyIcon` `TaskbarIcon` and is created at startup. There is **no main window**: the app is tray-only, and `ShutdownMode = OnExplicitShutdown`.

### 4.1 Icon, tooltip and multiple-timer visibility

**Icons** are 16 + 32 px filled circles, generated by `tools/make-icons.ps1` (using `System.Drawing` in Windows PowerShell 5.1) into `App/Assets/tray/`, with the outputs committed. The base colour shows state, and a white bold **count digit** is drawn on the circle whenever 2 or more timers are running:

| File | When | Look |
|---|---|---|
| `idle.ico` | 0 running, no nag | grey |
| `tracking-1.ico` | 1 running | green, no digit |
| `tracking-2.ico` … `tracking-9.ico`, `tracking-9plus.ico` | ≥2 running | **amber**, digit `2`–`9` or `9+` |
| `attention-0.ico`, `attention-1.ico` | a nag is visible or queued, 0–1 running | red, no digit |
| `attention-2.ico` … `attention-9.ico`, `attention-9plus.ico` | a nag is visible or queued, ≥2 running | red, digit |

`TrayHost.UpdateIcon()` picks the file via `Core/Tray/TrayIconSelector.Select(hasAttention, runningCount)` and runs on `RunningEntriesChanged` and on nag queue changes. Amber is only ever used for "multiple running", so it can't be mistaken for anything else.

**Tooltip:** a WPF `TrayToolTip` (H.NotifyIcon's rich tooltip, which avoids the 128-char limit of the native tooltip). It shows:

- 0 running: *Not tracking*
- 1 running: *Acme · Dev Work — 1h 23m* (customer first, as everywhere else)
- ≥2 running: a bold amber header *"⚠ 2 timers running in parallel"*, then one line per running entry (`customer · type — elapsed`)

It is refreshed by a 30 s `DispatcherTimer` and on `RunningEntriesChanged`. The native `ToolTipText` carries the same lines, fitted to its 127-character limit (`Core/Tray/TrayToolTipText.cs`). Windows 11 often shows the native tooltip instead of the rich one.

**Elsewhere when ≥2 are running:**

- **Quick panel:** the Running section (below) gets an amber banner, *"⚠ N timers running in parallel"*. Each row shows its `overlap_reason` (if any) in small text, and gains a **Keep only this** button that stops every other running entry.
- **Entries window:** running rows get an amber row background when more than one is running. A ⧉ column marks rows that overlap another entry (§5).
- **Check-in nag (§6.2):** it always lists *all* running entries, with the due ones first, so the parallel timers are visible together.

### 4.2 Quick panel, menu and dialogs

**Left-click → Quick panel** (`App/Windows/QuickPanelWindow.xaml`): a regular borderless, `Topmost`, `ShowInTaskbar=false` WPF window, positioned at the bottom-right of `SystemParameters.WorkArea`. It hides itself on `Deactivated`, except while it has a dialog open: every `ShowDialog`/`MessageBox.Show` it makes (concurrency dialog, TooShort prompt, Required error) goes through a helper that increments a `_modalDepth` counter around the call, and the `Deactivated` handler skips hiding while `_modalDepth > 0`. Before hiding, the `Deactivated` handler flushes any edited Running-row notes box (see below). Because it is a normal window rather than an H.NotifyIcon popup, the nag window, the single-instance signal and the context menu can all open it with `TrayHost.ShowQuickPanel()`. Its sections:

1. **Running** — one row per running entry: customer · type · live elapsed time, a notes textbox (saved via `TrackingService.UpdateNotes` on `LostKeyboardFocus` and on the panel's `Deactivated`, only if the text changed), and a **Stop** button (shows the TooShort prompt if applicable, §3). With ≥2 rows, the section also shows the banner and the **Keep only this** buttons from §4.1.
2. **Start new** — an editable `ComboBox` (`IsEditable=true`, `IsTextSearchEnabled=true`) for Customer and another for Task type, both listing active items; an External link textbox; a Notes textbox; and a **Start** button (also Enter). On Start, both texts go through `CatalogService.GetOrCreate…`. If either is empty, show an inline "Required" error and don't start.
3. **Recent** — `GetRecentDistinct(5)` (§2), so repeatedly continued work appears once. Each row shows customer · type (· link, if set) and has a **Continue** button that continues that row's entry.
4. Footer links: *All entries…*, *Export…*, *Settings…*.

**Right-click → context menu:** Start new… (→ `ShowQuickPanel`) · Stop all · Continue last · *separator* · Pause reminders ▸ (1 hour → now + 1 h / Until tomorrow → next local midnight / Resume → clears it; all write `reminders_paused_until_utc`) · *separator* · All entries… · Export CSV… · Settings… · *separator* · Exit.

**Concurrency confirmation** (`App/Windows/ConcurrencyDialog.xaml`, shown whenever `Start`/`Continue` returns `NeedsConfirmation`): a modal dialog listing the running entries, with three buttons:

- **Stop others & start** — `StopAll()` (no TooShort prompt, §3), then retries the call with `confirmedConcurrent: true` (nothing is running at that point, so it doesn't matter). When the call being retried is `Continue` on a *running* source, that source isn't running any more after `StopAll()`, so the continue just goes ahead from the stopped source.
- **Run in parallel** — an optional "Why are these overlapping?" textbox is always visible; this retries with `confirmedConcurrent: true` and `overlapReason` set to the trimmed text, or `null` if blank.
- **Cancel**.

**Exit** with timers running asks: *"N timers are still running. They will keep counting while the app is closed. Exit anyway?"* Running entries persist (`stop_utc IS NULL`) and resume on next launch, where the downtime is checked (§7.3). Windows logoff/shutdown closes the app without prompting; the same downtime check covers it.

**Single instance** (`App/SingleInstance.cs`): a named `Mutex` `Local\NaggingTimeTracker` (suffixed under `--data-dir`, §1). If the mutex is already held, the new process sets the named `EventWaitHandle` `Local\NaggingTimeTracker.Show` and exits. The first instance waits on that handle on a background thread and calls `ShowQuickPanel()` on the dispatcher.

---

## 5. Entries window and editor

**`App/Windows/EntriesWindow.xaml`:** non-modal, one instance at most (re-activated if already open, via `App/Windows/WindowManager.cs`). Rows are newest first by start; columns aren't user-sortable; the default preset is *This week*; Stop shows `(+N)` for an entry that ends on a later local day. The editor's time boxes are 24-hour `HH:mm`.

- Filter bar: date range (presets Today / This week (Mon-start) / Last week / This month / Custom from–to dates, the "to" date inclusive), Customer (including archived), Task type (including archived), a text search box. These map to `EntryFilter` via `LocalDateRange.ToUtcRange` (§2). Rows come from `IEntryRepository.QueryViews`.
- `DataGrid` columns: Date, Start, Stop (shows a "running" badge if open), Duration (`h:mm`, running entries measured to now), Customer, Task type, Link (clickable; opens with `Process.Start(new ProcessStartInfo(link) { UseShellExecute = true })` only if it parses as an absolute `http`/`https` `Uri`, otherwise shown as plain text), Notes (first line), ↪ (marker if `continued_from_id` is set), ⧉ (marker if the entry overlaps another, via a single `IEntryRepository.GetOverlappingIds(FromUtc, ToUtc, nowUtc)` self-join query over the filtered range, not one query per row; tooltip shows `overlap_reason`).
- Footer: total duration of the filtered rows.
- Row actions: double-click / **Edit**, **Continue**, **Delete** (with confirmation), **New entry…** (manual entry for untracked time). The grid refreshes on `RunningEntriesChanged` and after any dialog closes.

**`App/Windows/EntryEditorDialog.xaml`** (modal), used for both edit and manual create:

- Start date + time and Stop date + time (WPF `DatePicker` + an `HH:mm` textbox for each, interpreted as local time). Stop may be empty only when the entry being edited is currently running. New manual entries require a stop.
- Customer and Task type: the same editable combo boxes as the quick panel. On Save, text that still names the entry's current customer/type keeps that id; other text is resolved through `CatalogService.GetOrCreate…`. This way an unrelated edit never un-archives an archived customer or type.
- External link: free text. If non-empty and not an absolute `http`/`https` URI, show a non-blocking hint ("Not a URL — saved as plain text"). Ticket IDs like `JIRA-123` are fine.
- Notes: multiline.
- A read-only *"Continued from: <date> <customer> · <type>"* link when `continued_from_id` is set; clicking it opens that entry in a second editor.
- Overlap reason: a stored reason is kept when the box is hidden (no overlaps any more). The box is shown only when `GetOverlapping(start, stop, excludeId: entry.Id, now)` returns rows for the current start/stop (an empty stop counts as now). It is re-queried whenever a start/stop field changes and parses.
- **Validation:** stop must be after start (blocking error). If overlaps exist and the overlap reason is empty, show a non-blocking warning *"Overlaps N other entries"*. Saving is still allowed, matching the "allowed but confirmed" concurrency rule.
- Save goes through `TrackingService.Save`.

**Manage lists:** the Settings window (§8) has *Customers* and *Task types* tabs, each a list with **Rename** and **Archive/Unarchive**, calling `CatalogService.Rename…` / `Set…Archived` (§3). A rename that returns `NameTaken` is rejected with the message *"A customer/task type named '<name>' already exists."*

---

## 6. Reminders

### 6.0 Engine

`Core/Reminders/ReminderEngine.cs` (pure logic) exposes `Evaluate(ReminderState state) → IReadOnlyList<Nag>`. As built ([plan-phase5-reminders.md](plan-phase5-reminders.md) §2): the engine is a singleton **class that owns the in-memory bookkeeping** and has action methods (`SnoozeNotTracking`, `CheckedIn`, `SnoozeCheckIns`, `SnoozeScheduled`), so `ReminderState` carries only the inputs below, not the bookkeeping. `Core/Reminders/NagQueue.cs` holds the queue, presentation-hold and pruning rules; `App/Reminders/IIdleState.cs` (`IsUserIdle`, `IsPromptOpen`) is the seam idle detection (§7) plugs into; `ReminderHost` passes `nags.HasPending || idle.IsPromptOpen` as `IsPromptOpen`. `ReminderState` carries:

- the time
- `AppSettings`
- the running entries
- the enabled scheduled reminders
- in-memory per-rule bookkeeping: last fired, snoozed-until, last check-in per entry id
- `IsUserIdle`
- `IsPresentationBusy`
- `IsPromptOpen`

`Evaluate` returns the nags due and mutates the bookkeeping. The bookkeeping lives in memory and resets on restart; the exceptions are `reminders.last_fired_utc` and the `*_until_utc` settings, which persist.

`App/Reminders/ReminderHost.cs` (`IHostedService`) runs a `PeriodicTimer` every **30 s**. Each tick runs **on the UI thread** (awaited `Dispatcher.InvokeAsync`, so ticks never overlap): it builds the state, calls `Evaluate`, and hands resulting nags to `NagWindowManager`. A failed tick is logged to `Debug` and skipped. `ReminderHost` isn't registered under `--no-background`. For each scheduled-reminder nag returned (`Nag.ReminderId` set), it calls `IReminderRepository.SetLastFired(id, now)`, because the engine itself doesn't touch storage. It also writes the heartbeat (§7.3) once per minute.

**Suppression, in order:**

1. `reminders_paused_until_utc > now` → return nothing.
2. `IsPromptOpen` (a nag is visible **or queued** in `NagWindowManager`, or the idle prompt is open) → return nothing, so bookkeeping doesn't advance and nothing is lost. Counting queued nags means a long presentation holds one pending nag, not a pile of repeats. Anything that falls due meanwhile is produced once the queue drains (a scheduled reminder then gets its "missed at" suffix, §6.3).
3. `IsUserIdle` (the user has been idle for at least the idle threshold, §7) → return nothing. There's no point nagging an empty desk.
4. `IsPresentationBusy` → nags are still produced but held in `NagWindowManager`'s queue, which only shows once not busy. The tray icon still switches to the `attention-*` set.

`IsPresentationBusy` comes from `SHQueryUserNotificationState` (shell32 P/Invoke). It is true for `QUNS_BUSY`, `QUNS_RUNNING_D3D_FULL_SCREEN` and `QUNS_PRESENTATION_MODE`. `QUNS_APP` is deliberately excluded, because it also reports ordinary Store apps such as Settings. `NagWindowManager` re-checks it every 30 s while its queue is non-empty.

### 6.1 Not tracking during work hours

- Settings: work days (default Mon–Fri), work start/end (default 09:00–17:00 local; start ≠ end, otherwise rejected in Settings), interval (default 15 min), enabled (default on).
- **Shifts** (`Core/Reminders/WorkSchedule.cs`, `CurrentShift(nowLocal) → (startUtc, endUtc)?`):
  - A shift begins on each work day D at `work_start`.
  - It ends at `work_end` on D if `work_end > work_start` (day shift), otherwise at `work_end` on D+1 (**overnight shift**, e.g. Fri 22:00 → Sat 06:00).
  - The work-day checkboxes refer to the day a shift **starts**.
  - To find the current shift, check the shifts that start yesterday and today, in that order, and return the first that contains now.
  - Local→UTC conversion uses `TimeProvider.LocalTimeZone`. A local start/end time that falls in a DST gap moves forward to the first valid instant; an ambiguous time (DST overlap) uses the earlier instant.
- Fires when **all** hold:
  - the rule is enabled;
  - `not_working_until_utc` is unset or past, and the rule isn't snoozed;
  - `CurrentShift(now)` is not null;
  - zero entries are running;
  - at least `interval` has passed since the latest of: this rule's last fire, the most recent stop of any entry, and the current shift's start.
- Message: *"You're not tracking anything."* Actions: **Start…** (`ShowQuickPanel`), **Continue <last entry's customer · type>** (`ContinueLast`; hidden if there are no entries), **Snooze 15 / 30 / 60 min**, **Not working this shift** (sets `not_working_until_utc` to the current shift's end).

### 6.2 Periodic check-in while tracking

- Settings: interval (default 60 min), enabled (default on).
- Per running entry: due when `now - max(entry.start_utc, lastCheckIn[entry.id] ?? appStartTime) ≥ interval`, where `appStartTime` is when the process started. That avoids a burst of check-ins after a restart.
- All entries due on the same tick are combined into one nag. When ≥2 entries are running, the nag lists every running entry (due ones first; not-due ones get the same actions), with the amber "N timers running in parallel" header from §4.1.
- Per entry, the nag offers **Still on it** (`lastCheckIn[id] = now`), **Stop** (through `TrackingActions.StopSingle` with no owner, so the TooShort prompt survives the nag closing), **Stop & add note** (stop, then open `EntryEditorDialog`; no TooShort prompt), and **Switch…** (stop, then `ShowQuickPanel`; no TooShort prompt). The nag closes once every *due* row is answered; rows of entries stopped elsewhere are pruned. **Snooze 15 / 30 / 60 min** sets `lastCheckIn[id] = now + S - interval` for every listed entry, so each comes due again exactly S minutes later.

### 6.3 Custom scheduled reminders

- Stored in `reminders` and managed in Settings → *Reminders* tab (add / edit / delete / enable toggle; fields: title, message, weekday checkboxes, `HH:mm` time).
- Today's occurrence = today's local date at `time_local`, converted to UTC. An occurrence is due when the reminder is enabled, today's weekday bit is set, occurrence ≤ now, and `last_fired_utc` is null or earlier than the occurrence. On firing, `ReminderHost` calls `SetLastFired(id, now)` (§6.0).
- **No instant fire on create/edit:** `Core/Reminders/ScheduledReminderService.cs`, `Save(ScheduledReminder)`, is the only save path (used by the Reminders tab). When today's weekday bit is set and today's occurrence is ≤ now, it sets `last_fired_utc = now` before inserting or updating, so the reminder next fires at its following occurrence. It also validates that the title is non-empty and `days_mask != 0`.
- **Missed occurrences** (PC asleep or app closed at the time): because the rule compares against *today's* occurrence only, a missed one fires on the next evaluation the same local day, with the title suffixed *" (missed at 4:00 PM)"* when `now - occurrence > 2 min`. Earlier days' occurrences are never fired.
- Actions: **Dismiss**, **Snooze 15 min** (in-memory; re-shows the same nag after 15 min).

### 6.4 Nag window

`App/Reminders/NagWindowManager.cs` + `App/Windows/NagWindow.xaml`. The app uses its own topmost window instead of Windows toast notifications: toasts are swallowed by Focus Assist and need AUMID/shortcut registration for unpackaged apps, and a *nagging* tracker must actually be seen.

- Borderless, `Topmost=true`, `ShowActivated=false` (doesn't steal keyboard focus while typing; buttons still work on click), `ShowInTaskbar=false`, positioned bottom-right of `SystemParameters.WorkArea`.
- Only one is visible at a time. New nags go into a FIFO queue and the next is shown when the current one closes (and not presentation-busy, §6.0).
- The tray icon uses the `attention-*` set (§4.1) while a nag is visible or queued.
- No auto-dismiss. Closing via ✕ (or Alt+F4) counts as **Snooze 15 min**.
- A not-tracking nag, shown or queued, is dropped as soon as a timer starts.
- Any action that opens the quick panel or editor closes the nag first.

---

## 7. Idle detection

As built ([plan-phase6-idle-detection.md](plan-phase6-idle-detection.md)): the rules are pure Core code (`Core/Idle/IdleTracker.cs`, `Core/Idle/Downtime.cs`, `Core/Services/IdleResolver.cs`), and the App side is a thin shell. Neither `IdleMonitor` nor the startup check runs under `--no-background`.

### 7.1 Detection

`App/Idle/IdleMonitor.cs` (an `IHostedService` that also implements `IIdleState`) polls `GetLastInputInfo` every 15 s on a UI-thread `DispatcherTimer`. It uses user32 P/Invoke via `[LibraryImport]` on a blittable `LASTINPUTINFO { uint cbSize; uint dwTime; }`; the App csproj sets `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`, which the `LibraryImport` generator requires. Idle time is `unchecked((uint)Environment.TickCount64 - info.dwTime)` milliseconds, which survives the 49.7-day wrap of the 32-bit tick count.

It also subscribes to `Microsoft.Win32.SystemEvents.SessionSwitch` (`SessionLock` / `SessionUnlock`) and `PowerModeChanged` (`Suspend` / `Resume`), marshals each to the UI thread, and unsubscribes on stop. Every input goes to `IdleTracker`, a pure state machine:

- Settings: idle threshold (default 10 min), enabled (default on). Turning it off resets the state on the next poll.
- State: `IdleStartUtc?`. It is set to the **last input** when the polled idle time reaches the threshold, or on lock/suspend (so an auto-lock after inactivity counts from when the user left). An existing idle period keeps its start. `IsUserIdle` (§6.0) = `IdleStartUtc != null`.
- **Return** is any input **after** `IdleStartUtc` (polled last input > `IdleStartUtc` + 1 s; the period ends at that input), or `SessionUnlock`. While locked or suspended, polls are ignored, so lock-screen input isn't a return. `Resume` alone doesn't end the period; the next real input or unlock does.
- On return, the period `(IdleStartUtc, end)` is offered to the prompt only if it lasted **at least the threshold** (a short Win+L suppresses nags but doesn't prompt) and some running entry has `start_utc < IdleStartUtc`. Either way, `IdleStartUtc` is cleared. While the prompt is open, polls and session events are ignored.

### 7.2 Prompt on return

`App/Windows/IdlePromptDialog.xaml` (topmost, activated, modal, no owner, shown in the taskbar):

> You were away from **10:32** to **11:15** (43m) while tracking *Acme · Dev Work* (and 1 other).

Times use the current culture's short time, or short date and time when the two local dates differ. No button is the default and none has initial focus, so keys typed on the way back in can't pick an option; ✕ / Alt+F4 / Esc mean **Keep it**.

The choice is applied by `Core/Services/IdleResolver.cs`, `Apply(IdleChoice choice, DateTimeOffset idleStartUtc)`, which re-reads the affected running entries itself (`Affected(idleStartUtc)`, oldest first), so one stopped meanwhile is skipped:

| Button | Effect |
|---|---|
| **Keep it** | Nothing changes. |
| **Discard idle time & continue** | For each running entry with `start_utc < idleStart`: `StopAt(entry, idleStart)`, then `Continue(entry, confirmedConcurrent: true, overlapReason: entry.overlap_reason)`. Each entry replaces itself, so the overall concurrency doesn't change; the new entries start at the click. |
| **Discard idle time & stop** | For each running entry with `start_utc < idleStart`: `StopAt(entry, idleStart)`. |

Entries started during the idle period (`start_utc ≥ idleStart`) are left untouched by every option. Reminder bookkeeping isn't reset after the prompt (stakeholder decision): whatever fell due while idle fires on the next tick.

### 7.3 App not running (crash, logoff, exit with timers running)

`ReminderHost` writes `settings['heartbeat_utc'] = now` every 60 s. `App/Idle/StartupDowntimeCheck.cs` runs at startup in the slot given in §1 (after migrations and backup, before `host.StartAsync()`, so before the first heartbeat write; skipped under `--no-background`). `Core/Idle/Downtime.Find` decides: if `heartbeat_utc` is set, idle detection is enabled, `now - heartbeat_utc ≥ idle threshold`, and some running entry started before the heartbeat, it shows `IdlePromptDialog` with `idleStart = heartbeat_utc` and the wording *"The app wasn't running from … to …"*. The choice goes through the same `IdleResolver`.

---

## 8. Settings

`App/Windows/SettingsWindow.xaml`, a tabbed window opened from the tray:

- **General** (the first tab, and the default for `ShowSettings()`): Start with Windows (writes/removes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\NaggingTimeTracker` = `"<Environment.ProcessPath>"`, applied at once), the data folder path, and Open data folder (button). At startup, if the Run value exists but points to a different path (e.g. after a reinstall to a different scope), the app rewrites it to the current `Environment.ProcessPath`. The rules live in `Core/Startup/RunAtLogin.cs` behind `IRunKeyStore`; `App/Startup/RegistryRunKeyStore.cs` (`ValueName = "NaggingTimeTracker"`) is the registry side. **A `--data-dir` instance never touches the value** (the checkbox is disabled, no self-heal), because the Run value has no arguments and always starts the default data folder, and **Debug builds never self-heal** (stakeholder decision). As built: [plan-phase7-polish.md](plan-phase7-polish.md) §2–§4.
- **Work hours:** work days, start, end; not-tracking nag on/off + interval. The Work hours and Check-ins tabs **apply each valid change immediately** (on focus loss); there's no Save button. Tabs are selected by a `SettingsTab` tag, not by position.
- **Check-ins:** on/off + interval.
- **Idle:** on/off + threshold (1–1440 min), between Check-ins and Reminders; it applies immediately like the Work hours and Check-ins tabs.
- **Reminders:** custom reminders grid (§6.3).
- **Customers** / **Task types:** rename / archive (§5).

`Core/Settings/AppSettings.cs` is a typed record, loaded and saved via `Core/Settings/SettingsService.cs` over `ISettingsStore`. Values are stored as invariant-culture strings. The `*_utc` keys hold epoch milliseconds as decimal text (e.g. `"1767225600000"`), matching §2. Keys and defaults:

| Key | Default |
|---|---|
| `work_days_mask` | `62` (Mon–Fri, bits per §2) |
| `work_start` / `work_end` | `09:00` / `17:00` |
| `nag_not_tracking_enabled` / `nag_not_tracking_interval_min` | `true` / `15` |
| `checkin_enabled` / `checkin_interval_min` | `true` / `60` |
| `idle_enabled` / `idle_threshold_min` | `true` / `10` |
| `heartbeat_utc`, `reminders_paused_until_utc`, `not_working_until_utc`, `last_backup_date` | unset |

The four state keys in the last row aren't part of `AppSettings`. They're read and written through `SettingsService.GetInstant` / `SetInstant` (epoch ms; `null` removes the key), and these don't raise `SettingsChanged`. That way, saving the user's settings can never overwrite a heartbeat or pause written meanwhile. Missing or unparsable keys fall back to these defaults. `SettingsService` raises `SettingsChanged`; the engine reads the current settings every tick, so changes apply without a restart.

---

## 9. CSV export

`App/Windows/ExportWindow.xaml`: date range (same presets as §5, "to" inclusive), Customer and Task type filters, the same text search box as §5, an "Include running entries" checkbox (default off), and **Export…** → `SaveFileDialog` (default name `timesheet-<from yyyy-MM-dd>_to_<to yyyy-MM-dd>.csv`, using the inclusive "to" date). Opening it from `EntriesWindow` pre-fills all four filters (date range, customer, task type, text) from that window, so what's exported matches what was on screen. Rows come from `QueryViews` with the same `EntryFilter`. When the checkbox is off, rows with `StopUtc == null` are dropped in `ExportWindow` before calling the exporter.

`Core/Export/CsvExporter.cs`, `Write(Stream, IEnumerable<EntryView>, DateTimeOffset nowUtc, TimeZoneInfo tz)`, writes **UTF-8 with BOM** (so Excel detects the encoding), comma-delimited, with RFC 4180 quoting via CsvHelper and `CultureInfo.InvariantCulture`. Rows are ordered by `start_utc`. Columns:

| Column | Format |
|---|---|
| `entry_id` | integer |
| `date` | local `yyyy-MM-dd` of start |
| `start` | local `yyyy-MM-dd HH:mm` |
| `stop` | local `yyyy-MM-dd HH:mm`; blank if running |
| `duration_hours` | decimal, 2 dp (`1.25`); running entries measured to `nowUtc` |
| `duration_minutes` | integer, rounded half away from zero |
| `customer` | name |
| `task_type` | name |
| `external_link` | as stored |
| `notes` | as stored (newlines preserved inside quotes) |

Any text cell that starts with `=`, `+`, `-`, `@`, tab or CR gets a leading `'` (CsvHelper `CsvInjection.Escape`; stakeholder decision in [plan-phase4-csv-export.md](plan-phase4-csv-export.md)), so Excel shows it as text instead of running it as a formula.
| `continued_from_id` | integer or blank |
| `overlap_reason` | as stored or blank |

`EntryView` is the Core record from `IEntryRepository.QueryViews` (§2). The window previews how many entries will be exported (and how many running ones are left out) and disables **Export…** when nothing matches. The file is written to `<path>.tmp` and then moved over the target, so a failure never leaves a truncated file. After a successful write, the window offers **Open file** / **Open folder**. Opening it from `EntriesWindow` re-applies the pre-fill even if the Export window is already open. Both windows share one filter bar (`App/Controls/FilterBar.xaml`, `Core/Entries/FilterState.cs`).

---

## 10. Automated UI tests

**Practicality:** WPF exposes its controls through Windows UI Automation, so FlaUI (UIA3) can drive every window the app owns, reliably. Two things are deliberately **not** UI-tested:

- **The tray icon itself.** It lives in Explorer's notification area, and its overflow/pinning layout differs between machines. Tests open windows with `--show` (§1) instead of clicking the icon.
- **Timing-driven nags and idle prompts.** These depend on wall-clock time and real input idleness. They are covered by `ReminderEngineTests` / `IdleResolverTests` with `FakeTimeProvider`, and tests launch with `--no-background`.

**Constraints:** UI tests need an interactive, unlocked desktop session. They can't run headless or on a locked screen. They're marked `[Trait("Category", "UI")]` and run on demand:

- unit tests: `dotnet test --filter "Category!=UI"`
- UI tests: `dotnet test tests/NaggingTimeTracker.UiTests`

**Harness** (`tests/NaggingTimeTracker.UiTests/AppFixture.cs`, `IDisposable`):

1. Creates a temp data dir.
2. Launches `src/NaggingTimeTracker.App/bin/<Configuration>/net10.0-windows/NaggingTimeTracker.exe` via `FlaUI.Core.Application.Launch` with `--data-dir <temp> --no-background --show <window>`. The App project is a `ProjectReference` with `ReferenceOutputAssembly=false`, so it is built first.
3. Waits (up to 10 s) for the target window by AutomationId.
4. On dispose, closes the app (killing it after 5 s), then deletes the temp dir.

Helpers: `FindById(window, "QuickPanel.StartButton")` and similar, with a `Retry.WhileNull` timeout of 5 s.

**Scenarios** (one test class per file in `tests/NaggingTimeTracker.UiTests/`):

| File | Scenario |
|---|---|
| `QuickPanelTests.cs` | Type a new customer `Acme` + new type `Research` and click Start → a Running row appears; dispose that app (so the single-instance mutex is released), then launch again with `--show entries` on the same data dir → the grid shows a running row for Acme · Research. `AppFixture` therefore takes an optional existing data dir, and deletes it only if it created it. |
| `ConcurrencyTests.cs` | With one running, start a second → `ConcurrencyDialog` appears; enter a reason and click **Run in parallel** → the quick panel shows the "2 timers running in parallel" banner and both rows; **Keep only this** on one leaves exactly one row |
| `EntriesEditorTests.cs` | Open entries, double-click a row, change notes, Save → the grid shows the new notes; set stop before start → Save shows the blocking error and the dialog stays open |
| `ContinueTests.cs` | Stop an entry, click Continue in Recent → a new running row with the same customer/type; the entries grid shows ↪ on it |
| `ExportTests.cs` | Open export, click Export…, type a temp path into the `SaveFileDialog`'s *File name* box, press Enter → the file exists and its header row matches §9 |

Seed data for a test goes through the UI or through pre-populating the temp DB with `TrackerDbContext`; the UiTests project references Data for that.

---

## 11. Installer

**Technology:** WiX Toolset v5, with an SDK-style `installer/NaggingTimeTracker.Installer.wixproj` (`<Project Sdk="WixToolset.Sdk/5.x.x">`, pinned) and `installer/Package.wxs`. It builds with `dotnet build`, so no separate WiX install is needed.

**Payload:** the self-contained single-file publish output (§12 Phase 9): `PublishSingleFile=true`, `SelfContained=true`, `IncludeNativeLibrariesForSelfExtract=true` (pulls the WPF and `e_sqlite3` native DLLs into the exe), `RuntimeIdentifier=win-x64`. The MSI therefore installs a single `NaggingTimeTracker.exe`.

**Install scope chosen by the user:**

- `<Package Scope="perUserOrMachine" …>` plus `<ui:WixUI Id="WixUI_Advanced" />` (from `WixToolset.UI.wixext`). WixUI_Advanced shows the **"Install just for you / for all users"** dialog.
- *Just for you*: no UAC; installs to `%LOCALAPPDATA%\Programs\NaggingTimeTracker\`.
- *All users*: UAC prompt; installs to `%ProgramFiles%\NaggingTimeTracker\` (the 64-bit folder).
- WixUI_Advanced drives the folder through the `APPLICATIONFOLDER` directory and its `WixPerUserFolder` / `WixPerMachineFolder` properties. So the install directory **must** have `Id="APPLICATIONFOLDER"` (under `ProgramFiles64Folder`, `Name="NaggingTimeTracker"`), and `Package.wxs` sets `<Property Id="ApplicationFolderName" Value="NaggingTimeTracker" />` and `<Property Id="WixAppFolder" Value="WixPerUserFolder" />` (per-user preselected).
- WiX 5.0.2's stock defaults are `[LocalAppDataFolder]Apps\[ApplicationFolderName]` and `[ProgramFilesFolder][ApplicationFolderName]`, i.e. the 32-bit Program Files (checked in the cached `wixtoolset.ui.wixext` 5.0.2 wixlib). In both the UI and execute sequences, the stock actions run as a chain: `WixSetDefaultPerUserFolder` (before `CostFinalize`) → `WixSetDefaultPerMachineFolder` → `WixSetPerUserFolder` → `WixSetPerMachineFolder`, and the last two copy the chosen property into `APPLICATIONFOLDER`. `Package.wxs` overrides the defaults with two `<SetProperty Sequence="both" Before="WixSetPerUserFolder" …>` elements, so they land after both defaults and before either copy:
  - `Id="WixPerUserFolder" Value="[LocalAppDataFolder]Programs\[ApplicationFolderName]"`
  - `Id="WixPerMachineFolder" Value="[ProgramFiles64Folder][ApplicationFolderName]"`
- User data is per-user (`%LOCALAPPDATA%\NaggingTimeTracker\`) in both cases and is never touched by the MSI.

**Package contents (`Package.wxs`):**

- `Name="Nagging Time Tracker"`, `Manufacturer="Joseph Thompson"`, `Version="$(ProductVersion)"`, and a fixed `UpgradeCode` GUID, generated once and never changed. The wixproj forwards MSBuild properties to WiX preprocessor variables with `<DefineConstants>ProductVersion=$(ProductVersion);PublishDir=$(PublishDir)</DefineConstants>`, and the exe's `<File Source="$(PublishDir)NaggingTimeTracker.exe" />` uses that `PublishDir`.
- `<MajorUpgrade DowngradeErrorMessage="A newer version is already installed." />`. Installing a newer MSI upgrades in place.
- A Start Menu shortcut, *Nagging Time Tracker*, in `ProgramMenuFolder`. Its component's key path is a `<RegistryValue Root="HKMU" Key="Software\NaggingTimeTracker" Name="StartMenuShortcut" Type="integer" Value="1" KeyPath="yes" />` (HKMU resolves to HKCU or HKLM to match the chosen scope), alongside a `<RemoveFolder On="uninstall" />`.
- An optional "Launch Nagging Time Tracker" checkbox on the exit dialog (`WIXUI_EXITDIALOGOPTIONALCHECKBOX` + a `LaunchApplication` custom action).
- `<util:CloseApplication Target="NaggingTimeTracker.exe" EndSessionMessage="yes" TerminateProcess="1" RebootPrompt="no" />` (from `WixToolset.Util.wixext`) closes a running instance before upgrade or uninstall. That's safe because running timers are persisted (§4) and the downtime is checked on next launch (§7.3).
- On uninstall, a `RemoveRegistryValue` for `HKCU\…\Run\NaggingTimeTracker` removes the *uninstalling user's* autostart entry. For an all-users install, other users' Run entries can't be reached from the MSI; the startup self-heal in §8 doesn't apply after uninstall, so those stay behind (documented in the README).
- `ARPPRODUCTICON` set to `tracking-1.ico`, and an Add/Remove Programs entry.

**Build script** (`tools/build-installer.ps1`):

1. `dotnet publish src/NaggingTimeTracker.App -c Release -p:PublishProfile=win-x64` (the profile sets `PublishDir` to `bin\publish\win-x64\`)
2. Reads `<Version>` from the App csproj.
3. `dotnet build installer/NaggingTimeTracker.Installer.wixproj -c Release -p:ProductVersion=<v> -p:PublishDir=<absolute path of src/NaggingTimeTracker.App/bin/publish/win-x64/, with trailing slash>`
4. Copies the MSI to `artifacts/NaggingTimeTracker-<v>-x64.msi`. `artifacts/` is git-ignored.

**Signing:** none for now. Windows SmartScreen will warn on first run of the MSI (*More info → Run anyway*); the README says so. The script has a clearly marked no-op `# Signing:` step where `signtool sign` for the exe and the MSI goes later.

---

## 12. Phases

Each phase is one commit. At the end of every phase, `dotnet build` passes with zero warnings and `dotnet test --filter "Category!=UI"` passes. From Phase 8 onward, the UI suite (§10) also passes when run on an interactive desktop.

### Phase 1 — Scaffold + data layer
- [x] Create `NaggingTimeTracker.sln`, the three `src/` projects and `tests/NaggingTimeTracker.Tests` per §1, plus `Directory.Build.props`, `.editorconfig`, `.gitignore`
- [x] Add NuGet references per §1
- [x] `Core/Time/Clock.cs` (`NowUtc` truncated to ms) per §2
- [x] Domain records in `Core/Model/` (`Customer`, `TaskType`, `Entry`, `EntryView`, `ScheduledReminder`) and the interfaces in `Core/Abstractions/` per §2
- [x] `.config/dotnet-tools.json` with `dotnet-ef` per §1
- [x] `Data/Converters/EpochMsConverter.cs`, `Data/TrackerDbContext.cs` (with `ConfigureConventions`), and `Data/Configurations/{Customer,TaskType,Entry,Reminder,Setting}Configuration.cs` per §2, including `HasData` seed
- [x] `Data/DataPaths.cs` (resolves and creates `%LOCALAPPDATA%\NaggingTimeTracker\`, returns the DB and backup paths) and `Data/DesignTimeDbContextFactory.cs`
- [x] Generate `Data/Migrations/*_InitialCreate.cs` with `dotnet ef migrations add InitialCreate`; check `dotnet ef migrations script` against the §2 schema (NOCASE, CHECK, partial index, FK delete behaviours)
- [x] `Data/ServiceCollectionExtensions.cs`: `AddTrackerData(dbPath)` registers `AddDbContextFactory` plus the repositories; `Data/DatabaseInitializer.cs`: `Migrate()` then the WAL pragma
- [x] `Data/Repositories/*` implementing every §2 interface, including `QueryViews`, `GetRecentDistinct`, `GetOverlappingIds` and `UpdateNotes`; `Data/Entities/SettingRow.cs`
- [x] `Core/Time/LocalDateRange.cs` (`ToUtcRange`, inclusive "to" date) per §2
- [x] `tests/.../TestDb.cs` fixture per §1
- [x] `tests/.../Core/LocalDateRangeTests.cs`: `to` is inclusive (ends at the next local midnight); a DST-change day in `America/New_York` is 23 h / 25 h long
- [x] `tests/.../Data/MigrationTests.cs`: `Migrate()` on an empty DB applies `InitialCreate` and seeds 5 task types; re-running is a no-op; `Database.GetPendingMigrations()` is empty, and the model has no pending changes (`db.Database.HasPendingModelChanges()` is false), which catches a forgotten migration
- [x] `tests/.../Data/EntryRepositoryTests.cs` (the `TimeLocal` check lives in `ReminderRepositoryTests.cs`; `SettingsStoreTests.cs` was added too): CRUD round-trip; `GetRunning`; `GetRecentStopped` (ordered by stop, newest first); `GetRecentDistinct` (three stops of the same customer/type/link → one row; a different link → separate row; respects `n`); `Query` date range is half-open and filters by customer/type/text; `GetOverlapping` (overlap, touching boundaries don't count, running entry treated as ending now, null `stopUtc` treated as now, `excludeId`); `GetOverlappingIds` (A overlaps B → both ids; touching entries → neither); `UpdateNotes` leaves `stop_utc` untouched; `TimeLocal` round-trips as `'HH:mm'` text; `CHECK` rejects stop ≤ start; deleting a source nulls `continued_from_id`
- [x] `tests/.../Data/CatalogRepositoryTests.cs`: unique name is case-insensitive; archived items are excluded from `GetActive` but resolve through an entry's `QueryViews`
- [x] `tests/.../Core/ClockTests.cs`: `NowUtc` drops sub-millisecond ticks and returns offset zero
- [x] `tests/.../Data/EpochMsConverterTests.cs`: a ms-truncated value round-trips exactly, including a non-UTC input offset (stored as the same instant, read back with offset zero); the column type is `INTEGER`; a `Query` date-range filter and an `OrderBy(StartUtc)` both translate to SQL without client evaluation (`ToQueryString()` contains the comparison) and return the right rows

### Phase 2 — Tracking service + tray
- [x] `Core/Services/CatalogService.cs` and `Core/Services/TrackingService.cs` per §3 (including `StopAllExcept`, `UpdateNotes`, and the `Save` guards)
- [x] `Core/Settings/AppSettings.cs` + `SettingsService.cs` per §8 (moved here from a later phase because the tray's Pause reminders submenu writes `reminders_paused_until_utc` through it)
- [x] `tests/.../Core/SettingsServiceTests.cs`: defaults for missing keys; round-trip; invariant culture under `de-DE`
- [x] `tests/.../Core/TrackingServiceTests.cs` (`FakeTimeProvider`): start/stop; start trims the link and stores a blank one as `NULL`; `StopAllExcept` leaves only the given entry running; `UpdateNotes` on an entry stopped in the meantime keeps it stopped; `Save` rejects a new entry without stop, rejects re-opening an entry stopped in the DB, and rejects stop ≤ start; `NeedsConfirmation` when another is running; confirmed start stores `overlap_reason`; continuing a stopped entry while another runs needs confirmation; continuing a running entry stops it, needs no confirmation, and copies `overlap_reason`; continue copies customer/type/link with empty notes and sets `continued_from_id`; `TooShort` under 1 min; stop clamp when `now ≤ start`; `StopAt` rejects `≤ start`; `ContinueLast` on an empty DB
- [x] `tests/.../Core/CatalogServiceTests.cs`: trims; case-insensitive match; un-archives; empty name throws; rename to another row's name in different case → `NameTaken`; rename to a case change of its own name succeeds; archive/unarchive toggles `GetActive`
- [x] `App/App.xaml(.cs)`: host wiring per §1, `ShutdownMode=OnExplicitShutdown`; `<AssemblyName>`/`<Version>` per §1
- [x] `Core/Startup/StartupOptions.cs` + `InstanceNames.cs` (`--data-dir`, `--show`, `--no-background`) per §1; `DataPaths` accepts the override
- [x] `App/SingleInstance.cs` per §4
- [x] `tools/make-icons.ps1` + the generated `App/Assets/tray/*.ico` set from §4.1
- [x] `App/Tray/TrayHost.cs`: `UpdateIcon()` icon selection (`hasAttention` is always `false` until Phase 5), the rich `TrayToolTip`, and the context menu per §4 (Pause reminders submenu wired to `reminders_paused_until_utc` via `SettingsService`; it takes effect once Phase 5 lands). Menu items, quick-panel footer links and `--show` values for windows that don't exist yet (*All entries…*, *Export…*, *Settings…*) are added in the phase that builds each window (3, 4, 3), so every phase stays runnable with no dead links
- [x] `Core/Tray/TrayIconSelector.cs`: a pure static `Select(hasAttention, runningCount) → file name`, which `TrayHost.UpdateIcon()` calls. `tests/.../Core/TrayIconSelectorTests.cs` tests every row of the §4.1 table, including 9 and 10 → `9plus`
- [x] `App/Windows/QuickPanelWindow.xaml` per §4.2, including the multiple-timer banner, **Keep only this** (`StopAllExcept`), Recent via `GetRecentDistinct(5)`, the `_modalDepth` hide guard and the notes flush on `Deactivated`. Set AutomationIds on every control per §1
- [x] `App/Windows/ConcurrencyDialog.xaml` per §4
- [x] Exit confirmation when timers are running

### Phase 3 — Entries window + editor
- [x] `App/Windows/EntriesWindow.xaml` per §5, including the ⧉ column and the amber running rows from §4.1; add the *All entries…* menu item, footer link and `--show entries`
- [x] `App/Windows/EntryEditorDialog.xaml` per §5
- [x] `App/Windows/SettingsWindow.xaml` shell with Customers and Task types tabs per §5; add the *Settings…* menu item, footer link and `--show settings`

### Phase 4 — CSV export
- [x] `Core/Export/CsvExporter.cs` per §9
- [x] `tests/.../Core/CsvExporterTests.cs`: header and column order; notes with commas, quotes and newlines round-trip through a CsvHelper reader; BOM present; `1.25` (not `1,25`) under a `de-DE` current culture; local conversion in a DST zone (`America/New_York`, an entry spanning 2026-03-08 02:00); running entry blank stop + duration to `nowUtc`
- [x] `App/Windows/ExportWindow.xaml` (with the text search box and the running-entries filter) + pre-fill of all four filters from `EntriesWindow` per §9; add the *Export CSV…* menu item, footer link and `--show export`

### Phase 5 — Reminders
- [x] `Core/Reminders/WorkSchedule.cs` per §6.1
- [x] `tests/.../Core/WorkScheduleTests.cs`: day shift inside/outside; overnight shift Fri 22:00–06:00 → in at Sat 03:00, out at Sat 07:00, out at Sat 23:00 (Sat isn't a work day); overnight shift starting Sun when only Mon–Fri are checked → Mon 02:00 is out; DST spring-forward start time moved forward; start == end rejected
- [x] `Core/Reminders/ReminderEngine.cs`, `ReminderState`, `Nag` per §6
- [x] `tests/.../Core/ReminderEngineTests.cs` (`FakeTimeProvider`):
  - not-tracking: fires only inside a shift; interval measured from shift start and from last stop; honours snooze, "not working this shift" (clears at the shift end, including an overnight shift end) and pause
  - check-in: fires per entry; combines entries due on the same tick; with 2 running and 1 due, the nag lists both (due first); "Still on it" resets; no burst after restart
  - scheduled: fires once per occurrence (given `last_fired_utc` updated as `ReminderHost` would); missed same-day fires with the suffix; earlier days are skipped; returned nags carry `ReminderId`
  - suppression: idle and prompt-open (including "queued") return nothing; presentation-busy still returns nags
- [x] `Core/Reminders/ScheduledReminderService.cs` per §6.3 + `tests/.../Core/ScheduledReminderServiceTests.cs`: saving after today's time sets `last_fired_utc = now` (no instant fire); saving before today's time leaves it null; a weekday not in the mask leaves it null; empty title or `days_mask = 0` rejected
- [x] `App/Reminders/ReminderHost.cs` (`PeriodicTimer`, UI dispatch, `SetLastFired` for scheduled nags, heartbeat)
- [x] `App/Reminders/PresentationState.cs` (`SHQueryUserNotificationState`) per §6.0
- [x] `App/Reminders/NagWindowManager.cs` + `App/Windows/NagWindow.xaml` per §6.4
- [x] SettingsWindow tabs: Work hours (with an "(overnight)" hint shown when end ≤ start), Check-ins, Reminders

### Phase 6 — Idle detection
- [x] `App/Idle/IdleMonitor.cs` per §7.1 (+ `<AllowUnsafeBlocks>` in the App csproj); feeds `IsUserIdle` and `IsPromptOpen`; the rules live in `Core/Idle/IdleTracker.cs`
- [x] `Core/Services/IdleResolver.cs` per §7.2
- [x] `tests/.../Core/IdleResolverTests.cs`: keep; discard-continue (stop at idleStart, new continued entry starts now, `overlap_reason` preserved, no confirmation); discard-stop; entry started during idle is untouched; plus `IdleTrackerTests.cs` and `DowntimeTests.cs`
- [x] `App/Windows/IdlePromptDialog.xaml` shown by `IdleMonitor` on return per §7.2
- [x] `App/Idle/StartupDowntimeCheck.cs` per §7.3 (decision in `Core/Idle/Downtime.cs`), wired into the §1 startup order (skipped when `heartbeat_utc` is unset or under `--no-background`)
- [x] SettingsWindow tab: Idle

### Phase 7 — Polish
- [x] SettingsWindow General tab: Start with Windows, Open data folder, Run-path self-heal per §8 (`Core/Startup/RunAtLogin.cs`, `App/Startup/RegistryRunKeyStore.cs`)
- [x] `Data/BackupService.cs` per §2, called at startup after migrations
- [x] `tests/.../Data/BackupServiceTests.cs`: creates today's backup once; retains 7; plus `Core/RunAtLoginTests.cs`
- [x] `<ApplicationIcon>Assets\app.ico</ApplicationIcon>` in the App csproj (`app.ico` is the green circle at 16–256 px, generated by `tools/make-icons.ps1`; stakeholder decision, instead of the 32-px `Assets\tray\tracking-1.ico`)

### Phase 8 — Automated UI tests
- [ ] Create `tests/NaggingTimeTracker.UiTests` (`net10.0-windows`, FlaUI.UIA3, `ProjectReference` to App with `ReferenceOutputAssembly=false`, and to Data) and add it to the sln
- [ ] `AppFixture.cs` + `FindById` helpers per §10
- [ ] Audit AutomationIds across every window against the §1 convention; add any missing
- [ ] The five scenario files in the §10 table, each class marked `[Trait("Category", "UI")]`

### Phase 9 — Installer
- [ ] `src/NaggingTimeTracker.App/Properties/PublishProfiles/win-x64.pubxml`: Release, `win-x64`, self-contained, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`
- [ ] `installer/NaggingTimeTracker.Installer.wixproj` + `installer/Package.wxs` per §11 (generate the `UpgradeCode` GUID once and commit it)
- [ ] `tools/build-installer.ps1` per §11; add `artifacts/` to `.gitignore`
- [ ] Manual install check, recorded in the commit message: per-user install with no UAC, exe lands in `%LOCALAPPDATA%\Programs\NaggingTimeTracker\` (not `…\Apps\…`) → Start Menu shortcut launches → upgrade with a version-bumped MSI while the app is running (the app closes, its running timer survives) → uninstall leaves `%LOCALAPPDATA%\NaggingTimeTracker\` intact and removes the Run value; then repeat the install as all-users (UAC shown, installs to `C:\Program Files\NaggingTimeTracker\`, not `Program Files (x86)`)
- [ ] Update [README.md](README.md): drop the "planned, not yet implemented" status note; keep the feature summary and add build/test (unit vs UI)/run commands; `tools/build-installer.ps1`; install scope choice; SmartScreen warning for unsigned builds; data and backup locations; uninstall keeps data

---

## Non-goals

- **No cloud sync, multi-device or multi-user support.** Local-only is a hard requirement (Overview).
- **No integration with external trackers** (Jira, Azure DevOps, GitHub). `external_link` is free text, opened in a browser only if it's an http(s) URL; nothing is fetched or validated remotely (§5).
- **No Windows toast notifications.** Reminders use the custom nag window (§6.4).
- **No "long-running timer" warning.** The periodic check-in (§6.2) already covers a forgotten timer; the stakeholder did not choose a separate rule.
- **No split or per-day schedules.** There is one work start/end pair for all work days (which may be overnight, §6.1). It can't be e.g. 9–12 + 13–17, or a different time per weekday.
- **No merging of customers or task types.** Rename and archive only (§5).
- **No reports or charts** beyond the filtered total in `EntriesWindow` (§5). CSV export (§9) is the reporting path.
- **No billing rates, invoicing or rounding rules.** Export gives raw durations.
- **No CSV import.**
- **No UI tests of the tray icon or of timing-driven nags/idle prompts**, and no UI tests in a headless CI run (§10). Those behaviours are unit-tested.
- **No switching install scope on upgrade.** Windows Installer only finds related products in the same context, so a per-machine MSI won't upgrade a per-user install (or vice versa). To change scope, uninstall first; the README says so (§11).
- **No TooShort prompt on bulk stops.** Only an explicit single stop offers Discard/Keep. Short entries from bulk stops are kept and can be deleted in the Entries window (§3).
- **No MSIX, no auto-update.** An update feed would break the local-only rule, so upgrades happen by running a newer MSI (§11).
- **No code signing yet.** There's a placeholder step in `tools/build-installer.ps1` (§11).
- **No removal of user data on uninstall.** It's the only copy of the user's records (§11).
- **No encryption at rest.** The DB sits in the user's profile, protected by Windows account permissions.
- **No localisation.** The UI is English only; dates and times follow the current Windows culture in the UI, while the CSV uses the fixed formats in §9.
- **No macOS/Linux support.** The App project is `net10.0-windows`.
