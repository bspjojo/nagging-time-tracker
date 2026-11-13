# Phase 2 — Tracking Service + Tray (implementation plan)

**Status:** Shipped — 1 commit. Build clean, 166/166 tests green, 28 scripted UI Automation checks of §8 pass, and the tray tooltip was confirmed by hand.

**Sequencing note:** this doc expands **Phase 2** of [plan-time-tracker-v1.md](plan-time-tracker-v1.md) (§12, "Phase 2 — Tracking service + tray") into implementable detail. It builds on Phase 1 ([plan-phase1-scaffold-data.md](plan-phase1-scaffold-data.md)), which has shipped (commit `a910fba`). The parent plan stays the source of truth for behaviour: §3 (tracking rules), §4 (tray, quick panel, dialogs, single instance), §8 (settings keys and defaults) and §1 (host wiring, switches, AutomationIds). Where this doc deviates from the parent, it says so in §9 "Deviations from the parent"; fold those back into the parent once this doc is approved.

## Overview

Phase 2 turns the migrate-and-exit placeholder into the real tray app. It covers:

- **Core services:** `TrackingService`, `CatalogService` and `SettingsService`.
- **The tray:** icon, rich tooltip and context menu.
- **The quick panel**, the concurrency dialog and the too-short prompt.
- **Startup plumbing:** single-instance handling, command-line switches and the Generic Host.

At the end you can start, stop and continue timers (including parallel ones with a reason) entirely from the tray, and the data survives a restart.

**What exists today (confirmed by reading the code):**

- **App** is a placeholder, [App.xaml.cs](src/NaggingTimeTracker.App/App.xaml.cs). It builds a bare `ServiceCollection`, runs `DatabaseInitializer` and calls `Shutdown(0)`. The App csproj references `H.NotifyIcon.Wpf` 2.4.1, `CommunityToolkit.Mvvm` 8.4.2 and `Microsoft.Extensions.Hosting` 10.0.15, all unused so far, and has no `<AssemblyName>`/`<Version>` ([NaggingTimeTracker.App.csproj](src/NaggingTimeTracker.App/NaggingTimeTracker.App.csproj)).
- **Repositories** are synchronous ([Repositories.cs](src/NaggingTimeTracker.Core/Abstractions/Repositories.cs)). `ICatalogRepository<T>.Rename` throws `DbUpdateException` on a duplicate name, leaving the pre-check to `CatalogService`. `ISettingsStore` already has `Remove`.
- **Missing query:** `IEntryRepository` has no "most recently **started**" query. It only has `GetRecentStopped`, which orders by stop. The start form's defaults (parent §3, last paragraph) need one, so Phase 2 adds `GetLatestStarted()` (§2.4).
- **Data paths:** `DataPaths(string? rootOverride)` already accepts the `--data-dir` override ([DataPaths.cs](src/NaggingTimeTracker.Data/DataPaths.cs)).
- **Tests** are only possible for `net10.0` code. [NaggingTimeTracker.Tests.csproj](tests/NaggingTimeTracker.Tests/NaggingTimeTracker.Tests.csproj) targets `net10.0` and references Core and Data only, so anything in the `net10.0-windows` App project can't be unit-tested. That is why the pure parts of startup (switch parsing, instance-name derivation) move to Core (§1.1, §9).

**Decisions already made (givens):**

- Everything in the parent's stakeholder list.
- Phase 1's conventions: synchronous repositories, `TimeProvider` + `Clock.NowUtc()`, real SQLite in tests.
- The parent's Phase 2 scope boundaries. Windows that don't exist yet get no menu items, footer links or `--show` values. The Pause reminders submenu writes its setting now but only takes effect in Phase 5. `hasAttention` is always `false` until Phase 5.

**Commit granularity:** one commit, "Phase 2 — tracking service + tray", per the parent's one-commit-per-phase rule. Generated icon files are committed in the same commit as the script that makes them.

---

## 1. Core: startup helpers, formatting, tray selection

All of these are pure and unit-tested.

### 1.1 `Core/Startup/StartupOptions.cs`

```csharp
public enum ShowTarget { None, QuickPanel }   // Entries / Settings / Export added in Phases 3–4

public sealed record StartupOptions(string? DataDir, ShowTarget Show, bool NoBackground)
{
    public static StartupOptions Parse(IReadOnlyList<string> args);   // throws StartupOptionsException
}
public sealed class StartupOptionsException(string message) : Exception(message);
```

- **Accepted forms:** `--data-dir <path>`, `--show <target>` and `--no-background`.
- **Target values:** `quick-panel` is the only one in Phase 2. Matching is case-insensitive; option names are case-sensitive.
- **Errors:** an unknown option, a missing value, an unknown `--show` value or a repeated option throws `StartupOptionsException` with a one-line message. `App` shows that message in a `MessageBox` and exits with code 2.
- **No arguments** gives `(null, None, false)`.
- **`NoBackground`:** parsed and stored, but nothing reads it until Phases 5–6 (`ReminderHost`, `IdleMonitor`).

### 1.2 `Core/Startup/InstanceNames.cs`

```csharp
public sealed record InstanceNames(string Mutex, string ShowEvent)
{
    public static InstanceNames For(string? dataDir);
}
```

- **No `dataDir`:** `Local\NaggingTimeTracker` and `Local\NaggingTimeTracker.Show`.
- **With `dataDir`:** both names get the suffix `.` + the first 8 lowercase hex characters of `SHA256(UTF-8 bytes of Path.GetFullPath(dataDir).TrimEnd('\\').ToUpperInvariant())`. Upper-casing and trimming make `C:\x\` and `c:\X` the same instance, as they are on NTFS. Parent §1 says "a SHA-256 of the full path"; this pins down the normalisation.

### 1.3 `Core/Formatting/DurationFormat.cs`

- `Short(TimeSpan d)` → `"1h 23m"`, `"23m"`, `"0m"`. It uses whole minutes, truncating; negative values clamp to zero. Used by the tooltip.
- `Clock(TimeSpan d)` → `"1:23:45"`, `"0:00:07"`, with unbounded hours (`"26:00:00"`). Used by the quick panel's live elapsed time.

### 1.4 `Core/Tray/TrayIconSelector.cs`

`public static string Select(bool hasAttention, int runningCount)` returns the file name (without directory) from the parent §4.1 table:

| hasAttention | runningCount | Result |
|---|---|---|
| false | 0 | `idle.ico` |
| false | 1 | `tracking-1.ico` |
| false | 2–9 | `tracking-<n>.ico` |
| false | ≥10 | `tracking-9plus.ico` |
| true | 0 / 1 | `attention-0.ico` / `attention-1.ico` |
| true | 2–9 | `attention-<n>.ico` |
| true | ≥10 | `attention-9plus.ico` |

A negative `runningCount` throws `ArgumentOutOfRangeException`. `TrayIconSelector.AllFileNames` (a static list of the 22 names) lets the icon script and a test both check that the set is complete.

---

## 2. Core services

All three services take their dependencies through the constructor and are registered as singletons. They're called on the UI thread in Phase 2. They hold no state between calls apart from event subscribers.

### 2.1 `Core/Services/TrackingResults.cs`

```csharp
public sealed record StartRequest(
    long CustomerId, long TaskTypeId, string? ExternalLink, string? Notes,
    bool ConfirmedConcurrent = false, string? OverlapReason = null);

