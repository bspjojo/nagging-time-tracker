# Phase 1 — Scaffold + Data Layer (implementation plan)

**Status:** In progress — code complete (build clean, 58/58 tests green, placeholder app verified); the two commit steps remain.

**Sequencing note:** this doc expands **Phase 1** of [plan-time-tracker-v1.md](plan-time-tracker-v1.md) (§12, "Phase 1 — Scaffold + data layer") into implementable detail. It is the first phase, so nothing has to land before it. The parent plan stays the source of truth for the schema (§2), the repository contracts (§2 "Repositories"), the solution layout (§1) and the time convention (§2). Where this doc deviates from the parent, it says so under §5 "Accepted deviations from parent §2"; those deviations should be folded back into the parent's §2 once this doc is approved. Phase 2 (tracking service + tray) builds directly on the interfaces fixed here.

The repo is greenfield, confirmed by reading: the only files are [README.md](README.md) and [plan-time-tracker-v1.md](plan-time-tracker-v1.md), on one `first commit` (`8673775`). There is no code to reuse. The machine has .NET SDKs 5.0.301, 6.0.100 and **10.0.400** installed, plus global tools `dotnet-ef` 10.0.10 and `wix` 5.0.2 (checked with `dotnet --list-sdks` / `dotnet tool list -g`).

Phase 1 produces: the solution and the five projects of parent §1 (minus `UiTests`, which is Phase 8, and the installer, which is Phase 9); the Core domain records, time helpers and repository interfaces; the complete EF Core/SQLite data layer with its `InitialCreate` migration; a placeholder App that migrates the real database and exits; and the integration tests for all of it. **No UI, no services** (`TrackingService`, `CatalogService` and `SettingsService` are Phase 2).

**Decisions already made (givens):** everything in the parent's "Decisions already made by the stakeholder" list, plus the parent's §1/§2 technical choices (EF Core + SQLite, epoch-ms `INTEGER` timestamps, `IDbContextFactory` with a short-lived context per call, real SQLite in tests, no InMemory provider).

**Commit granularity:** one commit, "Phase 1 — scaffold + data layer", per the parent's one-commit-per-phase rule. The two plan docs and the README fix (§1.4) are committed first as their own commit, so the Phase 1 commit contains only code.

---

## 1. Toolchain and scaffold

### 1.1 SDK pinning

Several SDKs are installed, so add `global.json` at the repo root:

```json
{ "sdk": { "version": "10.0.400", "rollForward": "latestFeature" } }
```

This guarantees `dotnet` resolves a 10.0.x SDK on any machine and on this one even if an 11.x SDK is installed later.

### 1.2 Package versions

Pinned to the latest stable **10.x-compatible** versions on NuGet as of 2026-11-14 (checked with `dotnet package search --exact-match`). EF Core / `Microsoft.Extensions.*` **11.0.0** exist but belong to .NET 11; the stack is .NET 10, so Phase 1 stays on the 10.0.x line.

| Package | Version | Project |
|---|---|---|
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.15 | Data |
| `Microsoft.EntityFrameworkCore.Design` | 10.0.15 (`PrivateAssets=all`) | Data |
| `CsvHelper` | 34.0.0 | Core (unused until Phase 4; added now per parent §1) |
| `H.NotifyIcon.Wpf` | 2.4.1 | App (unused until Phase 2) |
| `CommunityToolkit.Mvvm` | 8.4.2 | App (unused until Phase 2) |
| `Microsoft.Extensions.Hosting` | 10.0.15 | App (unused until Phase 2) |
| `xunit` | 2.9.3 | Tests |
| `xunit.runner.visualstudio` | 3.1.5 | Tests |
| `Microsoft.NET.Test.Sdk` | 18.12.0 | Tests |
| `Microsoft.Extensions.TimeProvider.Testing` | 10.11.0 | Tests |
| `dotnet-ef` (local tool) | 10.0.15 | `.config/dotnet-tools.json` |

`xunit` stays on v2 (the package the parent names). `xunit.runner.visualstudio` is pinned to the 3.x line, the last line documented to run v2 tests; 4.x is not used.

### 1.3 Commands

Run from the repo root, in this order:

