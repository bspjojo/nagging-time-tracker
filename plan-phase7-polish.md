# Phase 7 — Polish: Start with Windows, backups, app icon (implementation plan)

**Status:** Shipped — 1 commit. Build clean (Debug and Release), tests 345/345 green. The 12 Phase 7 UI checks of §6.2 pass, the Phase 3–6 UI scripts still pass (34/34, 27/27, 28/28, 20/20), and the by-hand list passed: 8 scripted registry/Explorer checks against the real profile, then the exe icon by eye.

**Sequencing note:** this doc expands **Phase 7** of [plan-time-tracker-v1.md](plan-time-tracker-v1.md) (§12, "Phase 7 — Polish") into implementable detail. It builds on Phases 1–6, all shipped (latest commit `d2726fa`). The parent stays the source of truth for backups (§2 "Backups"), the General tab and the Run-key self-heal (§8), and the startup order (§1). Phase 9's installer ([plan-time-tracker-v1.md](plan-time-tracker-v1.md) §11) removes the Run value on uninstall and relies on the value name chosen here. Deviations are listed in §6 of this doc and get folded back into the parent once this doc is approved.

## Overview

Phase 7 finishes the app's everyday behaviour before the UI test project (Phase 8) and the installer (Phase 9):

- **Start with Windows** and **Open data folder**, on a new *General* Settings tab. At startup, a Run value pointing at an old exe path is rewritten to the current one.
- **A daily database backup** at startup, keeping the newest 7.
- **An application icon** for the exe and its windows.

**What exists today (confirmed by reading the code):**