public abstract record StartResult
{
    public sealed record Started(Entry Entry) : StartResult;
    public sealed record NeedsConfirmation(IReadOnlyList<Entry> Running) : StartResult;
    public sealed record NothingToContinue : StartResult;
}

public sealed record StopResult(Entry Entry, bool TooShort);
```

### 2.2 `Core/Services/TrackingService.cs`

Constructor: `(IEntryRepository entries, TimeProvider time)`. It raises `event EventHandler? RunningEntriesChanged` after every call that changed a row, and never after a call that changed nothing.

The behaviour is exactly as in the parent §3 table, with these clarifications:

**Starting and stopping**

- **`Start(StartRequest req)`:**
  - Reads `GetRunning()`. If it isn't empty and `!req.ConfirmedConcurrent`, returns `NeedsConfirmation(running)` without inserting anything.
  - Otherwise inserts with `StartUtc = CreatedUtc = UpdatedUtc = now`. `ExternalLink` is trimmed, and blank becomes `null`. `Notes` is `req.Notes ?? ""`, not trimmed: whatever the user typed is kept. `OverlapReason` is `req.OverlapReason` trimmed (blank becomes `null`), and only when `running` wasn't empty; otherwise `null`.
- **`Stop(long id)` → `StopResult?`:**
  - Returns `null` if the id doesn't exist.
  - If the entry is already stopped, returns `StopResult(entry, TooShort: false)` without writing or raising the event. This makes a stale UI row's **Stop** harmless.
  - Otherwise writes `stored with { StopUtc = max(now, StartUtc + 1 s), UpdatedUtc = now }` through `IEntryRepository.Update`. `stored` is the row just read by `GetById`, never a caller's copy, so notes saved meanwhile aren't overwritten. `TooShort` is `StopUtc - StartUtc < 1 min`.
- **`StopAll()` / `StopAllExcept(long keepId)`:** `IReadOnlyList<StopResult>`, in `GetRunning()` order. Each list raises the event **once** at the end, if it stopped anything, not once per entry.
- **`StopAt(long id, DateTimeOffset stopUtc)` → `Entry?`:**
  - Returns `null` if the id is missing or the entry isn't running.
  - Throws `ArgumentOutOfRangeException` if `stopUtc <= StartUtc`.
  - Has no callers until Phase 6, but it is implemented and tested now as the parent's Phase 2 checklist requires.

**Continuing**

- **`Continue(long sourceId, bool confirmedConcurrent = false, string? overlapReason = null)`:**
  - **Missing source:** returns `NothingToContinue`.
  - **Running source:** stops the source (same clamp as `Stop`) and inserts the copy. It copies `CustomerId`, `TaskTypeId` and `ExternalLink`, sets `Notes = ""` and `ContinuedFromId = source.Id`. `OverlapReason` is the trimmed `overlapReason`, or else the source's `OverlapReason`. No confirmation is needed.
  - **Stopped source:** the `Start` concurrency rule applies, using the same copy rules. Here `OverlapReason` is set only when others are running.
  - Raises the event once.
- **`ContinueLast(bool confirmedConcurrent = false, string? overlapReason = null)`:** returns `NothingToContinue` if `GetRecentStopped(1)` is empty; otherwise `Continue(that.Id, …)`. The parent shows no parameters, but the confirmation retry needs them.

**Editing**

- **`UpdateNotes(long id, string notes)` → `bool`:** calls `IEntryRepository.UpdateNotes(id, notes, now)`, and raises the event only if a row was updated.
- **`Delete(long id)` → `bool`:** raises the event only if a row was deleted.
- **`Save(Entry e)` → `Entry`:**
  - **Validation:**
    - `ArgumentException` if `StopUtc is { } s && s <= StartUtc`.
    - `InvalidOperationException` if `e.Id == 0 && e.StopUtc is null`.
    - For `e.Id != 0`: loads the stored row. `InvalidOperationException` if it's missing, or if `e.StopUtc is null && stored.StopUtc is not null`.
  - **Writing:** trims the link (blank becomes `null`) and sets `UpdatedUtc = now`. A new entry also gets `CreatedUtc = now`, and is inserted. An existing entry keeps the stored `CreatedUtc` and goes through `Update`.
  - Returns the saved entry. Its first caller is the Phase 3 editor; it's tested now.

### 2.3 `Core/Services/CatalogService.cs`

Constructor: `(ICustomerRepository customers, ITaskTypeRepository taskTypes, TimeProvider time)`.

```csharp
public enum RenameResult { Renamed, NameTaken, NotFound }