```powershell
dotnet new gitignore
dotnet new editorconfig
dotnet new sln -n NaggingTimeTracker --format sln
dotnet new classlib -n NaggingTimeTracker.Core -o src/NaggingTimeTracker.Core -f net10.0
dotnet new classlib -n NaggingTimeTracker.Data -o src/NaggingTimeTracker.Data -f net10.0
dotnet new wpf      -n NaggingTimeTracker.App  -o src/NaggingTimeTracker.App  -f net10.0
dotnet new xunit    -n NaggingTimeTracker.Tests -o tests/NaggingTimeTracker.Tests -f net10.0
dotnet sln add src/NaggingTimeTracker.Core src/NaggingTimeTracker.Data src/NaggingTimeTracker.App tests/NaggingTimeTracker.Tests
dotnet add src/NaggingTimeTracker.Data reference src/NaggingTimeTracker.Core
dotnet add src/NaggingTimeTracker.App  reference src/NaggingTimeTracker.Core src/NaggingTimeTracker.Data
dotnet add tests/NaggingTimeTracker.Tests reference src/NaggingTimeTracker.Core src/NaggingTimeTracker.Data
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 10.0.15
```

The 10.0.400 SDK writes `dotnet-tools.json` at the repo root; move it to `.config/dotnet-tools.json` (parent §1). `dotnet` finds it in either place.

`--format sln` keeps the parent's `NaggingTimeTracker.sln` name; the .NET 10 SDK would otherwise create a `.slnx`. Then:

- Delete the template's `Class1.cs` (Core, Data), `UnitTest1.cs` (Tests), and `MainWindow.xaml(.cs)` (App).
- In the Tests csproj, make the `PackageReference` list **exactly** the four §1.2 test packages at their pins. Remove anything else the template wrote, e.g. `coverlet.collector`, or `xunit.v3` if the SDK's template has moved to v3. Also remove any `<Using Include="Xunit" />` that doesn't match v2.
- Add the §1.2 packages with `dotnet add <project> package <id> --version <v>`.
- `TreatWarningsAsErrors` also turns NuGet audit warnings (`NU1901`–`NU1904`, a vulnerable transitive package) into errors. If restore reports one, for example through `Microsoft.EntityFrameworkCore.Design`'s Roslyn/MSBuild dependencies, fix it by adding a direct `PackageReference` to the patched version of that transitive package in the same project. Don't suppress it with `NoWarn`.

### 1.4 Repo-level files

- **`Directory.Build.props`:** `Nullable=enable`, `TreatWarningsAsErrors=true`, `LangVersion=latest`, `ImplicitUsings=enable`. Remove those properties from the generated csprojs so each one is set in a single place.
- **`.editorconfig`:** the `dotnet new editorconfig` output, plus a section that marks the generated migrations so analyzers don't fail the build on them:

  ```ini
  [src/NaggingTimeTracker.Data/Migrations/**.cs]
  generated_code = true
  ```

- **`.gitignore`:** the `dotnet new gitignore` output (the VisualStudio template the parent asks for). No additions are needed: the design-time DB lives in `%TEMP%` (§3.5).
- **README encoding fix:** [README.md](README.md) is currently **UTF-16 LE with BOM** (first bytes `FF FE`, confirmed with `od`), although the parent describes it as UTF-8. Git treats UTF-16 as binary, so diffs and GitHub rendering of later README edits break. Re-save it as UTF-8 without BOM, content unchanged. This goes in the docs commit before Phase 1.

---

## 2. Core: time helpers, domain records, abstractions

Core references no EF Core and no WPF. All records are `sealed record` with `init` properties (not positional), so EF Core can materialize them and callers can use `with`.

### 2.1 `Core/Time/Clock.cs`

```csharp
public static class Clock
{
    public static DateTimeOffset NowUtc(this TimeProvider tp)
    {
        var ticks = tp.GetUtcNow().UtcTicks;
        return new DateTimeOffset(ticks - ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
    }
}
```

### 2.2 `Core/Time/LocalDateRange.cs`

```csharp
public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) ToUtcRange(
    DateOnly from, DateOnly toInclusive, TimeZoneInfo tz)
```