- **Paths:** `DataPaths` already resolves `BackupDir` (`<root>\backups`) and creates it in `EnsureCreated()` ([DataPaths.cs:14-25](src/NaggingTimeTracker.Data/DataPaths.cs#L14-L25)). Under `--data-dir` both follow the override. `DataPaths` is registered as a singleton ([App.xaml.cs:122](src/NaggingTimeTracker.App/App.xaml.cs#L122)).
- **Settings key:** `SettingKeys.LastBackupDate = "last_backup_date"` exists ([SettingKeys.cs:20](src/NaggingTimeTracker.Core/Settings/SettingKeys.cs#L20)) and nothing reads or writes it yet (confirmed by grep). `ISettingsStore` has `Get` / `Set` / `Remove` ([Repositories.cs:99-107](src/NaggingTimeTracker.Core/Abstractions/Repositories.cs#L99-L107)). `SettingsService` has no plain-string accessor; it only reads typed `AppSettings` and epoch-ms instants.
- **SQLite:** `Microsoft.Data.Sqlite` comes into the Data project with `Microsoft.EntityFrameworkCore.Sqlite` 10.0.15 ([NaggingTimeTracker.Data.csproj:8](src/NaggingTimeTracker.Data/NaggingTimeTracker.Data.csproj#L8)). The database runs in WAL mode ([DatabaseInitializer.cs](src/NaggingTimeTracker.Data/DatabaseInitializer.cs)).
- **Startup order today:** `DatabaseInitializer.Initialize()` → `StartupDowntimeCheck.Run()` (skipped under `--no-background`) → `host.StartAsync()` ([App.xaml.cs:59-68](src/NaggingTimeTracker.App/App.xaml.cs#L59-L68)). The parent §1 order puts `BackupService` between the first two.
- **Startup options:** `StartupOptions(string? DataDir, ShowTarget Show, bool NoBackground)` ([StartupOptions.cs:19](src/NaggingTimeTracker.Core/Startup/StartupOptions.cs#L19)).
- **Settings window:** tabs are selected by `SettingsTab` tag ([SettingsWindow.xaml.cs:31-32](src/NaggingTimeTracker.App/Windows/SettingsWindow.xaml.cs#L31-L32)). `ShowSettings()` defaults to `SettingsTab.WorkHours` ([WindowManager.cs:35](src/NaggingTimeTracker.App/Windows/WindowManager.cs#L35)), and the Phase 5 UI script asserts that `--show settings` opens on *Work hours* ([verify-phase5.ps1:119](tools/ui-checks/verify-phase5.ps1#L119)).
- **Opening Explorer:** `ExportViewModel` already uses `Process.Start("explorer.exe", …)` ([ExportViewModel.cs:164](src/NaggingTimeTracker.App/ViewModels/ExportViewModel.cs#L164)).
- **Icons:** `tools/make-icons.ps1` writes 16, 20, 24 and 32 px images into each tray `.ico` ([make-icons.ps1:20](tools/make-icons.ps1#L20)). The App csproj has no `<ApplicationIcon>` yet.
- **Registry:** nothing in the app touches the registry yet.
- **Tests** reference Core and Data only, so testable logic goes there.

**Decisions already made (givens), from the parent:**

- The Run value is `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\NaggingTimeTracker` = `"<Environment.ProcessPath>"` (quoted, no arguments).
- At every startup, a Run value that exists but points elsewhere is rewritten to the current `Environment.ProcessPath`.
- The backup runs at startup when `last_backup_date` isn't today's local date. It uses `SqliteConnection.BackupDatabase` outside EF, writes to `backups\tracker-yyyy-MM-dd.db`, keeps the newest 7 `tracker-*.db`, then sets `last_backup_date`.

**Stakeholder decisions for this phase:**

- **Debug builds never self-heal the Run value** (§4). Only Release builds, which is what the installer ships, rewrite it.
- **A dedicated `Assets/app.ico`** (the same green circle, 16–256 px) replaces the parent's reuse of the 32-px tray icon (§5).

**Commit granularity:** one commit, "Phase 7 — polish", per the parent's one-commit-per-phase rule.

---

## 1. Data: `Data/BackupService.cs`

```csharp
public sealed class BackupService(string dbPath, string backupDir, ISettingsStore settings, TimeProvider time)
{
    public const int Keep = 7;

    /// The backup file written, or null when today's backup already exists.
    public string? RunIfDue();
}
```

It takes the two paths rather than `DataPaths`, so tests can point it at a `TestDb` file and a temp folder.

**`RunIfDue()`:**

1. `today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), time.LocalTimeZone).DateTime)`, formatted `"yyyy-MM-dd"` (invariant).
2. If `settings.Get(SettingKeys.LastBackupDate) == today` → return `null`.
3. `Directory.CreateDirectory(backupDir)`. `target = Path.Combine(backupDir, $"tracker-{today}.db")`; `temp = target + ".tmp"`. Delete `temp` if it's left over from a failed run.
4. Copy with SQLite's online backup API, which takes a consistent snapshot including WAL content:
   ```csharp
   using (var source = new SqliteConnection(Cs(dbPath)))   // Cs: SqliteConnectionStringBuilder { DataSource = path, Pooling = false }
   using (var dest = new SqliteConnection(Cs(temp)))
   {
       source.Open(); dest.Open();
       source.BackupDatabase(dest);
   }
   File.Move(temp, target, overwrite: true);
   ```
   The connection strings are built with `SqliteConnectionStringBuilder`, not interpolation, so a path containing `;` can't break them. `Pooling=False` means closing a connection really releases the file, so the move and later deletes can't hit a lock. Writing to `.tmp` first means a crash mid-copy never leaves a truncated `tracker-*.db` that retention would count.
5. **Retention:** `Directory.GetFiles(backupDir, "tracker-*.db")`, kept only where the file name matches `^tracker-\d{4}-\d{2}-\d{2}\.db$` (a regex, not `?` wildcards, whose matching rules differ between Windows and .NET), sorted by file name descending (the ISO date sorts correctly), and delete everything after the first `Keep`. Files that don't match (e.g. a user's own `tracker-copy.db`) are never touched.
6. `settings.Set(SettingKeys.LastBackupDate, today)`; return `target`.

**Failure policy:** `RunIfDue` throws on failure. The App caller (§4) catches, logs to `Debug`, and continues startup. `last_backup_date` isn't written, so the next start retries. A failed backup must never stop the user tracking time.

---

## 2. Core: `Core/Startup/RunAtLogin.cs`

The Start-with-Windows rules, pure, over a tiny abstraction that the App implements with the registry:

```csharp
public interface IRunKeyStore
{
    string? Get();              // the raw value, or null if absent
    void Set(string value);
    void Remove();
}

public sealed class RunAtLogin(IRunKeyStore store)
{
    public bool IsEnabled { get; }                    // value exists (whatever it points at)
    public void Enable(string exePath);               // Set($"\"{exePath}\"")
    public void Disable();                            // Remove()
    public bool HealIfMoved(string exePath);          // true when it rewrote the value
    public static string? ParsePath(string value);    // strips one pair of surrounding quotes, trims; null if empty
}
```

- **`HealIfMoved`:** if the value is absent → `false`. Otherwise compare `ParsePath(value)` with `exePath` after `Path.GetFullPath` on both, case-insensitively (`StringComparison.OrdinalIgnoreCase`; Windows paths). Equal → `false`. Different, or unparsable → `Enable(exePath)`, `true`.
- **Quoting:** the value is always written quoted, so a path with spaces (`C:\Program Files\NaggingTimeTracker\…`) launches correctly.

### 2.1 `App/Startup/RegistryRunKeyStore.cs`

Implements `IRunKeyStore` on `Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)`, value name `NaggingTimeTracker` (a `public const string ValueName`, which Phase 9's installer uses too). `Set` writes `RegistryValueKind.String`; `Remove` uses `DeleteValue(name, throwOnMissingValue: false)`. `Microsoft.Win32.Registry` is part of the `net10.0-windows` framework, so no package is needed.

---

## 3. App: *General* tab

### 3.1 `App/ViewModels/GeneralSettingsViewModel.cs`

Constructor: `(RunAtLogin runAtLogin, DataPaths paths, StartupOptions options)`.

- **`StartWithWindows`** (bool): initialised from `runAtLogin.IsEnabled`. Its setter applies immediately, like the other tabs: `Enable(Environment.ProcessPath!)` or `Disable()`. If the registry write throws (e.g. a policy blocks it), `GeneralError` shows *"Couldn't change the startup setting: {message}"* and the checkbox reverts to `runAtLogin.IsEnabled`.
- **`CanChangeStartWithWindows`** = `options.DataDir is null`. The Run value has no arguments, so it always starts the app on the **default** data folder. Changing it from an instance using `--data-dir` would register something other than what's running. When false, the checkbox is disabled and a hint reads *"Only available when using the default data folder."*
- **`DataFolder`** (string) = `paths.Root`, shown as read-only text.
- **`OpenDataFolderCommand`**: `Process.Start("explorer.exe", $"\"{paths.Root}\"")`.

### 3.2 Markup and tab order

- `SettingsTab` gains `General` as its **first** member. A `TabItem` *General* becomes the first tab in [SettingsWindow.xaml](src/NaggingTimeTracker.App/Windows/SettingsWindow.xaml), with `DataContext="{Binding General}"`. The parent §8 lists General first.
- `ShowSettings()` with no argument opens **General** (the first tab, as Phase 5 defined "no argument"). [WindowManager.cs:35](src/NaggingTimeTracker.App/Windows/WindowManager.cs#L35) changes its default.
- **Content:**
  - `CheckBox` *"_Start Nagging Time Tracker when I sign in to Windows"* → `StartWithWindows`, `IsEnabled` → `CanChangeStartWithWindows`;
  - the data-dir hint (visible when `!CanChangeStartWithWindows`);
  - *"Data folder:"* and the `DataFolder` text (selectable `TextBox`, `IsReadOnly=True`);
  - a `Button` *"_Open data folder"* → `OpenDataFolderCommand`;
  - a hint: *"Your entries are in tracker.db. A backup is made each day you use the app; the last 7 are kept in the backups folder."*;
  - the error `TextBlock` → `GeneralError`.
- **AutomationIds:** `SettingsWindow.GeneralTab`, `.StartWithWindows`, `.StartWithWindowsHint`, `.DataFolder`, `.OpenDataFolderButton`, `.GeneralError`.
- `SettingsViewModel` gets a `General` property, built with the injected `RunAtLogin`, `DataPaths` and `StartupOptions`.

### 3.3 UI script update

Settings no longer opens on *Work hours*, and controls on an unselected tab aren't in the UI Automation tree. [verify-phase5.ps1](tools/ui-checks/verify-phase5.ps1) edits Work hours without selecting it in three places:

- after the `--show settings` launch at [line 117](tools/ui-checks/verify-phase5.ps1#L117): the check at line 119 now asserts *"--show settings opens on General"* (`SettingsWindow.GeneralTab` selected), then `SelectTab $set 'SettingsWindow.WorkHoursTab'`;
- after the second `--show settings` launch at [line 125](tools/ui-checks/verify-phase5.ps1#L125): `SelectTab $set 'SettingsWindow.WorkHoursTab'`;
- after `OpenSettings` at [line 190](tools/ui-checks/verify-phase5.ps1#L190), before `SetCheck … NotTrackingEnabled`: `SelectTab $set 'SettingsWindow.WorkHoursTab'`.

The Phase 3 and Phase 6 scripts already select their tabs explicitly.

---

## 4. App: startup wiring ([App.xaml.cs](src/NaggingTimeTracker.App/App.xaml.cs))

`OnStartup`, in the parent §1 order:

1. `DatabaseInitializer.Initialize()` (unchanged).
2. **Backup:** `try { _host.Services.GetRequiredService<BackupService>().RunIfDue(); } catch (Exception ex) { Debug.WriteLine($"Backup failed: {ex}"); }`. It runs under `--no-background` too: it's data safety, not a background behaviour, and with `--data-dir` it backs up that folder's database.
3. **Run-key self-heal:** only in **Release** builds (`#if !DEBUG`, stakeholder decision) and only when `options.DataDir is null` (same reason as §3.1). `try { runAtLogin.HealIfMoved(Environment.ProcessPath!); } catch (Exception ex) { Debug.WriteLine(…); }`. So neither a Debug build run from the repo against your real data folder, nor the UI scripts (which always pass `--data-dir`), can point your real Run value at a development build. Ticking *Start with Windows* in a Debug build still writes that build's path: that's an explicit choice, not a silent rewrite.
4. `StartupDowntimeCheck.Run()` (unchanged, gated on `--no-background`).
5. `host.StartAsync()` and the rest (unchanged).

**DI** (`BuildHost`):

- `services.AddSingleton(sp => new BackupService(paths.DbPath, paths.BackupDir, sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<TimeProvider>()))`;
- `services.AddSingleton<IRunKeyStore, RegistryRunKeyStore>()` and `services.AddSingleton<RunAtLogin>()`.

---

## 5. App icon

- **[tools/make-icons.ps1](tools/make-icons.ps1):**
  - `Write-Ico` gains a `[int[]]$iconSizes = $sizes` parameter and uses it in place of the script-level `$sizes` ([lines 63-86](tools/make-icons.ps1#L63-L86)).
  - The `ICONDIRENTRY` width/height bytes ([line 74](tools/make-icons.ps1#L74)) are written as `[byte]($s % 256)`, because the ICO format stores 256 as 0.
  - After the tray icons, it writes `src/NaggingTimeTracker.App/Assets/app.ico`: `Write-Ico <path> $colors.tracking '' -iconSizes 16, 20, 24, 32, 48, 64, 256`. It's the same green circle as `tracking-1.ico`, with larger images.
  - The final count message still counts the tray folder only. The synopsis and description mention `app.ico`.
- **Regenerate** with `powershell -NoProfile -File tools/make-icons.ps1`. The tray `.ico` files must come out byte-identical (git shows no change). If they differ, restore them with `git checkout -- src/NaggingTimeTracker.App/Assets/tray`; only `app.ico` is new output. Commit `app.ico`.
- **[NaggingTimeTracker.App.csproj](src/NaggingTimeTracker.App/NaggingTimeTracker.App.csproj):** `<ApplicationIcon>Assets\app.ico</ApplicationIcon>`. That embeds it as the exe's icon (Explorer, taskbar, Alt+Tab). WPF windows without their own `Icon` show the exe's icon, so no window changes. `app.ico` doesn't need a `<Resource>` item; the tray icons keep theirs.

---

## 6. Tests

### 6.1 Unit tests (`tests/NaggingTimeTracker.Tests/`)

- **`Data/BackupServiceTests.cs`** (`TestDb`, a temp backup folder per test, `FakeTimeProvider` with a pinned local zone):
  - first run writes `tracker-<today>.db`, returns its path, and sets `last_backup_date`;
  - the backup is a valid database with the same entries (open it with a plain `SqliteConnection`, `SELECT COUNT(*) FROM entries`);
  - a second run the same day returns `null` and writes nothing;
  - the next local day writes a second file;
  - "today" is the **local** date: at 23:30 local, which is the next day in UTC, the file is named for the local date;
  - **retention:** with 8 older `tracker-*.db` files present, one run leaves the newest 7 (today's plus the 6 newest older ones); a non-matching file (`notes.txt`, `tracker-copy.db`) is untouched;
  - a leftover `tracker-<today>.db.tmp` is replaced, and no `.tmp` remains afterwards.
- **`Core/RunAtLoginTests.cs`** (an in-memory fake `IRunKeyStore`):
  - `Enable` writes the quoted path; `Disable` removes it; `IsEnabled` follows;
  - `HealIfMoved`: absent → `false`, nothing written; same path (different case, quoted) → `false`; different path → rewritten quoted, `true`; an unquoted value with the same path → `false`;
  - `ParsePath`: quoted, unquoted, empty / whitespace → `null`.

### 6.2 UI checks (`tools/ui-checks/verify-phase7.ps1`, same harness as Phase 6)

1. **Backup:** launch with a scratch `--data-dir --no-background`, close; `backups\tracker-<today>.db` exists. Relaunch and close: still one backup file, same timestamp.
2. **Retention:** with the app closed, create 8 dummy `tracker-2026-01-0N.db` files; launch, close → the newest 7 `tracker-*.db` remain, and today's is among them. (The script deletes `last_backup_date` first, so a backup is due.)
3. **General tab:** `--show settings` opens on *General*. The data folder text equals the scratch folder. With `--data-dir`, *Start with Windows* is disabled and the hint is shown.
4. **Phase 5 script** re-run with its updated tab assertion (§3.3).
5. **By hand** (registry and Explorer side effects on the real profile):
   - Without `--data-dir`, tick *Start with Windows*: `reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v NaggingTimeTracker` shows the quoted exe path. Untick: the value is gone.
   - Self-heal, with a **Release** build (`dotnet build -c Release`): set the value to `"C:\old\NaggingTimeTracker.exe"` with `reg add`, start the Release exe without `--data-dir` → the value is the Release exe's path. Then start the Debug exe the same way → the value is unchanged. Delete it afterwards.
   - *Open data folder* opens Explorer on the folder.
   - The exe shows the green icon in Explorer, including *Large icons* view (sharp, not upscaled), and on the taskbar.

### Implementation notes

- **`IOException` needs `using System.IO;` in the App project.** WPF projects leave `System.IO` out of their implicit usings (it clashes with `System.Windows.Shapes.Path`).
- **`GeneralSettingsViewModel` catches registry failures** (`UnauthorizedAccessException`, `SecurityException`, `IOException`) both when reading the state and when applying it, so a locked-down profile shows the inline error instead of crashing Settings.
- **Self-heal and backup are `App.RunBackup` / `App.HealRunKey`** in [App.xaml.cs](src/NaggingTimeTracker.App/App.xaml.cs). The `#if DEBUG` branch only discards the parameters, so both configurations build without warnings.
- **`app.ico` checks:** its 256-px entry is a real 256×256 PNG stored with size byte 0 (header inspected). Windows PowerShell 5.1's `System.Drawing.Icon` won't load 256-px entries, so Explorer's *Extra large icons* view was the real check; it shows the sharp green circle. Re-running `make-icons.ps1` left all 22 tray icons byte-identical.
- **The by-hand list was scripted where safe.** A scratchpad script ticked and unticked *Start with Windows* through UI Automation and checked the value each time, opened the data folder, and checked self-heal three ways: Release repoints a moved value, Debug leaves it, Release doesn't create an absent value. It deleted the value afterwards (it was absent before). Only the icon was checked by eye.
- **`p6db.py` gained `unset`** for the Phase 7 script; `verify-phase7.ps1` uses `p6db.py` for both `get` and `unset`.

---

## 7. Docs

- [README.md](README.md): the status line (Phases 1–7); a feature bullet for Start with Windows, daily backups (newest 7 in `backups\`) and *Open data folder*; a Conventions bullet that registry logic lives in `Core/Startup/RunAtLogin` behind `IRunKeyStore`, and that `--data-dir` instances and Debug builds never rewrite the Run value on their own; the `tools/make-icons.ps1` row says it also writes `Assets/app.ico`.

---

## 8. Deviations from the parent (fold into [plan-time-tracker-v1.md](plan-time-tracker-v1.md))

1. **`RunAtLogin` + `IRunKeyStore`** (Core) and `RegistryRunKeyStore` (App) are new, so the Run-key rules are unit-testable. The parent puts nothing in Core for this.
2. **`--data-dir` instances never touch the Run value:** no self-heal, and the checkbox is disabled (§8 doesn't say).
3. **Backups write to `.tmp` and then move**, use `Pooling=False`, and only count `tracker-yyyy-MM-dd.db` names for retention. A failed backup is logged and retried next start (§2 doesn't say).
4. **The backup runs under `--no-background` too.**
5. **`BackupService` takes the DB path and backup folder**, not `DataPaths`.
6. **General is the first Settings tab and the default** for `ShowSettings()`.
7. **No self-heal in Debug builds** (stakeholder decision).
8. **The app icon is a new `Assets/app.ico`** (16–256 px, generated by `make-icons.ps1`), not `Assets\tray\tracking-1.ico` (stakeholder decision).

---

## Steps

- [x] Data: `BackupService.cs` (§1)
- [x] Core: `Startup/RunAtLogin.cs` with `IRunKeyStore` (§2)
- [x] Tests: `Data/BackupServiceTests.cs`, `Core/RunAtLoginTests.cs` (§6.1)
- [x] App: `Startup/RegistryRunKeyStore.cs` (§2.1)
- [x] App: `ViewModels/GeneralSettingsViewModel.cs`; `General` on [SettingsViewModel.cs](src/NaggingTimeTracker.App/ViewModels/SettingsViewModel.cs); `SettingsTab.General` and the `ShowSettings` default in [WindowManager.cs](src/NaggingTimeTracker.App/Windows/WindowManager.cs); the tab in [SettingsWindow.xaml](src/NaggingTimeTracker.App/Windows/SettingsWindow.xaml) (§3.1–3.2)
- [x] App: startup wiring and DI in [App.xaml.cs](src/NaggingTimeTracker.App/App.xaml.cs) (§4)
- [x] Tools: the `Write-Ico` sizes parameter, the 256 → 0 size byte and the `app.ico` output in [make-icons.ps1](tools/make-icons.ps1); regenerate, confirm the tray icons are unchanged, add `src/NaggingTimeTracker.App/Assets/app.ico` (§5)
- [x] App: `<ApplicationIcon>Assets\app.ico</ApplicationIcon>` in [NaggingTimeTracker.App.csproj](src/NaggingTimeTracker.App/NaggingTimeTracker.App.csproj) (§5)
- [x] `dotnet build` (zero warnings) and `dotnet test --filter "Category!=UI"` (all green)
- [x] UI: the three tab selections in [verify-phase5.ps1](tools/ui-checks/verify-phase5.ps1) (§3.3); write `tools/ui-checks/verify-phase7.ps1` and add it to [tools/ui-checks/README.md](tools/ui-checks/README.md)
- [x] Re-run the Phase 3–6 UI scripts (regression), then the §6.2 checks and the by-hand list; record the results in the commit message
- [x] Update [README.md](README.md) (§7)
- [x] Tick the parent's Phase 7 checklist in [plan-time-tracker-v1.md](plan-time-tracker-v1.md), fold in the §8 deviations (§1, §2, §8, and the Phase 7 checklist's icon line), and flip this doc's Status to Shipped
- [x] Commit "Phase 7 — polish"

---

## Non-goals

- **No restore-from-backup UI.** To restore, close the app and copy a backup over `tracker.db`; *Open data folder* gets you there. A restore command could come later.
- **No configurable backup count, schedule or location.** Seven daily backups in the data folder, as the parent specifies.
- **No backups while the app keeps running past midnight.** The backup runs at startup only, so a session spanning several days backs up once.
- **No Run value for `--data-dir` instances**, and no `--data-dir` argument in the Run value (§3.1).
- **No removal of the Run value on uninstall here.** That's Phase 9's installer (parent §11), using `RegistryRunKeyStore.ValueName`.
- **No per-machine (HKLM) startup entry**, even for an all-users install. Start with Windows is per user.
- **No new icon artwork.** `app.ico` is the tray's green circle at more sizes (§5).
- **No Run-key self-heal in Debug builds** (§4).
- **No UI test project** (Phase 8).