Customer GetOrCreateCustomer(string name);
TaskType GetOrCreateTaskType(string name);
RenameResult RenameCustomer(long id, string newName);
RenameResult RenameTaskType(long id, string newName);
bool SetCustomerArchived(long id, bool archived);
bool SetTaskTypeArchived(long id, bool archived);
IReadOnlyList<Customer> GetActiveCustomers();
IReadOnlyList<TaskType> GetActiveTaskTypes();
CatalogNames GetNames();
```

- **`GetOrCreate…`:**
  - Trims the name; empty input throws `ArgumentException`.
  - `FindByName` finds an existing item case-insensitively. If it's archived, `SetArchived(id, false)` and return it with `Archived = false`. The stored name is kept: typing `acme` returns `Acme`.
  - Otherwise inserts with `CreatedUtc = now`.
- **`Rename…`:**
  - Trims the name; empty input throws `ArgumentException`.
  - Returns `NotFound` if the id is missing (checked through `GetAll()`).
  - Returns `NameTaken` if `FindByName(newName)` returns a **different** id.
  - Otherwise renames and returns `Renamed`. A case-only change of the item's own name (`acme` → `Acme`) is allowed.
- **`GetActive…`:** pass-throughs for the pickers. `GetNames()` loads `GetAll()` of both catalogs into a `CatalogNames` record, which has `string Customer(long id)` and `string TaskType(long id)` (`"?"` when the id is unknown). The tooltip and quick panel use it to label running and recent rows. Both tables are tiny, so one load per refresh is cheaper than a join per row.

### 2.4 `IEntryRepository.GetLatestStarted()`

Add `Entry? GetLatestStarted();` to [Repositories.cs](src/NaggingTimeTracker.Core/Abstractions/Repositories.cs). The implementation in [EntryRepository.cs](src/NaggingTimeTracker.Data/Repositories/EntryRepository.cs) is `OrderByDescending(StartUtc).ThenByDescending(Id).FirstOrDefault()`, running or stopped. The quick panel uses it to pre-fill Customer and Task type. On a fresh DB it returns `null` and both fields stay empty.

### 2.5 `Core/Settings/`

- **`SettingKeys.cs`:** `const string` for every key in parent §8.
- **`AppSettings.cs`:** a `sealed record` holding only the user-configurable values, each initialised to the §8 default:

  ```csharp
  public sealed record AppSettings
  {
      public int WorkDaysMask { get; init; } = 62;
      public TimeOnly WorkStart { get; init; } = new(9, 0);
      public TimeOnly WorkEnd { get; init; } = new(17, 0);
      public bool NagNotTrackingEnabled { get; init; } = true;
      public int NagNotTrackingIntervalMin { get; init; } = 15;
      public bool CheckinEnabled { get; init; } = true;
      public int CheckinIntervalMin { get; init; } = 60;
      public bool IdleEnabled { get; init; } = true;
      public int IdleThresholdMin { get; init; } = 10;
  }
  ```

- **`SettingsService.cs`:** constructor `(ISettingsStore store)`.
  - **`AppSettings Load()`:** each key falls back to its default when it's missing **or unparsable**. A hand-edited bad value never crashes startup.
  - **`void Save(AppSettings s)`:**
    - Validates first: `WorkDaysMask` in 0–127, every interval and the threshold ≥ 1. Anything else throws `ArgumentOutOfRangeException`, before writing anything.
    - Then writes all nine keys and raises `event EventHandler? SettingsChanged`.
    - `WorkStart == WorkEnd` isn't rejected here. That is Phase 5's `WorkSchedule` rule, enforced by its UI.
  - **`DateTimeOffset? GetInstant(string key)`** / **`void SetInstant(string key, DateTimeOffset? value)`:** for the state keys `heartbeat_utc`, `reminders_paused_until_utc` and `not_working_until_utc`. Values are stored as epoch-ms decimal text; `null` calls `Remove`, and an unparsable value reads as `null`. These don't raise `SettingsChanged`, because the heartbeat writes every 60 s from Phase 5.
  - **Formats** (always `CultureInfo.InvariantCulture`): ints as `ToString(CultureInfo.InvariantCulture)`, bools as `"true"`/`"false"`, times as `"HH:mm"`.

The state keys are deliberately not in `AppSettings`. Saving the user's settings must never overwrite a heartbeat or pause value written meanwhile, and writing a heartbeat must not raise `SettingsChanged`. This is a deviation from parent §8, which lists all keys in one table (§9).

---

## 3. App startup, host and single instance

### 3.1 App csproj

Add these to [NaggingTimeTracker.App.csproj](src/NaggingTimeTracker.App/NaggingTimeTracker.App.csproj):

- `<AssemblyName>NaggingTimeTracker</AssemblyName>` and `<Version>1.0.0</Version>`, per parent §1.
- `<RootNamespace>NaggingTimeTracker.App</RootNamespace>`. This keeps the XAML `x:Class` namespaces unchanged now that the assembly name differs from the project name.
- `<Resource Include="Assets\tray\*.ico" />`.

### 3.2 `App.xaml` / `App.xaml.cs`

`App.xaml` sets `ShutdownMode="OnExplicitShutdown"` and has no `StartupUri`. `OnStartup` runs these steps in order:

1. **Parse switches.** `StartupOptions.Parse(e.Args)`. On `StartupOptionsException`: `MessageBox`, then `Shutdown(2)`.
2. **Single instance.** `SingleInstance.TryAcquire(InstanceNames.For(options.DataDir))`. If another instance holds the mutex, signal its show event and `Shutdown(0)`. This happens **before** any database work, so a second launch never migrates concurrently.
3. **Paths.** `var paths = new DataPaths(options.DataDir); paths.EnsureCreated();`
4. **Host.** `Host.CreateApplicationBuilder()`, registering:
   - `TimeProvider.System`, `options` and `paths` as singletons;
   - `AddTrackerData(paths.DbPath)`;
   - `TrackingService`, `CatalogService`, `SettingsService`, `TrackingActions`, `TrayHost` and `QuickPanelViewModel` as singletons;
   - `QuickPanelWindow` as transient. Only `TrayHost` resolves it, once.

   Logging is cleared (`builder.Logging.ClearProviders()`), because the app has no console and the EF query logs would just cost time.
5. **Database.** `host.Services.GetRequiredService<DatabaseInitializer>().Initialize()`.
6. **Start.** `host.StartAsync().GetAwaiter().GetResult()`. There are no hosted services yet, but parent §1's order is kept so Phase 5 only has to add them.
7. **Tray.** `TrayHost.Initialize()` creates the icon, then `SingleInstance.ListenForShow(() => trayHost.ShowQuickPanel())`.
8. **`--show`.** If `options.Show == QuickPanel`, call `trayHost.ShowQuickPanel()`.

**Errors and exit:**

- **Failure in steps 3–8:** a `MessageBox` with the message, then `Shutdown(1)`. This is the same behaviour as today's placeholder.
- **`DispatcherUnhandledException`:** a `MessageBox` with the message, then `e.Handled = true`, and the app keeps running. Every operation uses its own short-lived `DbContext`, so a failed one leaves no broken shared state.
- **`OnExit`:** dispose `TrayHost` (removes the icon immediately, so no ghost icon is left behind), then `host.StopAsync().GetAwaiter().GetResult()`, dispose the host, and release `SingleInstance`.

### 3.3 `App/SingleInstance.cs`

An `IDisposable` class.

- **`static SingleInstance? TryAcquire(InstanceNames names)`:** creates `new Mutex(initiallyOwned: true, names.Mutex, out var createdNew)`.
  - If `!createdNew`: opens the existing `EventWaitHandle` (`EventWaitHandle.TryOpenExisting(names.ShowEvent, …)`), calls `Set()` and returns `null`. If the event doesn't exist yet (the first instance is still starting), it just returns `null`.
  - Otherwise it creates `new EventWaitHandle(false, EventResetMode.AutoReset, names.ShowEvent)` and returns the instance.
- **`void ListenForShow(Action onShow)`:** starts a background thread (`IsBackground = true`) that loops on `WaitHandle.WaitAny([showEvent, stopEvent])`. Each show signal is marshalled with `Application.Current.Dispatcher.BeginInvoke(onShow)`.
- **`Dispose`:** sets the private stop event, joins the thread (1 s timeout), `ReleaseMutex()` and disposes the handles. A mutex must be released on the thread that acquired it. `TryAcquire` (in `OnStartup`) and `Dispose` (in `OnExit`) both run on the UI thread.

---

## 4. Tray

### 4.1 `tools/make-icons.ps1` and `App/Assets/tray/`

The script is for Windows PowerShell 5.1 (`#Requires -Version 5.1`; `Add-Type -AssemblyName System.Drawing`). It writes the 22 files from §1.4 into `src/NaggingTimeTracker.App/Assets/tray/`.