- Throws `ArgumentException` when `toInclusive < from`.
- `FromUtc` = `StartOfLocalDay(from, tz)`; `ToUtc` = `StartOfLocalDay(toInclusive.AddDays(1), tz)`.
- `StartOfLocalDay(DateOnly d, TimeZoneInfo tz)` (private): take local midnight `d.ToDateTime(TimeOnly.MinValue)` (Kind `Unspecified`).
  - If `tz.IsInvalidTime(midnight)` (a zone whose DST gap starts at midnight), step forward one minute at a time, up to 24 h, until the time is valid. That first valid minute is when the day starts.
  - If `tz.IsAmbiguousTime(midnight)`, use the **larger** of `tz.GetAmbiguousTimeOffsets(midnight)`. That gives the earlier instant, i.e. the first time the day's midnight occurs.
  - Otherwise use `tz.GetUtcOffset(midnight)`.
  - Return `new DateTimeOffset(midnight, offset).ToUniversalTime()`, truncated to ms (it already is).

### 2.3 `Core/Model/`

```csharp
public interface ICatalogItem { long Id { get; } string Name { get; } bool Archived { get; } }

public sealed record Customer : ICatalogItem
{ public long Id { get; init; } public required string Name { get; init; } public bool Archived { get; init; } public DateTimeOffset CreatedUtc { get; init; } }

public sealed record TaskType : ICatalogItem   // same shape as Customer
{ public long Id { get; init; } public required string Name { get; init; } public bool Archived { get; init; } public DateTimeOffset CreatedUtc { get; init; } }

public sealed record Entry
{
    public long Id { get; init; }
    public long CustomerId { get; init; }
    public long TaskTypeId { get; init; }
    public DateTimeOffset StartUtc { get; init; }
    public DateTimeOffset? StopUtc { get; init; }      // null = running
    public string? ExternalLink { get; init; }
    public string Notes { get; init; } = "";
    public long? ContinuedFromId { get; init; }
    public string? OverlapReason { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
}

public sealed record EntryView(Entry Entry, string CustomerName, string TaskTypeName);

public sealed record EntryFilter(
    DateTimeOffset FromUtc, DateTimeOffset ToUtc,
    long? CustomerId = null, long? TaskTypeId = null, string? Text = null);

public sealed record ScheduledReminder
{
    public long Id { get; init; }
    public required string Title { get; init; }
    public string Message { get; init; } = "";
    public int DaysMask { get; init; }                 // bit n = (DayOfWeek)n
    public TimeOnly TimeLocal { get; init; }
    public bool Enabled { get; init; } = true;
    public DateTimeOffset? LastFiredUtc { get; init; }
}
```

Ids are `long`, because SQLite `INTEGER` is 64-bit. `EntryFilter` lives in `Core/Model/` next to `EntryView`. No computed properties (such as `IsRunning`) go on the records, so EF has nothing unexpected to map.

### 2.4 `Core/Abstractions/`

All methods are **synchronous**. Microsoft.Data.Sqlite's async methods run synchronously anyway (it has no true async I/O), so async would add ceremony with no benefit, and the parent's method names (`GetRunning()`, …) are already sync-shaped. A `bool` return means "a row was affected".

```csharp
public interface ICatalogRepository<T> where T : ICatalogItem
{
    IReadOnlyList<T> GetActive();               // !Archived, ordered by Name (NOCASE)
    IReadOnlyList<T> GetAll();                  // ordered by Name
    T? FindByName(string name);                 // case-insensitive (column collation)
    T Insert(T item);                           // returns the item with its generated Id
    bool Rename(long id, string newName);       // throws DbUpdateException on a duplicate; Phase 2's CatalogService pre-checks
    bool SetArchived(long id, bool archived);
}
public interface ICustomerRepository : ICatalogRepository<Customer> { }
public interface ITaskTypeRepository : ICatalogRepository<TaskType> { }

public interface IEntryRepository
{
    IReadOnlyList<Entry> GetRunning();                                  // ordered by StartUtc, Id
    Entry? GetById(long id);
    IReadOnlyList<Entry> GetRecentStopped(int n);                       // StopUtc desc, Id desc
    IReadOnlyList<Entry> GetRecentDistinct(int n);                      // per parent §2
    IReadOnlyList<Entry> Query(EntryFilter filter);                     // ordered by StartUtc, Id
    IReadOnlyList<EntryView> QueryViews(EntryFilter filter);            // ordered by StartUtc, Id
    IReadOnlyList<Entry> GetOverlapping(DateTimeOffset startUtc, DateTimeOffset? stopUtc,
                                        long? excludeId, DateTimeOffset nowUtc);
    IReadOnlySet<long> GetOverlappingIds(DateTimeOffset fromUtc, DateTimeOffset toUtc, DateTimeOffset nowUtc);
    Entry Insert(Entry entry);                                          // Id must be 0
    bool Update(Entry entry);                                           // all columns except id, created_utc
    bool UpdateNotes(long id, string notes, DateTimeOffset updatedUtc);
    bool Delete(long id);
}

public interface IReminderRepository
{
    IReadOnlyList<ScheduledReminder> GetAll();                          // ordered by TimeLocal, Title
    ScheduledReminder? GetById(long id);
    ScheduledReminder Insert(ScheduledReminder reminder);
    bool Update(ScheduledReminder reminder);
    bool Delete(long id);
    bool SetLastFired(long id, DateTimeOffset utc);
}

public interface ISettingsStore
{
    string? Get(string key);        // null when unset
    void Set(string key, string value);
    void Remove(string key);        // no-op when unset; used later to clear e.g. not_working_until_utc
}
```