- **Each file** contains four images, 16, 20, 24 and 32 px square. The 20 and 24 px images cover 125% and 150% display scaling without blurry rescaling. Each is drawn on a transparent 32-bpp `Bitmap` with `SmoothingMode.AntiAlias` and `TextRenderingHint.AntiAliasGridFit`.
- **The circle** is filled with an inset of `size / 32` px.
- **Colours:** grey `#8A8A8A` (idle), green `#2E9E44` (tracking-1), amber `#E8A000` (tracking-2…9plus), red `#D93025` (attention-*).
- **The digit** is white `Segoe UI` Bold, centred with `StringFormat` Center/Center. Its size is `0.69 × size` px (11 px at 16), and `9+` uses `0.5 × size` px so it fits.
- **ICO writing:** each bitmap is saved to PNG in memory, and the script writes the ICO container by hand. That is an `ICONDIR` (reserved 0, type 1, count 4), four 16-byte `ICONDIRENTRY` records (width, height, 0 colours, 0, 1 plane, 32 bpp, byte size, offset) and then the four PNG blobs. PNG-compressed ICO entries are supported since Windows Vista. The script deliberately avoids `Icon.FromHandle`, which degrades the alpha channel.
- **Completeness check:** the script ends by printing the file count. The test `TrayIconSelectorTests.All_icon_files_exist` checks the set against `AllFileNames`; the Tests project gets the 22 files as content via `<None Include="..\..\src\NaggingTimeTracker.App\Assets\tray\*.ico" Link="tray\%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />`.

### 4.2 `App/Tray/TrayHost.cs`

`IDisposable`. Constructor: `(TrackingService tracking, CatalogService catalog, SettingsService settings, TrackingActions actions, IServiceProvider services, TimeProvider time)`.

**`Initialize()`** creates the `TaskbarIcon` in code:

- `ToolTipText = "Nagging Time Tracker"` (the fallback), `TrayToolTip = new TrayToolTipView()`, `NoLeftClickDelay = true` and `ContextMenu = BuildMenu()`.
- It then calls `ForceCreate(enablesEfficiencyMode: false)`. H.NotifyIcon requires `ForceCreate` for an icon that isn't part of a XAML visual tree. Its default argument (`true`) would put the whole process into Windows 11 *efficiency mode* (EcoQoS), which throttles timers, so reminders and idle checks could fire late from Phase 5 on.
- Then it subscribes to `tracking.RunningEntriesChanged` → `Refresh()`, starts a 30-s `DispatcherTimer` → `RefreshToolTip()`, and calls `Refresh()` once.

**`UpdateIcon()`:**

- Selects `TrayIconSelector.Select(hasAttention: false, runningCount)`. The selected file is loaded once with `Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/tray/{file}"))` and `new System.Drawing.Icon(stream, size, size)`, then cached in a dictionary.
  - `size` is `GetSystemMetrics(SM_CXSMICON = 49)`, the tray icon size in physical pixels at the current system DPI. It is declared as `[DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);` in `App/Tray/NativeMethods.cs`.
  - WinForms' `SystemInformation.SmallIconSize` isn't used, because the App project doesn't reference WinForms. `System.Drawing.Icon` itself is available, because `TaskbarIcon.Icon` is typed as it.
- It assigns `TaskbarIcon.Icon`.
- `hasAttention` is a private field that is always `false` in Phase 2; Phase 5's `NagWindowManager` sets it.

**`RefreshToolTip()`** fills `TrayToolTipView`'s bound properties from `GetRunning()` + `catalog.GetNames()` + `time.NowUtc()`, per parent §4.1:

- 0 running: *Not tracking*.
- 1 running: `{customer} · {type} — {DurationFormat.Short(elapsed)}`.
- ≥2 running: the amber header `⚠ {n} timers running in parallel`, then one line per entry in the same `{customer} · {type} — {elapsed}` form.

Customer comes first everywhere, the same as in the quick panel. Parent §4.1's single-timer example (*Dev Work · Acme*) is changed to match (§9).

**Left click.** `TrayLeftMouseUp` → `ShowQuickPanel()`, with a toggle guard. Clicking the icon while the panel is open deactivates the panel, so it hides itself, just before the click arrives. If the panel was hidden by `Deactivated` less than 300 ms before the click (`QuickPanelWindow.LastAutoHideUtc`), the click is treated as "close" and the panel isn't shown again.

**`ShowQuickPanel()`:** resolves the `QuickPanelWindow` once (lazy), then `vm.Refresh()`, `Show()`, `Activate()`. It's public, because the single-instance signal and `--show` call it now, and the nag window in Phase 5.

**Context menu (`BuildMenu()`):** a WPF `ContextMenu` built in code. Its items carry the AutomationIds given here, although the tray menu itself isn't UI-tested (parent §10).

| Item | AutomationId | Action |
|---|---|---|
| Start new… | `TrayMenu.StartNew` | `ShowQuickPanel()` |
| Stop all | `TrayMenu.StopAll` | `tracking.StopAll()`; enabled only when anything is running |
| Continue last | `TrayMenu.ContinueLast` | `actions.ContinueLast(owner: null)`; enabled only when `GetRecentStopped(1)` isn't empty |
| — | | |
| Pause reminders ▸ | `TrayMenu.Pause` | submenu below |
| — | | |
| Exit | `TrayMenu.Exit` | `RequestExit()` |

- **When enablement is computed:** in the menu's `Opened` handler, so it's always current.
- **Pause reminders ▸** has three items:
  - **1 hour** (`TrayMenu.Pause1h`): `SetInstant(reminders_paused_until_utc, now + 1 h)`.
  - **Until tomorrow** (`TrayMenu.PauseTomorrow`): sets it to the next local midnight, computed as `LocalDateRange.ToUtcRange(today, today, time.LocalTimeZone).ToUtc`, where `today` is `DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, tz).DateTime)`. This reuses the DST-safe midnight logic.
  - **Resume** (`TrayMenu.Resume`): `SetInstant(…, null)`. Enabled only while the stored value is in the future.

  When paused, the submenu header reads *Pause reminders (until HH:mm)*, or *(until tomorrow)* when the value is the next local midnight. The setting has no effect until Phase 5.
- **Not added yet:** *All entries…*, *Export CSV…* and *Settings…*. They come with their windows in Phases 3, 4 and 3, per parent §12, together with their separator.

**`RequestExit()`:**

- If `GetRunning().Count is var n and n > 0`, it asks with `MessageBox` (Yes/No, Warning icon) using the parent §4.2 text. The text is *"1 timer is still running. It will keep counting…"* when `n == 1`, and *"N timers are still running. They will keep counting while the app is closed. Exit anyway?"* otherwise.
- On Yes, or when nothing is running, it calls `Application.Current.Shutdown()`.
- The quick panel's modal guard isn't involved, since the panel is hidden when the menu is used.
- Windows logoff/shutdown goes through `SessionEnding` and never prompts. WPF shuts down on its own.

**`Dispose`:** stops the timer, unsubscribes from events, disposes the `TaskbarIcon` and the cached `Icon`s.

### 4.3 `App/Tray/TrayToolTipView.xaml`

A `UserControl` with a bordered, padded panel that uses the system tooltip brushes (`SystemColors.InfoBrushKey` / `InfoTextBrushKey`). It contains:

- a `TextBlock` header, bold and amber (`#B07800`), visible only with ≥2 running;
- an `ItemsControl` of line strings;
- a *Not tracking* `TextBlock` for 0 running.

Its view-model is a small `ObservableObject` (`TrayToolTipViewModel`) that `TrayHost` updates.

---

## 5. Tracking actions (shared UI flow)

`App/Tracking/TrackingActions.cs`, a singleton, holds the confirm/retry and too-short logic. The quick panel and tray menu use it in Phase 2; the nag window (Phase 5) and Entries window (Phase 3) reuse it.

```csharp
StartResult Start(StartRequest req, Window? owner);
StartResult Continue(long sourceId, Window? owner);
StartResult ContinueLast(Window? owner);
void StopSingle(long id, Window? owner);   // explicit single stop: offers the TooShort prompt
```

- **`Start` / `Continue` / `ContinueLast`:** call the service. On `NeedsConfirmation(running)`, show `ConcurrencyDialog` (owner set if given) and act on the choice:
  - **StopOthers:** `tracking.StopAll()`, then retry with `confirmedConcurrent: true` and no reason.
  - **RunInParallel:** retry with `confirmedConcurrent: true` and `overlapReason: dialog.Reason` (trimmed; blank becomes `null`).
  - **Cancel:** return the original `NeedsConfirmation`.

  A `Continue` of a running source never reaches the dialog (§2.2).