`Remove` is not in the parent's list. It is added because §8's "unset" keys (`not_working_until_utc`, `reminders_paused_until_utc`) need a way back to unset, and settling the interface now avoids changing it in Phase 2.

---

## 3. Data: converter, context, configurations, wiring

### 3.1 `Data/Converters/EpochMsConverter.cs`

```csharp
public sealed class EpochMsConverter() : ValueConverter<DateTimeOffset, long>(
    v => v.ToUnixTimeMilliseconds(),
    v => DateTimeOffset.FromUnixTimeMilliseconds(v));
```

### 3.2 `Data/TrackerDbContext.cs`

- Constructor `TrackerDbContext(DbContextOptions<TrackerDbContext> options)`.
- `DbSet<Customer> Customers`, `DbSet<TaskType> TaskTypes`, `DbSet<Entry> Entries`, `DbSet<ScheduledReminder> Reminders`, `DbSet<SettingRow> Settings`.
- `ConfigureConventions`: `Properties<DateTimeOffset>().HaveConversion<EpochMsConverter>()` and `Properties<DateTimeOffset?>().HaveConversion<EpochMsConverter>()`. The nullable line is what makes `stop_utc` and `last_fired_utc` `INTEGER`. `EpochMsConverterTests` asserts the `stop_utc` column type, so a missing or ineffective nullable mapping fails a test rather than silently storing `TEXT`.
- `OnModelCreating`: `modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrackerDbContext).Assembly)`.

`Data/Entities/SettingRow.cs`: `public sealed class SettingRow { public required string Key { get; set; } public required string Value { get; set; } }`. It is **public**, because `TrackerDbContext` is public, so its `DbSet<SettingRow>` must be too. It stays out of Core; Core sees settings only through `ISettingsStore`.

### 3.3 `Data/Configurations/`

Every property gets an explicit `HasColumnName` in snake_case, and every table an explicit `ToTable`. Index names are set with `HasDatabaseName` to match the parent's schema.

- **`CustomerConfiguration`** / **`TaskTypeConfiguration`:** tables `customers` / `task_types`. `Name` `.IsRequired().UseCollation("NOCASE")`. `HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_customers_name")` / `"ux_task_types_name"`.
  - `TaskTypeConfiguration` also has `HasData` for ids 1–5: `Admin`, `Training`, `Dev Work`, `Meetings`, `Support`, each with `Archived = false` and `CreatedUtc = DateTimeOffset.FromUnixTimeMilliseconds(1767225600000)`. That timestamp is a `public const long SeedCreatedUtcMs` on the class, so tests can assert it.
- **`EntryConfiguration`:** table `entries` with `ToTable("entries", t => t.HasCheckConstraint("ck_entries_stop_after_start", "stop_utc IS NULL OR stop_utc > start_utc"))`.
  - `Notes` is `.IsRequired()`; `ExternalLink` and `OverlapReason` are optional.
  - `HasIndex(e => e.StartUtc).HasDatabaseName("ix_entries_start")`.
  - `HasIndex(e => e.StopUtc).HasFilter("stop_utc IS NULL").HasDatabaseName("ix_entries_running")`.
  - `HasOne<Customer>().WithMany().HasForeignKey(e => e.CustomerId).OnDelete(DeleteBehavior.Restrict)`, and the same for `TaskType`.
  - `HasOne<Entry>().WithMany().HasForeignKey(e => e.ContinuedFromId).OnDelete(DeleteBehavior.SetNull)`.