- **`StopSingle`:** `tracking.Stop(id)`. If the result has `TooShort`, show `TooShortDialog`; **Discard** → `tracking.Delete(id)`, **Keep** → nothing.
- **Owners:** every dialog is shown with `ShowDialog()`. The caller is responsible for its own modal guard (the quick panel's `BeginModal()`, §6.2). `TrackingActions` takes no dependency on any window type.

### 5.1 `App/Windows/ConcurrencyDialog.xaml`

A modal window: `WindowStartupLocation=CenterOwner` (or `CenterScreen` without an owner), `Topmost`, `ResizeMode=NoResize` and `SizeToContent`. It is constructed directly as `new ConcurrencyDialog(running, names)`, not through DI, because it takes arguments.

**Contents:**

- The text *"Another timer is already running:"* (or *"{n} timers are already running:"*).
- A list of `customer · type — elapsed` lines.
- The label *"Why are these overlapping? (optional)"* and a textbox, always visible.
- Three buttons: **Stop others & start**, **Run in parallel** (`IsDefault`) and **Cancel** (`IsCancel`).

**Result:** `Choice` (enum `ConcurrencyChoice { StopOthers, RunInParallel, Cancel }`) and `Reason`. Closing the window counts as Cancel.

**AutomationIds:** `ConcurrencyDialog.RunningList`, `.ReasonBox`, `.StopOthersButton`, `.RunInParallelButton`, `.CancelButton`.

### 5.2 `App/Windows/TooShortDialog.xaml`

The same modal style. Text: *"This entry was shorter than a minute ({customer} · {type}, {seconds}s). Keep it?"* Buttons: **Discard** and **Keep** (`IsDefault`, `IsCancel`). Closing the window counts as Keep, so the data is never lost by accident.

**AutomationIds:** `TooShortDialog.DiscardButton`, `.KeepButton`.

---

## 6. Quick panel

### 6.1 `App/ViewModels/QuickPanelViewModel.cs` (CommunityToolkit.Mvvm)

**State:**

- `ObservableCollection<RunningRowViewModel> Running`, `bool ShowParallelBanner` (`Running.Count >= 2`) and `string ParallelBannerText` (`⚠ {n} timers running in parallel`).
- The start form: `IReadOnlyList<string> CustomerNames`, `TaskTypeNames` (active items, by name), `string CustomerText`, `TaskTypeText`, `ExternalLinkText`, `NotesText`, and `string? RequiredError`.
- `ObservableCollection<RecentRowViewModel> Recent`.

**`Refresh()`:**

- Reloads the names, `GetRunning()` and `GetRecentDistinct(5)`.
- The Running rows are **reconciled by entry id**: existing row view-models are updated in place, missing ones removed and new ones appended in `GetRunning()` order. A refresh caused by one row's notes save therefore doesn't recreate the textbox that is being typed into, and doesn't steal focus.
- When the form is pristine (`CustomerText` and `TaskTypeText` both empty), it pre-fills them from `GetLatestStarted()`.

**`RunningRowViewModel`:**

- `Id`, `Label` (`customer · type`), `string Elapsed` (`DurationFormat.Clock`), `string? OverlapReason`, `string Notes` (two-way), `string savedNotes`, `bool ShowKeepOnly` (set by the parent view-model to `Running.Count >= 2`).
- `FlushNotes()` calls `tracking.UpdateNotes` only when `Notes != savedNotes`, then sets `savedNotes = Notes`.

**`RecentRowViewModel`:** `Id`, `Label` (`customer · type` plus ` · {link}` when the link is set).

**Commands** (each runs inside the window's `BeginModal()` scope, §6.2):

- **`StartCommand`:**
  - If either combo text is blank (after trimming), set `RequiredError = "Customer and task type are required."` and stop.
  - Otherwise clear the error, resolve both names through `GetOrCreate…`, and call `actions.Start(new StartRequest(c.Id, t.Id, ExternalLinkText, NotesText), owner)`.
  - On `Started`, clear the link and notes. Customer and type stay, because they are now the latest started.
  - On `Cancel` nothing changes. A customer or type created by `GetOrCreate` stays created (see Non-goals).
- **`StopCommand(row)`:** `row.FlushNotes()`, then `actions.StopSingle(row.Id, owner)`. Without the flush, notes typed but not yet saved would be lost when the row disappears.
- **`KeepOnlyCommand(row)`:** flush every row's notes, then `tracking.StopAllExcept(row.Id)`.
- **`ContinueCommand(recentRow)`:** `actions.Continue(recentRow.Id, owner)`.

Each command calls `Refresh()` at the end. Most refreshes come anyway through the `RunningEntriesChanged` subscription, which the view-model makes in its constructor; a refresh after a cancelled dialog covers the case where nothing changed.

**Elapsed ticking:** one `DispatcherTimer` (1 s) runs while the window is visible (started on `IsVisibleChanged` true, stopped on false). Each tick updates `Elapsed` on every row from `time.NowUtc() - StartUtc`; it doesn't query the database.

### 6.2 `App/Windows/QuickPanelWindow.xaml(.cs)`

**Window properties:** `WindowStyle=None`, `ResizeMode=NoResize`, `AllowsTransparency=False`, `Topmost=True`, `ShowInTaskbar=False`, `Width=360`, `SizeToContent=Height`, a 1-px border, and `MaxHeight = SystemParameters.WorkArea.Height - 16`, with the content in a `ScrollViewer`.

**Placement:** on `SizeChanged` and before every show, `Left = WorkArea.Right - ActualWidth - 8` and `Top = WorkArea.Bottom - ActualHeight - 8`. These are DIPs, which is what `SystemParameters.WorkArea` returns.

**Modal guard:**

```csharp
private int _modalDepth;
public IDisposable BeginModal() { _modalDepth++; return new Releaser(() => _modalDepth--); }
```

The view-model gets the window as its `owner` and enters `BeginModal()` through an `IModalHost` interface (`Window Owner { get; }`, `IDisposable BeginModal()`) that the window implements. The interface is set by the window's constructor (`vm.Host = this`), which keeps the view-model free of a concrete window type.

**Hiding:**

- **`Deactivated`:** if `_modalDepth > 0`, return. Otherwise flush the notes of every running row, `Hide()`, and set `LastAutoHideUtc = DateTime.UtcNow`. This is the click-toggle guard from §4.2; it's UI timing, not domain time, so `TimeProvider` isn't needed.
- **Esc** (`PreviewKeyDown`) does the same as `Deactivated`.
- **Closing:** `e.Cancel = true; Hide();`. The panel lives for the life of the app; `Alt+F4` only hides it.

**Notes:** each row's notes `TextBox` (`AcceptsReturn=False`, `MaxLines=1`, `TextWrapping=NoWrap`) calls `row.FlushNotes()` on `LostKeyboardFocus`.

**Layout,** top to bottom:

1. **Running.** The header *Running*, the amber banner (`ShowParallelBanner`), then one row per entry. Each row has the label and elapsed time; the overlap reason in small grey text, if set; the notes textbox; **Stop**; and **Keep only this** (visible with `ShowKeepOnly`). With nothing running, the section shows *Nothing running*.
2. **Start new.** Two editable combos (`IsEditable=True`, `IsTextSearchEnabled=True`, `Text` bound two-way to `CustomerText` / `TaskTypeText`, `ItemsSource` the names), a link textbox, a notes textbox, the inline `RequiredError` text (red), and **Start** (`IsDefault=True`, so Enter starts).
3. **Recent.** Up to five rows, each a label and a **Continue** button; *No recent entries* when empty.
4. **Footer.** No links in Phase 2 (parent §12). The *All entries… / Export… / Settings…* links arrive with their windows.

**AutomationIds** (parent §1 convention `<Window>.<Control>`; row controls are inside `ItemsControl` templates and carry the same id in every row, so UI tests find rows first and then their children):

- Window and sections: `QuickPanel` (the window), `QuickPanel.ParallelBanner`, `QuickPanel.RunningList`, `QuickPanel.NothingRunning`.
- Running rows: `QuickPanel.RunningRow`, `QuickPanel.RunningRow.Label`, `QuickPanel.RunningRow.Elapsed`, `QuickPanel.RunningRow.OverlapReason`, `QuickPanel.RunningRow.Notes`, `QuickPanel.RunningRow.StopButton`, `QuickPanel.RunningRow.KeepOnlyButton`.
- Start form: `QuickPanel.CustomerCombo`, `QuickPanel.TaskTypeCombo`, `QuickPanel.LinkBox`, `QuickPanel.NotesBox`, `QuickPanel.RequiredError`, `QuickPanel.StartButton`.
- Recent: `QuickPanel.RecentList`, `QuickPanel.RecentRow`, `QuickPanel.RecentRow.Label`, `QuickPanel.RecentRow.ContinueButton`, `QuickPanel.NoRecent`.

---

## 7. Tests (`tests/NaggingTimeTracker.Tests/`)

Service tests use `TestDb` (Phase 1) for the repositories and a `FakeTimeProvider` starting at `TestDb.T0`. A small `Services` helper in each test class builds `new TrackingService(db.Entries, time)` and so on directly; DI isn't needed.

| File | Cases |
|---|---|
| `Core/StartupOptionsTests.cs` | No args → defaults. All three switches parsed. `--show QUICK-PANEL` (case-insensitive value). Each error throws `StartupOptionsException`: unknown option, `--data-dir` without a value, `--show entries` (not valid until Phase 3), repeated option. |
| `Core/InstanceNamesTests.cs` | `null` → the plain names. Same dir with different case and a trailing `\` → same names. Different dirs → different names. The suffix is 8 lowercase hex characters. |
| `Core/DurationFormatTests.cs` | `Short`: 0 → `0m`, 59 s → `0m`, 23 m → `23m`, 83 m → `1h 23m`, negative → `0m`. `Clock`: 7 s → `0:00:07`, 26 h → `26:00:00`. |
| `Core/TrayIconSelectorTests.cs` | Every row of the §1.4 table, including 9 → `tracking-9`, 10 → `9plus`, attention 0/1/2/10. Negative throws. `AllFileNames` has 22 distinct names, and **every one exists** in the linked `tray\` output folder. |
| `Core/SettingsServiceTests.cs` | An empty store → all defaults. `Save` then `Load` round-trip. Unparsable stored values (`"abc"`, `"25:99"`) → defaults. `Save` under `CultureInfo.CurrentCulture = de-DE` stores `"62"`, `"09:00"` and `"true"`, verified through raw `ISettingsStore.Get`. Out-of-range values throw and write nothing. `SettingsChanged` is raised by `Save` only. `SetInstant` / `GetInstant` round-trip; `null` removes the key; unparsable → `null`; `SetInstant` doesn't raise `SettingsChanged`. |
| `Core/CatalogServiceTests.cs` | `GetOrCreate` trims and creates; a second call in a different case returns the same id and the original name; it un-archives an archived match; empty/whitespace throws. Rename to another row's name in a different case → `NameTaken`. Rename to a case change of its own name → `Renamed`. Rename of a missing id → `NotFound`. Archive/unarchive toggles `GetActive…`. `GetNames()` resolves archived items, and unknown ids → `"?"`. Each case runs for both customers and task types where both apply. |
| `Core/TrackingServiceTests.cs` | See the list below. |
| `Data/EntryRepositoryTests.cs` (add) | `GetLatestStarted`: `null` on an empty DB; returns the latest by start whether running or stopped; ties broken by the higher id. |

`TrackingServiceTests.cs` cases, all with `FakeTimeProvider`:

- **Start:**
  - Start then stop; times are `NowUtc`.
  - Start trims the link, and a blank link becomes `NULL`.
  - Start with one running and no confirmation → `NeedsConfirmation` with that entry, and nothing inserted.
  - Confirmed start stores the trimmed `overlap_reason`.
  - A confirmed start with nothing running stores no reason.
- **Stop:**
  - `TooShort` at 59 s, but not at 60 s.
  - The stop clamp: when `now <= start` (time moved backwards), stop = start + 1 s.
  - Stop on an already-stopped entry → no write and no event.
  - Stop on a missing id → `null`.
  - `StopAll` stops all and raises the event once.
  - `StopAllExcept` leaves only the given entry running.
  - `StopAt` rejects `<= start`, and returns `null` for a stopped entry.
- **Continue:**
  - Continuing a stopped entry while another runs needs confirmation.
  - Continuing a running entry stops it, needs no confirmation and copies `overlap_reason`.
  - An explicit reason on continue overrides the copied one.
  - Continue copies customer/type/link, with empty notes, and sets `continued_from_id`.
  - `ContinueLast` on an empty DB → `NothingToContinue`.
  - `ContinueLast` picks the latest **stopped**, not the latest started.
- **Editing:**
  - `UpdateNotes` on an entry stopped in the meantime keeps it stopped.
  - `UpdateNotes` of a missing id → `false`, no event.
  - `Save` rejects a new entry without stop, rejects re-opening an entry stopped in the DB, rejects stop ≤ start, and rejects a missing id.
  - `Save` of a new entry sets created/updated to now; `Save` of an existing entry keeps `CreatedUtc`.
- **Events:** raised after start/stop/continue/delete/save/updateNotes, and not after any no-op.

No UI tests in this phase (Phase 8), but every control gets its AutomationId now (§6.2, §5).

---

## 8. Manual verification (record the results in the commit message)

Run `dotnet run --project src/NaggingTimeTracker.App -- --data-dir %TEMP%\ntt-manual`, then check:

1. **Startup:** a grey icon appears, with tooltip *Not tracking*. Left-click opens the panel at the bottom-right. Clicking the icon again closes it (toggle guard). Esc and clicking elsewhere hide it.
2. **Validation:** Start with empty fields → *Required*. Type `Acme` / `Dev Work` → Start → a green icon and a running row with ticking elapsed time. The tooltip shows *Acme · Dev Work — 0m*.
3. **Too short:** Stop within a minute → the TooShort prompt. **Discard** removes the entry, and Recent stays empty. Start again and Stop after a minute → no prompt, and the entry appears in Recent.
4. **Parallel:** Start a second timer → the concurrency dialog. **Run in parallel** with reason `pairing` → an amber icon with `2`, the banner, the reason under the row, and the tooltip header. **Keep only this** → one row, and the icon turns green.
5. **Continue:** **Continue** on Recent while nothing is running → a new running row. Tray menu **Continue last** → the confirmation dialog. **Stop others & start** → exactly one running.
6. **Pause:** Pause reminders ▸ 1 hour → the header shows *(until HH:mm)*. Resume clears it.
7. **Exit and restart:** Exit with a timer running → the confirmation. **Yes** → the icon disappears immediately. Relaunch → the timer is still running with its full elapsed time.
8. **Single instance:** launch a second copy with the same `--data-dir` → no second icon, and the first instance's panel opens. A copy with a different `--data-dir` runs separately.
9. **Bad switch:** `--show entries` → an error box, and exit code 2.

---

### Implementation notes

- **Icons aren't cached.** `TaskbarIcon` disposes the previous `Icon` whenever `TaskbarIcon.Icon` changes. The first implementation cached icons and handed a disposed one back on the next state change; the exception surfaced inside `Stop`. `LoadIcon` now creates a fresh `Icon` on every change, and `TaskbarIcon` owns and disposes it.
- **Row ids sit on the item containers.** The quick panel's `QuickPanel.RunningRow` / `QuickPanel.RecentRow` AutomationIds are set on the `ItemsControl` item container (`ContentPresenter`, through `ItemContainerStyle`). A `Border` or `DockPanel` has no UI Automation peer, so an id there is invisible to UIA.
- **Owned dialogs are nested in the UIA tree.** UI Automation lists an owned dialog (`ConcurrencyDialog`, `TooShortDialog`) as a child of its owner window, not of the desktop. Phase 8's `AppFixture` must search there too.
- **The native tooltip carries the status too.** On Windows 11 (checked on this machine), H.NotifyIcon's rich `TrayToolTip` is created and `SupportsCustomToolTips` is `true`, yet hovering shows the native tooltip. That was the fixed text "Nagging Time Tracker", which hid the status. `TrayHost.RefreshToolTip()` now also sets `TaskbarIcon.ToolTipText` from `Core/Tray/TrayToolTipText.Build`. It shows the same lines as the rich tooltip, fitted to the native 127-character limit with a trailing *…and N more* (tested in `TrayToolTipTextTests.cs`). The rich tooltip stays for systems where it does open.
- **`TrackingService.HasStoppedEntries()`** was added for the tray menu's *Continue last* enablement.

## 9. Deviations from the parent (fold into [plan-time-tracker-v1.md](plan-time-tracker-v1.md))

1. **`StartupOptions` moves to Core** (`Core/Startup/StartupOptions.cs`, plus `InstanceNames.cs`). Parent §1 and §12 put it in `App/`. This lets the test project (`net10.0`) test parsing and name derivation; `App/SingleInstance.cs` stays in App.
2. **`IEntryRepository.GetLatestStarted()` is new.** Parent §3's start-form defaults need it, and parent §2's repository list didn't have it.
3. **The state keys are separate from `AppSettings`** (`GetInstant`/`SetInstant`). Parent §8 lists them in one table with the user settings.
4. **Precise return shapes** that parent §3 leaves open:
   - `Stop` → `StopResult?`; it is a no-op on an already-stopped entry.
   - `StopAt` → `Entry?`.
   - `UpdateNotes` / `Delete` → `bool`.
   - `ContinueLast` takes `confirmedConcurrent` / `overlapReason`.
   - Bulk stops raise `RunningEntriesChanged` once.
5. **`App/Tracking/TrackingActions.cs` and `App/Windows/TooShortDialog.xaml` are new names.** The parent describes the confirm-and-retry flow and the Discard/Keep prompt but doesn't name where they live.
6. **The tooltip puts the customer first for a single timer too.** Parent §4.1's example becomes *Acme · Dev Work — 1h 23m* (stakeholder decision, for consistency with the multi-timer lines and the quick panel).

---

## Steps

- [x] Core: `Startup/StartupOptions.cs`, `Startup/InstanceNames.cs` (§1.1–1.2)
- [x] Core: `Formatting/DurationFormat.cs`, `Tray/TrayIconSelector.cs` with `AllFileNames` (§1.3–1.4)
- [x] Core: `Services/TrackingResults.cs`, `Services/TrackingService.cs` (§2.1–2.2)
- [x] Core: `Services/CatalogService.cs` with `RenameResult` and `CatalogNames` (§2.3)
- [x] Core + Data: add `GetLatestStarted()` to `IEntryRepository` in [Repositories.cs](src/NaggingTimeTracker.Core/Abstractions/Repositories.cs) and implement it in [EntryRepository.cs](src/NaggingTimeTracker.Data/Repositories/EntryRepository.cs) (§2.4)
- [x] Core: `Settings/SettingKeys.cs`, `Settings/AppSettings.cs`, `Settings/SettingsService.cs` (§2.5)
- [x] Tests: the seven new files and the `EntryRepositoryTests` additions from §7. Add the linked `tray\*.ico` content items to the Tests csproj once the icons exist
- [x] `tools/make-icons.ps1` (§4.1); run it with `powershell -NoProfile -File tools/make-icons.ps1` (Windows PowerShell 5.1) and commit the 22 generated files in `src/NaggingTimeTracker.App/Assets/tray/`
- [x] App csproj: `AssemblyName`, `Version`, `RootNamespace`, `<Resource>` for the icons (§3.1)
- [x] `App/SingleInstance.cs` (§3.3)
- [x] `App/App.xaml(.cs)`: the startup sequence, unhandled-exception handler and exit (§3.2), replacing the Phase 1 placeholder
- [x] `App/Tracking/TrackingActions.cs`, `App/Windows/ConcurrencyDialog.xaml(.cs)`, `App/Windows/TooShortDialog.xaml(.cs)` (§5)
- [x] `App/Tray/TrayToolTipView.xaml(.cs)` + `TrayToolTipViewModel`, `App/Tray/NativeMethods.cs`, `App/Tray/TrayHost.cs` (§4.2–4.3)
- [x] `App/ViewModels/QuickPanelViewModel.cs`, `RunningRowViewModel.cs`, `RecentRowViewModel.cs`, `App/Windows/IModalHost.cs`, `App/Windows/QuickPanelWindow.xaml(.cs)` with every §6.2 AutomationId (§6)
- [x] `dotnet build` (zero warnings) and `dotnet test --filter "Category!=UI"` (all green)
- [x] Manual verification (§8)
- [x] Update [README.md](README.md): the status line (Phase 2 shipped); under "Build, test, run", say the app now runs in the tray and document the `--data-dir` / `--show quick-panel` / `--no-background` switches; replace the "placeholder" paragraph
- [x] Tick the parent's Phase 2 checklist in [plan-time-tracker-v1.md](plan-time-tracker-v1.md), fold in the §9 deviations (§1, §2 Repositories, §3, §4.1, §8), and flip this doc's Status to Shipped
- [x] Commit "Phase 2 — tracking service + tray"

---

## Non-goals

- **No Entries, Export or Settings windows,** and so no menu items, footer links or `--show` values for them. Those are Phases 3–4 (parent §12).
- **Reminder pause has no effect yet.** Only the setting is written; `ReminderEngine` reads it in Phase 5. The `attention-*` icons are generated but never selected until Phase 5.
- **`--no-background` does nothing yet.** It is parsed and stored, and the background services that honour it arrive in Phases 5–6.
- **No idle or downtime handling.** A timer left running over an app restart just keeps counting (parent §7.3, Phase 6).
- **No cleanup of a customer or task type created by a cancelled start.** `GetOrCreate` runs before the concurrency check, so cancelling leaves the new name in the list. Harmless, and it can be archived in Phase 3's Settings tabs.
- **No multi-monitor or taskbar-edge placement.** The panel always sits at the bottom-right of the primary work area (`SystemParameters.WorkArea`), even with the taskbar at the top or left.
- **No guaranteed foreground activation from a second launch.** Windows' foreground lock may only flash the panel when the signal comes from another process; a tray click always activates it.
- **No logging infrastructure.** Errors surface as message boxes (§3.2).
- **No UI tests** (Phase 8). AutomationIds are added now so Phase 8 only has to audit them.
- **No `Start with Windows`** (Phase 7).