- **`ReminderConfiguration`:** table `reminders`. `Title` and `Message` are required. `TimeLocal` gets `.HasConversion(new ValueConverter<TimeOnly, string>(t => t.ToString("HH:mm", CultureInfo.InvariantCulture), s => TimeOnly.ParseExact(s, "HH:mm", CultureInfo.InvariantCulture)))`. The EF SQLite provider's built-in `TimeOnly` mapping would store `HH:mm:ss.fffffff` text, which is why the explicit converter is needed.
- **`SettingConfiguration`:** table `settings`, `HasKey(s => s.Key)`, columns `key` / `value`.

### 3.4 `Data/DataPaths.cs`

```csharp
public sealed class DataPaths
{
    public DataPaths(string? rootOverride = null);   // default: <LocalApplicationData>\NaggingTimeTracker
    public string Root { get; }
    public string DbPath { get; }        // <Root>\tracker.db
    public string BackupDir { get; }     // <Root>\backups
    public void EnsureCreated();         // Directory.CreateDirectory(Root) and (BackupDir)
}
```

The constructor has no side effects. The override parameter exists now so Phase 2's `--data-dir` only has to pass a value through.

### 3.5 `Data/DesignTimeDbContextFactory.cs`

`IDesignTimeDbContextFactory<TrackerDbContext>` that uses `UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), "ntt-design-time.db")}")`. `migrations add` never opens the file; `migrations script` doesn't either. Keeping it in `%TEMP%` means nothing stray lands in the repo.

### 3.6 `Data/ServiceCollectionExtensions.cs` and `Data/DatabaseInitializer.cs`

```csharp
public static IServiceCollection AddTrackerData(this IServiceCollection services, string dbPath)
```

- `AddDbContextFactory<TrackerDbContext>(o => o.UseSqlite(new SqliteConnectionStringBuilder { DataSource = dbPath, ForeignKeys = true }.ToString()))`.
- Singletons: `ICustomerRepository → CustomerRepository`, `ITaskTypeRepository → TaskTypeRepository`, `IEntryRepository → EntryRepository`, `IReminderRepository → ReminderRepository`, `ISettingsStore → SettingsStore`, and `DatabaseInitializer`.

`DatabaseInitializer(IDbContextFactory<TrackerDbContext> factory)` has one method, `Initialize()`: create a context, call `Database.Migrate()`, then `Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;")`. EF Core 9+ makes `Migrate()` **throw** when the model has changes with no migration (`PendingModelChangesWarning` is an error by default). That is wanted: a forgotten migration fails loudly at startup and in `MigrationTests`.

---

## 4. Repositories (`Data/Repositories/`)

Each repository injects `IDbContextFactory<TrackerDbContext>`. Every method does `using var db = factory.CreateDbContext();` (sync, so `using`, not the parent's `await using`). Reads use `AsNoTracking()`. Writes other than `Insert` use `ExecuteUpdate` / `ExecuteDelete`, so no tracked-entity state can conflict and only the named columns are written.

- **`CatalogRepository<T>`** (abstract, `where T : class, ICatalogItem`), with `CustomerRepository` and `TaskTypeRepository` as empty subclasses that implement the specific interfaces. It uses `db.Set<T>()`.
  - `FindByName` compares `x.Name == name.Trim()`. SQLite applies the column's `NOCASE` collation because the column is on the left.
  - `Insert` does `db.Add(item); db.SaveChanges(); return item;`. EF writes the generated id into the `init` property of that same instance, so the returned record carries its id.
- **`EntryRepository`:**
  - A private `static IQueryable<Entry> ApplyFilter(IQueryable<Entry> q, EntryFilter f)` is shared by `Query` and `QueryViews`. It applies `StartUtc >= f.FromUtc && StartUtc < f.ToUtc`, then the optional customer id and task type id. Text, if `f.Text?.Trim()` isn't empty: escape `\`, `%` and `_` with `\`, then `EF.Functions.Like(e.Notes, p, "\\") || (e.ExternalLink != null && EF.Functions.Like(e.ExternalLink, p, "\\"))` with `p = "%" + escaped + "%"`. That is ASCII case-insensitive, which is SQLite's default `LIKE`.
  - `QueryViews`: `ApplyFilter(db.Entries)` joined to `Customers` and `TaskTypes` on their ids, `select new EntryView(e, c.Name, t.Name)`. The joins don't filter on `Archived`, so archived names still resolve.
  - `GetRecentDistinct(n)`: `Where(StopUtc != null).OrderByDescending(StopUtc).ThenByDescending(Id).Take(200)`, materialised, then `DistinctBy(e => (e.CustomerId, e.TaskTypeId, e.ExternalLink))` (ordinal string comparison; links are already trimmed on write), then `Take(n)`.
  - `GetOverlapping` / `GetOverlappingIds`: exactly the parent's predicate. `GetOverlappingIds` is `from a in Entries where a.StartUtc >= from && a.StartUtc < to from b in Entries where a.Id != b.Id && <overlap(a, b)> select a.Id`, then `.Distinct()` and materialised to a `HashSet<long>`.
  - `Update` / `UpdateNotes` / `Delete`: `ExecuteUpdate` / `ExecuteDelete` filtered by `Id`, returning `rows > 0`. Because `Delete` issues a raw `DELETE`, the `ON DELETE SET NULL` on `continued_from_id` is enforced by SQLite itself. That only works because the connection string sets `Foreign Keys=True`, and a test asserts it.
- **`ReminderRepository`:** straightforward, with the same patterns as above.
- **`SettingsStore`:** `Get` uses `AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefault()`. `Set` is a single atomic upsert: `db.Database.ExecuteSql($"INSERT INTO settings(key, value) VALUES ({key}, {value}) ON CONFLICT(key) DO UPDATE SET value = excluded.value")`. `Remove` uses `ExecuteDelete`.

---

## 5. Migration and schema check

Run `dotnet ef migrations add InitialCreate --project src/NaggingTimeTracker.Data --output-dir Migrations`. The Data project is its own startup project; that works because it references `Microsoft.EntityFrameworkCore.Design` and has the design-time factory.

Then run `dotnet ef migrations script --project src/NaggingTimeTracker.Data -o <scratch>/initial.sql` and check it against parent §2, item by item:

- [x] `customers.name` and `task_types.name` are `COLLATE NOCASE` and have unique indexes.
- [x] `ck_entries_stop_after_start` is present with the exact expression.
- [x] `ix_entries_start` exists, and `ix_entries_running` exists with `WHERE stop_utc IS NULL`.
- [x] The FKs on `customer_id` / `task_type_id` have `ON DELETE RESTRICT` (EF's `DeleteBehavior.Restrict`); `continued_from_id` has `ON DELETE SET NULL`.
- [x] Every timestamp column is `INTEGER`, and `reminders.time_local` is `TEXT`.
- [x] Five `INSERT INTO "task_types"` rows with `created_utc` `1767225600000`.

### Accepted deviations from parent §2

These are differences between EF's output and the parent's illustrative SQL. They are accepted rather than fought, and should be noted in the parent's §2:

1. **`AUTOINCREMENT`** on every integer primary key (EF SQLite always emits it for generated keys). The effect is that ids are never reused after a delete, which is safer for `continued_from_id` links. Seeded ids 1–5 still lead to user ids from 6 up, because an explicit insert advances `sqlite_sequence`.
2. **Unique indexes** (`ux_*_name`) instead of inline `UNIQUE` column constraints. The enforcement is the same.
3. **No `DEFAULT` clauses** (`archived`, `enabled`, `notes`). EF writes every column on insert, so a store default would never be used, and a store default on a `bool` makes EF log a sentinel-value warning. Ad-hoc SQL inserts must supply those columns.
4. **Extra FK indexes** that EF creates automatically (`IX_entries_customer_id`, `IX_entries_task_type_id`, `IX_entries_continued_from_id`). Harmless, and they speed up the joins in `QueryViews`.

---

## 6. App placeholder

The App project builds and runs, but has no UI yet. Phase 2 replaces all of this with the host wiring of parent §1.

- `App.xaml`: no `StartupUri`.
- `App.xaml.cs` `OnStartup`:
  1. `var paths = new DataPaths(); paths.EnsureCreated();`
  2. Build a `ServiceCollection().AddTrackerData(paths.DbPath)` provider and call `DatabaseInitializer.Initialize()`.
  3. `Shutdown(0)`.
  4. On any exception, show a `MessageBox` with the message, then `Shutdown(1)`.
- No `<AssemblyName>` / `<Version>` yet; those are Phase 2 per the parent.

Running it once proves the full chain works against the real `%LOCALAPPDATA%\NaggingTimeTracker\tracker.db`: paths, DI registration, migration and WAL.

---

## 7. Tests (`tests/NaggingTimeTracker.Tests/`)

### 7.1 `TestDb.cs` fixture

`IDisposable`. Each test class creates one per test, in its constructor; xUnit builds a new class instance per test.

- The constructor creates `%TEMP%\ntt-tests\<guid>.db` (making the directory if needed). It builds a `ServiceProvider` via `new ServiceCollection().AddTrackerData(path)`, which exercises the real registration, and calls `DatabaseInitializer.Initialize()`.
- It exposes `Factory` (`IDbContextFactory<TrackerDbContext>`) and `T Get<T>()` (resolves from the provider).
- It also has helpers `Customer AddCustomer(string name)` and `TaskType Type(long id)` (seeded types), and `Entry AddEntry(...)` with sensible defaults, so tests stay short.
- `Dispose`: dispose the provider, call `SqliteConnection.ClearAllPools()`, then delete `<guid>.db`, `<guid>.db-wal` and `<guid>.db-shm`. WAL leaves the side files behind.

Test timestamps are fixed ms-precision `DateTimeOffset` constants (e.g. `T0 = 2026-03-02T09:00Z`) plus offsets, never "now".

### 7.2 Test files and cases

| File | Cases |
|---|---|
| `Core/ClockTests.cs` | `FakeTimeProvider` set to an instant with sub-ms ticks → `NowUtc` drops them; the result's `Offset` is zero even when the provider's local zone isn't UTC. |
| `Core/LocalDateRangeTests.cs` | `to` is inclusive: a single day in UTC gives a 24 h range ending at the next midnight. In `America/New_York`, 2026-03-08 is 23 h and 2026-11-01 is 25 h. Midnight-gap handling uses a `TimeZoneInfo.CreateCustomTimeZone` whose DST starts at 00:00, so the day starts at 01:00 local (deterministic, independent of OS tz data). `toInclusive < from` throws. |
| `Data/MigrationTests.cs` | A fresh DB has `InitialCreate` as its only applied migration; `GetPendingMigrations()` is empty; `HasPendingModelChanges()` is false; 5 seeded task types with the expected names, ids 1–5 and `SeedCreatedUtcMs`. Calling `Initialize()` a second time is a no-op (still 5 types, still one applied migration). `PRAGMA journal_mode` returns `wal`. The first customer inserted after seeding gets id 1 and the first user task type gets id 6. |
| `Data/EpochMsConverterTests.cs` | A ms-truncated value round-trips exactly. An input with a `+02:00` offset is stored as the same instant and read back with offset zero. `PRAGMA table_info(entries)` reports `INTEGER` for `start_utc`/`stop_utc`/`created_utc`/`updated_utc`, and `PRAGMA table_info(reminders)` reports `INTEGER` for `last_fired_utc`. A `null` `StopUtc` round-trips as `null`. `db.Entries.Where(e => e.StartUtc >= a && e.StartUtc < b).OrderBy(e => e.StartUtc).ToQueryString()` contains `"start_utc" >=` and `ORDER BY "e"."start_utc"`, and running it returns the right rows in order. |
| `Data/EntryRepositoryTests.cs` | Insert → `GetById` round-trip of every field. `GetRunning` returns only rows with null stop. `GetRecentStopped` is newest stop first and respects `n`. `GetRecentDistinct`: three stops of the same customer/type/link → one row (the newest); a different link → a separate row; `null` link and `"x"` are distinct; respects `n`. `Query`: the range is half-open (an entry starting exactly at `ToUtc` is excluded, one at `FromUtc` included); filters by customer, by type, and by text in notes and in link; text containing `%` and `_` matches literally. `QueryViews` returns names. `GetOverlapping`: overlap found; touching at either boundary isn't; a running entry is treated as ending at `nowUtc`; null `stopUtc` is treated as `nowUtc`; `excludeId` is honoured. `GetOverlappingIds`: A overlaps B → both ids; touching → neither; an entry outside the range is not reported even if it overlaps one inside. `Update` changes all fields but `CreatedUtc`. `UpdateNotes` leaves `StopUtc` and every other column untouched. Inserting stop ≤ start throws `DbUpdateException` (CHECK). Inserting an unknown `CustomerId` throws (FKs on). Deleting a source nulls `ContinuedFromId` on its continuation. `Update`/`Delete` of a missing id return false. |
| `Data/CatalogRepositoryTests.cs` | Inserting `acme` after `Acme` throws `DbUpdateException` (case-insensitive unique). `FindByName("ACME ")` finds `Acme`. `GetActive` excludes archived and is ordered case-insensitively (`apple`, `Banana`, `cherry`). `GetAll` includes archived. `Rename` and `SetArchived` return true and persist. An entry on an archived customer still resolves its name through `QueryViews`. Run once for `ICustomerRepository` and once for `ITaskTypeRepository` (xUnit `[Theory]` over a repository selector). |
| `Data/ReminderRepositoryTests.cs` | CRUD round-trip. The raw column holds `'09:05'` for `TimeOnly(9, 5)` (read with raw SQL). `SetLastFired` persists. `GetAll` is ordered by time. |
| `Data/SettingsStoreTests.cs` | `Get` of a missing key → null. `Set` then `Get`. A second `Set` overwrites (one row). `Remove` then `Get` → null. `Remove` of a missing key doesn't throw. |

`ReminderRepositoryTests` and `SettingsStoreTests` are additions to the parent's list. The parent puts the `TimeLocal` check in `EntryRepositoryTests`; it moves to `ReminderRepositoryTests`, where it belongs.

---

## Steps

- [ ] Commit the docs first: `plan-time-tracker-v1.md`, this file, and [README.md](README.md) re-saved as UTF-8 without BOM (§1.4)
- [x] Add `global.json` (§1.1)
- [x] Run the §1.3 scaffold commands; delete the template files; pin the template's test packages and drop `coverlet.collector`
- [x] Add `Directory.Build.props`, and the `.editorconfig` migrations section (§1.4); strip duplicated properties from the csprojs
- [x] Add all §1.2 package references at the pinned versions
- [x] Core: `Time/Clock.cs`, `Time/LocalDateRange.cs` (§2.1–2.2)
- [x] Core: `Model/*.cs` and `Abstractions/*.cs` exactly as in §2.3–2.4
- [x] Data: `Converters/EpochMsConverter.cs`, `Entities/SettingRow.cs`, `TrackerDbContext.cs`, and the five `Configurations/*Configuration.cs` (§3.1–3.3)
- [x] Data: `DataPaths.cs`, `DesignTimeDbContextFactory.cs`, `ServiceCollectionExtensions.cs`, `DatabaseInitializer.cs` (§3.4–3.6)
- [x] Data: `Repositories/CatalogRepository.cs` (+ Customer/TaskType subclasses), `EntryRepository.cs`, `ReminderRepository.cs`, `SettingsStore.cs` (§4)
- [x] `dotnet ef migrations add InitialCreate`; run `migrations script` and tick the §5 checklist
- [x] App placeholder (§6); run it once and confirm `%LOCALAPPDATA%\NaggingTimeTracker\tracker.db` is created and the process exits with code 0
- [x] Tests: `TestDb.cs` and the eight files in §7.2
- [x] `dotnet build` (zero warnings, with warnings as errors) and `dotnet test` (all green)
- [x] Tick the parent's Phase 1 checklist in [plan-time-tracker-v1.md](plan-time-tracker-v1.md), add the §5 deviations to its §2, and flip this doc's Status to Shipped
- [ ] Commit "Phase 1 — scaffold + data layer"

---

## Non-goals

- **No services.** `TrackingService`, `CatalogService` (including the `NameTaken` pre-check for renames) and `SettingsService`/`AppSettings` are Phase 2 (parent §12). Phase 1 repositories let the unique index throw.
- **No UI, tray, single-instance handling, command-line switches or Generic Host.** The App is a migrate-and-exit placeholder (§6); all of that is Phase 2.
- **No `BackupService`.** That's Phase 7 (parent §12). `DataPaths.BackupDir` exists only so the path logic lives in one place.
- **No `UiTests` project or installer.** Those are Phases 8 and 9.
- **No CSV code.** `CsvHelper` is referenced but unused until Phase 4.
- **No Unicode case-folding for names.** SQLite `NOCASE` and `LIKE` fold ASCII only, so `Ärzte` and `ärzte` are different customers. Fixing that would need ICU or a custom collation; it's out of scope for v1.
- **No async repository APIs** (§2.4).
- **No central package management** (`Directory.Packages.props`). The parent specifies per-csproj pins; with four projects that's manageable.
- **No hand-editing of the generated migration** to match the parent's illustrative SQL (§5 "Accepted deviations").
