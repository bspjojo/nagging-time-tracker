# Phase 6 — Idle detection (implementation plan)

**Status:** Shipped — 1 commit. Build clean, tests 324/324 green. The Phase 3 and 4 UI scripts still pass with no failures, Phase 5's passes 28/28, the 19 Phase 6 UI checks of §5.2 pass on real timings, and a Win+L lock longer than the threshold was checked by hand.

**Sequencing note:** this doc expands **Phase 6** of [plan-time-tracker-v1.md](plan-time-tracker-v1.md) (§12, "Phase 6 — Idle detection") into implementable detail. It builds on Phases 1–5, all shipped (latest commit `2f1aff2`). It plugs into two seams Phase 5 left for it: the `IIdleState` interface ([plan-phase5-reminders.md](plan-phase5-reminders.md) §3.3) and the heartbeat that `ReminderHost` already writes (same doc, §3.1). The parent stays the source of truth for idle behaviour (§7.1–7.3) and the startup order (§1). Deviations are listed in §7 of this doc and get folded back into the parent once this doc is approved.

## Overview

Phase 6 notices when you walk away from a running timer and asks what to do with that time when you come back. It also asks the same question at startup when the app wasn't running (crash, logoff, or exit with timers running). Settings gains an *Idle* tab.

Like Phase 5, this phase touches OS events and a window that interrupts, so the rules are pure and unit-tested in Core: the idle state machine, the downtime decision and the resolver that rewrites entries. The App side is a thin shell around `GetLastInputInfo`, `SystemEvents` and one dialog.

**What exists today (confirmed by reading the code):**

- **The idle seam:** `IIdleState { bool IsUserIdle { get; } }` and `NeverIdle` ([IIdleState.cs](src/NaggingTimeTracker.App/Reminders/IIdleState.cs)). `NeverIdle` is registered unconditionally ([App.xaml.cs:138](src/NaggingTimeTracker.App/App.xaml.cs#L138)). `ReminderHost` reads `idle.IsUserIdle` every tick and passes `nags.HasPending` as `IsPromptOpen` ([ReminderHost.cs:85-101](src/NaggingTimeTracker.App/Reminders/ReminderHost.cs#L85-L101)). The engine already returns nothing when either is true ([ReminderEngine.cs:26](src/NaggingTimeTracker.Core/Reminders/ReminderEngine.cs#L26)), so nag suppression needs no Core change.
- **The heartbeat:** `ReminderHost.Tick` writes `heartbeat_utc` on its first tick and then at most once per 60 s ([ReminderHost.cs:117-121](src/NaggingTimeTracker.App/Reminders/ReminderHost.cs#L117-L121)). Nothing reads it yet. `ReminderHost` isn't registered under `--no-background` ([App.xaml.cs:140-143](src/NaggingTimeTracker.App/App.xaml.cs#L140-L143)).
- **Settings:** `AppSettings.IdleEnabled` (default `true`) and `IdleThresholdMin` (default `10`) exist, are loaded and saved, and `Save` rejects a threshold below 1 ([AppSettings.cs:18-19](src/NaggingTimeTracker.Core/Settings/AppSettings.cs#L18-L19), [SettingsService.cs:35-42](src/NaggingTimeTracker.Core/Settings/SettingsService.cs#L35-L42)). `SettingKeys.HeartbeatUtc` exists; `GetInstant`/`SetInstant` read and write it.
- **Tracking:** `TrackingService.StopAt(id, stopUtc)` returns `null` for a missing or already-stopped entry and throws for `stopUtc ≤ start` ([TrackingService.cs:76-89](src/NaggingTimeTracker.Core/Services/TrackingService.cs#L76-L89)). `Continue` on a **stopped** source needs `confirmedConcurrent: true` when other entries run, and stores the given `overlapReason` only when others run ([TrackingService.cs:91-132](src/NaggingTimeTracker.Core/Services/TrackingService.cs#L91-L132)). Each call raises `RunningEntriesChanged`.
- **Settings window:** tabs are selected by `SettingsTab` tag ([WindowManager.cs:8-15](src/NaggingTimeTracker.App/Windows/WindowManager.cs#L8-L15)), so an *Idle* tab can be inserted anywhere. `ReminderSettingsViewModel` drives the *Work hours* and *Check-ins* tabs with apply-immediately semantics: each `TryApply` builds the next `AppSettings` from `_saved` plus every currently valid field and saves the whole record ([ReminderSettingsViewModel.cs:77-133](src/NaggingTimeTracker.App/ViewModels/ReminderSettingsViewModel.cs#L77-L133)).
- **Startup:** `OnStartup` runs `DatabaseInitializer.Initialize()` and then `host.StartAsync()` with nothing in between ([App.xaml.cs:58-60](src/NaggingTimeTracker.App/App.xaml.cs#L58-L60)). `BackupService` (Phase 7) doesn't exist yet.
- **Interop:** the App project has no `AllowUnsafeBlocks` and no `LibraryImport` yet; the existing P/Invokes use `DllImport` ([Reminders/NativeMethods.cs](src/NaggingTimeTracker.App/Reminders/NativeMethods.cs)). The test project references Core and Data only, not App ([NaggingTimeTracker.Tests.csproj:25-26](tests/NaggingTimeTracker.Tests/NaggingTimeTracker.Tests.csproj#L25-L26)), so any logic worth testing has to live in Core.
- **Formatting:** `DurationFormat.Short` gives `"43m"` / `"1h 23m"` ([DurationFormat.cs:8](src/NaggingTimeTracker.Core/Formatting/DurationFormat.cs#L8)).

**Decisions already made (givens):**

- **From the parent:** everything in §7. Poll `GetLastInputInfo` every 15 s. Lock and suspend count as idle. While idle, no nags. On return, if a running entry started before the idle period, show a topmost, activated, modal prompt with **Keep it / Discard idle time & continue / Discard idle time & stop**. Entries started during the idle period are never touched. At startup, a heartbeat older than the threshold with entries running shows the same prompt worded *"The app wasn't running from … to …"*, resolved by the same `IdleResolver`. Skipped under `--no-background`.
- **Apply-immediately settings** (Phase 5 stakeholder decision) apply to the new *Idle* tab too.

**Commit granularity:** one commit, "Phase 6 — idle detection", per the parent's one-commit-per-phase rule.

---

## 1. Core: idle state machine

### 1.1 `Core/Idle/IdleTracker.cs`

A pure class with no timers, no P/Invoke and no clock of its own: every input carries `now`, and `IdleMonitor` (§3.1) feeds it. This is what makes lock/suspend/return ordering testable.

```csharp
public readonly record struct IdleSpan(DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    public TimeSpan Duration => EndUtc - StartUtc;
}

public readonly record struct IdleOptions(bool Enabled, TimeSpan Threshold)
{
    public static IdleOptions From(AppSettings s) => new(s.IdleEnabled, TimeSpan.FromMinutes(s.IdleThresholdMin));
}

public sealed class IdleTracker
{
    public DateTimeOffset? IdleStartUtc { get; }
    public bool IsUserIdle => IdleStartUtc is not null;

    public IdleSpan? Poll(DateTimeOffset nowUtc, TimeSpan sinceLastInput, IdleOptions o);
    public void Lock(DateTimeOffset nowUtc, TimeSpan sinceLastInput, IdleOptions o);
    public IdleSpan? Unlock(DateTimeOffset nowUtc, IdleOptions o);
    public void Suspend(DateTimeOffset nowUtc, TimeSpan sinceLastInput, IdleOptions o);
    public void Resume();
    public void Reset();
}
```

Private state: `IdleStartUtc`, `_locked`, `_suspended`.

**Rules:**

- **Disabled** (`o.Enabled == false`): `Poll` calls `Reset()` and returns `null`; `Lock` and `Suspend` do nothing; `Unlock` clears `_locked` and returns `null`. So turning idle off in Settings takes effect on the next poll, even mid-idle.
- **`Poll`:**
  1. If `_locked` or `_suspended` → return `null`. Input on the lock screen must not count as a return.
  2. `lastInput = nowUtc - sinceLastInput`.
  3. If `IdleStartUtc` is null and `sinceLastInput ≥ o.Threshold` → `IdleStartUtc = lastInput`; return `null`.
  4. If `IdleStartUtc` is set and `lastInput > IdleStartUtc + 1 s` → there was input after the idle period began: return `End(lastInput, o)`. The 1-s margin absorbs the rounding between `Environment.TickCount64` and `TimeProvider`.
  5. Otherwise `null`.
- **`Lock` / `Suspend`:** set `_locked` / `_suspended`; then `IdleStartUtc ??= nowUtc - sinceLastInput`. An already-running idle period keeps its earlier start. The start is the last input before locking, not "now": an auto-lock after 15 min of inactivity starts the idle period 15 min ago.
- **`Unlock`:** clear `_locked`. If `_suspended` is still set → `null`. Otherwise, if `IdleStartUtc` is set → `End(nowUtc, o)`.
- **`Resume`:** clear `_suspended` only. It does **not** end the idle period: waking can happen without the user (a lid opened, a wake timer), and the session lock that usually follows resume can arrive after it. The return is then picked up by the next `Poll` that sees fresh input, or by `Unlock`, within 15 s.
- **`End(endUtc, o)`** (private): `span = new IdleSpan(IdleStartUtc, endUtc)`; clear `IdleStartUtc`; return `span` if `span.Duration ≥ o.Threshold`, else `null`. A 2-minute Win+L with a 10-minute threshold suppresses nags while locked but doesn't prompt.
- **`Reset()`:** clears all three fields.

### 1.2 `Core/Idle/Downtime.cs`

The startup decision (§3.3), pure:

```csharp
public static class Downtime
{
    /// The period the app wasn't running, or null when there's nothing to ask about.
    public static IdleSpan? Find(DateTimeOffset? heartbeatUtc, DateTimeOffset nowUtc, AppSettings s, IReadOnlyList<Entry> running);
}
```

Returns `new IdleSpan(heartbeatUtc, nowUtc)` when **all** hold, otherwise `null`:

- `heartbeatUtc` is set;
- `s.IdleEnabled`;
- `nowUtc - heartbeatUtc ≥ TimeSpan.FromMinutes(s.IdleThresholdMin)`;
- some running entry has `StartUtc < heartbeatUtc` (the same "affected" rule as `IdleResolver`, §2; an entry started after the last heartbeat has nothing to discard).

The heartbeat is written at most every 60 s, so the downtime may start up to a minute early. Discarding it can drop up to a minute of real work, which is accepted.

---

## 2. Core: `Core/Services/IdleResolver.cs`

```csharp
public enum IdleChoice { Keep, DiscardAndContinue, DiscardAndStop }

public sealed class IdleResolver(TrackingService tracking)
{
    /// Running entries that started before the idle period, oldest start first.
    public IReadOnlyList<Entry> Affected(DateTimeOffset idleStartUtc);

    public void Apply(IdleChoice choice, DateTimeOffset idleStartUtc);
}
```

- **`Affected`:** `tracking.GetRunning()` filtered to `StartUtc < idleStartUtc`, ordered by `StartUtc` then `Id`.
- **`Apply`** re-reads `Affected(idleStartUtc)` itself (parent §7.2), so an entry stopped elsewhere while the prompt was open is skipped:
  - **`Keep`:** nothing.
  - **`DiscardAndStop`:** for each affected entry, `tracking.StopAt(e.Id, idleStartUtc)`.
  - **`DiscardAndContinue`:** for each affected entry, `stopped = tracking.StopAt(e.Id, idleStartUtc)`; if `stopped` is not null, `tracking.Continue(e.Id, confirmedConcurrent: true, overlapReason: e.OverlapReason)`. Because the source is now stopped, `Continue` takes its stopped-source path. That path stores the reason only when other entries run, so a lone entry's continuation gets `null` (correct: it isn't running in parallel), and each of two parallel entries keeps its own reason whichever is processed first. The new entries start at the moment of the click, so the time spent looking at the prompt is discarded as well.
  - `Continue` is never called without `confirmedConcurrent: true`, so `NeedsConfirmation` can't come back. The result is ignored.
- `StopAt`'s guard (`stopUtc > start`) always holds, because affected entries start before `idleStartUtc`.
- Each `StopAt`/`Continue` raises its own `RunningEntriesChanged`. With N affected entries that's up to 2N events, briefly showing an intermediate running count in the tray. That's accepted rather than adding a batch API to `TrackingService` for this one caller.

---

## 3. App: monitor, prompt, startup check

### 3.1 `App/Idle/IdleMonitor.cs`

A singleton implementing `IIdleState`, `IHostedService` and `IDisposable`. Constructor: `(IdleResolver resolver, SettingsService settings, CatalogService catalog, TimeProvider time)`.

- **`StartAsync`** captures `Application.Current.Dispatcher` and, inside `dispatcher.Invoke(...)` (inline when already on the UI thread, as it is from `OnStartup`), creates and starts a `DispatcherTimer` (15 s, `DispatcherPriority.Background`) whose tick calls `Poll()`. That way the timer belongs to the UI dispatcher whichever thread the host calls `StartAsync` on. It then subscribes to `SystemEvents.SessionSwitch` and `SystemEvents.PowerModeChanged`. `SystemEvents` may raise these on its own thread, so each handler only does `dispatcher.BeginInvoke(...)`; all tracker access happens on the UI thread, like the reminder tick. `StopAsync` and `Dispose` stop the timer and unsubscribe (both idempotent).
- **`SinceLastInput()`:** `GetLastInputInfo` (§3.4). Elapsed = `unchecked((uint)Environment.TickCount64 - info.dwTime)` ms, which survives the 49.7-day wrap. If the call fails, the poll is skipped.
- **`Poll()`:** skipped while `IsPromptOpen`. Otherwise `options = IdleOptions.From(settings.Load())`, `span = tracker.Poll(time.NowUtc(), SinceLastInput(), options)`, then `OnReturned(span)`.
- **Session and power events** (skipped while `IsPromptOpen`):
  - `SessionSwitchReason.SessionLock` → `tracker.Lock(now, SinceLastInput(), options)`.
  - `SessionSwitchReason.SessionUnlock` → `OnReturned(tracker.Unlock(now, options))`.
  - `PowerModes.Suspend` → `tracker.Suspend(now, SinceLastInput(), options)`.
  - `PowerModes.Resume` → `tracker.Resume()`.
  - Other reasons and modes are ignored.
- **`OnReturned(IdleSpan? span)`:** if `span` is null → return. `affected = resolver.Affected(span.StartUtc)`; if empty → return (nothing ran across the idle period, parent §7.1). Otherwise set `IsPromptOpen = true`, show `IdlePromptDialog` (§3.2) in `IdlePromptKind.Away` with the span and affected entries, `resolver.Apply(dialog.Choice, span.StartUtc)`, then `IsPromptOpen = false` in a `finally`.
- **`IIdleState`:** `IsUserIdle => tracker.IsUserIdle`; `IsPromptOpen` (new member, §3.5) is the flag above.
- While the prompt is open, polls and session events are ignored. A second absence with the prompt still up doesn't start a new idle period: the prompt's options already cover it (*Discard … & continue* starts the new entries at the click; *Discard … & stop* stops at the original idle start; *Keep* keeps all of it).

### 3.2 `App/Windows/IdlePromptDialog.xaml(.cs)`

- **Window:** `Topmost=True`, `ShowActivated=True`, `ShowInTaskbar=True` (a modal dialog with no owner needs a taskbar button so it can be found), `ResizeMode=NoResize`, `SizeToContent=WidthAndHeight`, `WindowStartupLocation=CenterScreen`, title *Nagging Time Tracker*. Shown with `ShowDialog()` and no owner.
- **Constructor:** `(IdlePromptKind kind, IdleSpan span, IReadOnlyList<string> affectedLabels, TimeZoneInfo tz)`, where `IdlePromptKind` is `Away` or `AppNotRunning` and each label is `customer · task type` (the parent §4.1 order) resolved by the caller through `IdlePromptLabels` (§3.3).
- **Message** (one `TextBlock`, `MessageText`):
  - `Away`: *"You were away from {start} to {end} ({duration}) while tracking {first}."*
  - `AppNotRunning`: *"The app wasn't running from {start} to {end} ({duration}) while tracking {first}."*
  - `{first}` is the first label; with more, append *" (and 1 other)"* / *" (and N others)"*.
  - `{start}`/`{end}` are local times in the current culture's short time (`"t"`). When their local dates differ, both use the short date and time (`"g"`) instead, since app-downtime can span days.
  - `{duration}` is `DurationFormat.Short(span.Duration)`.
- **Buttons:** **Keep it**, **Discard idle time & continue**, **Discard idle time & stop** (written `&amp;` in the XAML `Content`). `Choice` (an `IdleChoice`) is set by the click, then `DialogResult = true`.
- **No accidental choice:** the dialog is activated while the user is typing their way back in, so **no button is `IsDefault` and none has initial focus**; stray keystrokes can't pick an option. **Keep it** is `IsCancel`, and closing via ✕ / Alt+F4 / Esc leaves `Choice = Keep`, which changes nothing.
- **AutomationIds:** `IdlePrompt`, `IdlePrompt.Message`, `IdlePrompt.KeepButton`, `IdlePrompt.DiscardContinueButton`, `IdlePrompt.DiscardStopButton`.

### 3.3 `App/Idle/StartupDowntimeCheck.cs`

A singleton with one method, `Run()`. Constructor: `(IdleResolver resolver, SettingsService settings, TrackingService tracking, CatalogService catalog, TimeProvider time)`.

- `span = Downtime.Find(settings.GetInstant(SettingKeys.HeartbeatUtc), time.NowUtc(), settings.Load(), tracking.GetRunning())`. If null → return.
- Show `IdlePromptDialog` in `IdlePromptKind.AppNotRunning` with `resolver.Affected(span.StartUtc)`'s labels, then `resolver.Apply(dialog.Choice, span.StartUtc)`.
- **Wiring** in [App.xaml.cs](src/NaggingTimeTracker.App/App.xaml.cs) `OnStartup`, between `DatabaseInitializer.Initialize()` and `_host.StartAsync()` (parent §1 order; Phase 7 inserts `BackupService` before it): `if (!options.NoBackground) _host.Services.GetRequiredService<StartupDowntimeCheck>().Run();`. That's before `ReminderHost`'s first heartbeat write, so the stale heartbeat is still there to read. The tray doesn't exist yet; the dialog is self-contained, and `ShutdownMode=OnExplicitShutdown` means closing it doesn't end the app.
- The label lookup (`Entry` → `customer · type`) is shared with `IdleMonitor`: a small internal static helper `IdlePromptLabels.For(IEnumerable<Entry>, CatalogService)` in `App/Idle/`. It calls `catalog.GetNames()` once and formats `$"{names.Customer(e.CustomerId)} · {names.TaskType(e.TaskTypeId)}"`, as `NagWindowManager` does for its Continue label ([NagWindowManager.cs:167-168](src/NaggingTimeTracker.App/Reminders/NagWindowManager.cs#L167-L168)).
- Being the first window created, the startup dialog becomes `Application.MainWindow`. That's harmless: no code in the app reads `MainWindow` (confirmed by grep), and `ShutdownMode=OnExplicitShutdown` ignores it.

### 3.4 `App/Idle/NativeMethods.cs`

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

internal static partial class NativeMethods
{
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetLastInputInfo(ref LASTINPUTINFO plii);
}
```

`cbSize` is set to `(uint)Marshal.SizeOf<LASTINPUTINFO>()` before each call. The App csproj gains `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`, which the `LibraryImport` source generator requires (parent §7.1). `Microsoft.Win32.SystemEvents` comes with the WindowsDesktop shared framework that `UseWPF` already pulls in, so no package is added.

### 3.5 `IIdleState` and `ReminderHost`

- `IIdleState` gains `bool IsPromptOpen { get; }`; `NeverIdle` returns `false`.
- `ReminderHost.Tick` passes `nags.HasPending || idle.IsPromptOpen` as `IsPromptOpen` (parent §6.0 item 2 already says the idle prompt counts). One line, [ReminderHost.cs:101](src/NaggingTimeTracker.App/Reminders/ReminderHost.cs#L101).

### 3.6 DI ([App.xaml.cs](src/NaggingTimeTracker.App/App.xaml.cs) `BuildHost`)

- `services.AddSingleton<IdleResolver>()` and `services.AddSingleton<StartupDowntimeCheck>()` always (cheap; the check itself is gated in `OnStartup`).
- Replace the unconditional `AddSingleton<IIdleState, NeverIdle>()`:
  - **background on:** `AddSingleton<IdleMonitor>()`, `AddSingleton<IIdleState>(sp => sp.GetRequiredService<IdleMonitor>())`, `AddHostedService(sp => sp.GetRequiredService<IdleMonitor>())`, inside the existing `if (!options.NoBackground)` block, registered **before** `ReminderHost`.
  - **`--no-background`:** `AddSingleton<IIdleState, NeverIdle>()`. Nothing reads it in that mode, but it keeps the container complete.

`IdleMonitor.StartAsync` creates its timer through `dispatcher.Invoke` (§3.1), so it doesn't depend on which thread the host starts services on.

---

## 4. Settings: *Idle* tab

- `SettingsTab` gains `Idle`, placed after `CheckIns` (parent §8 order: Work hours, Check-ins, Idle, Reminders, Customers, Task types).
- **Extend `ReminderSettingsViewModel`** rather than adding a view-model: its `TryApply` saves the whole `AppSettings` from its own `_saved` copy, so a second view-model editing the same record would overwrite the first one's changes. Add `IdleEnabled` (bool), `IdleThresholdText` (string) and `IdleError`, initialised from `_saved` in the constructor, each with an `On…Changed => TryApply()` partial. In `TryApply`, after the check-ins block: `IdleError = null; next = next with { IdleEnabled = IdleEnabled }`; if `TryInterval(IdleThresholdText, out var t)` → `next = next with { IdleThresholdMin = t }`, else `IdleError = "Threshold must be 1–1440 minutes."`. Update the class summary to name the Idle tab.
- **Markup** in [SettingsWindow.xaml](src/NaggingTimeTracker.App/Windows/SettingsWindow.xaml), a new `TabItem` after *Check-ins* with `DataContext="{Binding Reminder}"`, laid out like *Check-ins*:
  - `CheckBox` *"_Ask what to do with the time when I come back from being away"* → `IdleEnabled`;
  - *"Away after"* `[TextBox]` *"minutes without keyboard or mouse input"* → `IdleThresholdText`;
  - a hint `TextBlock`: *"Locking the PC or putting it to sleep also counts as away."*;
  - the error `TextBlock` → `IdleError`.
- **AutomationIds:** `SettingsWindow.IdleTab`, `.IdleEnabled`, `.IdleThreshold`, `.IdleError`.
- `IdleMonitor` reads `settings.Load()` on every poll, so a change applies within 15 s.

---

## 5. Tests

### 5.1 Unit tests (`tests/NaggingTimeTracker.Tests/`)

- **`Core/IdleTrackerTests.cs`** (threshold 10 min, enabled, unless stated):
  - below the threshold → not idle, `Poll` returns `null`;
  - at the threshold → idle, `IdleStartUtc = now - sinceLastInput`;
  - input after the idle start → returns the span `(idleStart, lastInput)` and clears idle; input within the 1-s margin doesn't;
  - lock with recent input, unlock 2 min later → idle while locked, `Unlock` returns `null` (shorter than the threshold), not idle afterwards;
  - lock, unlock 30 min later → span from the last input before the lock to the unlock;
  - already idle for 20 min, then lock → the start is kept;
  - `Poll` while locked with a small `sinceLastInput` → stays idle, returns `null`;
  - suspend → resume → still idle; the next `Poll` with fresh input returns the span;
  - suspend, lock, resume, unlock → `Unlock` returns the span; suspend, lock, unlock (resume not yet seen) → `null`, still idle;
  - disabled → `Poll` never goes idle, `Lock` is ignored; disabling while idle → the next `Poll` resets.
- **`Core/DowntimeTests.cs`:** null heartbeat → `null`; idle disabled → `null`; gap below the threshold → `null`; no running entries, or only one started after the heartbeat → `null`; otherwise the span `(heartbeat, now)`.
- **`Core/IdleResolverTests.cs`** (`TestDb` + `FakeTimeProvider`, like `TrackingServiceTests`), covering the parent's list plus the cases this doc adds:
  - `Keep` changes nothing;
  - `DiscardAndStop` stops at `idleStart`;
  - `DiscardAndContinue`: the source stops at `idleStart`, the new entry starts now with `continued_from_id` set and the same customer, type and link; a lone entry's continuation has a `null` `overlap_reason`;
  - two parallel entries (B with reason `"x"`) → both continued, two running afterwards, B's continuation keeps `"x"`, no `NeedsConfirmation`;
  - an entry started at or after `idleStart` is untouched by every choice, and isn't in `Affected`;
  - an affected entry stopped between `Affected` and `Apply` is skipped (no exception, no continuation).

### 5.2 UI checks (scripted with UI Automation against a scratch `--data-dir`, as in Phases 2–5)

As in Phase 5, the script reads and writes the scratch database with `python -c` and the `sqlite3` module, writing only while the app is closed.

1. **Idle tab:** open Settings through the quick panel's footer link, select *Idle*. A threshold of `0` shows the inline error and isn't saved (re-open to confirm). Set it to `1`; re-open → `1`.
2. **Startup downtime:** with the app closed, insert a running entry started now − 60 min and set `heartbeat_utc` to now − 30 min. Launch → `IdlePrompt` appears, its message starts *"The app wasn't running from"*. **Discard idle time & stop** → the DB row's `stop_utc` equals the heartbeat.
3. Repeat step 2 with **Discard idle time & continue** → the old row stops at the heartbeat, a new running row has `continued_from_id` = the old id. Repeat with **Keep it** → still one running row, unchanged.
4. **Short downtime:** heartbeat now − 30 s → no prompt within 10 s of launch.
5. **`--no-background`:** the step 2 setup, launched with the switch → no prompt within 10 s.
6. **Live idle:** threshold 1 min, start a timer from the quick panel, then generate no input for 90 s. UI Automation's Invoke pattern doesn't synthesize input, but nobody may touch the keyboard or mouse during the wait. Then send a mouse move with user32 `SendInput` from the script → within 20 s `IdlePrompt` appears with *"You were away from"*. **Keep it** closes it and the timer is still running.
7. **Not checked by script:** lock/unlock and sleep/resume (they need credentials or real power transitions). A person checks one Win+L lasting over the threshold by hand and records it in the commit message.

### Implementation notes

- **The prompt's buttons stay focusable.** No button is `IsDefault` and none has initial focus (WPF doesn't focus one on its own), so stray keys can't choose; Tab still reaches them, so the dialog remains keyboard-usable.
- **`IdlePromptLabels.Ask`** holds the shared "affected → dialog → `Apply`" flow, used by both `IdleMonitor` and `StartupDowntimeCheck`. It does nothing when no entry is affected.
- **`IdleMonitor` guards every input** (poll, lock, unlock, suspend, resume) with one wrapper: it skips while the prompt is open, and logs and swallows exceptions to `Debug`, like the reminder tick. If `GetLastInputInfo` fails, a poll is skipped, and a lock or suspend uses zero idle time (the idle period starts "now").
- **The UI check scripts are now in the repo,** in [tools/ui-checks/](tools/ui-checks/README.md) (Phases 2–6, with their `sqlite3` helpers). Before this they lived only in temp folders. They load their helper from their own folder, and `-Out` (screenshots) defaults to `%TEMP%
tt-ui-checks`.
- **Two script bugs were fixed during the run:** the `SendInput` `INPUT` struct had extra padding (48 bytes rather than 40 on x64, so the call was rejected), and a one-row `Rows` result was unwrapped by PowerShell into a scalar whose `.Count` is empty in 5.1 (`return ,@(...)`). The app needed no change.
- **The live-idle check takes about 2 minutes** of no input. The script logs the idle time before and after its return nudge, so a failure shows whether the nudge registered.

---

## 6. Docs

- [README.md](README.md): the status line (Phases 1–6, idle detection); the `--no-background` row (drop "from Phase 6", it's now effective); a Conventions bullet that idle rules live in `Core/Idle/IdleTracker` and `IdleMonitor` is only the OS shell.

---

## 7. Deviations from the parent (fold into [plan-time-tracker-v1.md](plan-time-tracker-v1.md))

1. **New pure types in Core:** `IdleTracker`, `IdleSpan`, `IdleOptions` and `Downtime` (`Core/Idle/`). The parent puts all detection logic in `App/Idle/IdleMonitor.cs`, which the test project can't reference.
2. **Return = input after the idle start**, not "polled idle time below 15 s" (§7.1). It can't be missed by a late poll, and the span ends at the actual last input rather than at the poll.
3. **Lock and suspend start the idle period at the last input**, not "now", so an auto-lock after inactivity counts from when the user actually left.
4. **Resume doesn't end the idle period** on its own; the next real input or `SessionUnlock` does (§7.1 lists `Resume` as a return).
5. **No prompt for an absence shorter than the threshold**, even via lock or sleep. Nags are still suppressed while locked or asleep.
6. **The downtime prompt needs an entry started before the heartbeat**, not just any running entry (§7.3), matching the resolver's "affected" rule.
7. **The prompt names entries `customer · type`** (the parent's §7.2 example has them the other way round; §4.1 says customer first everywhere).
8. **Prompt safety:** no default button or initial focus; ✕ / Esc = **Keep it**.
9. **`IIdleState.IsPromptOpen`** feeds the engine's prompt-open suppression; `IdleMonitor` isn't run under `--no-background`.
10. **The Idle tab is part of `ReminderSettingsViewModel`**, and the threshold is limited to 1–1440 minutes like the other intervals.

---

## Steps

- [x] Core: `Idle/IdleTracker.cs` with `IdleSpan` and `IdleOptions` (§1.1)
- [x] Core: `Idle/Downtime.cs` (§1.2)
- [x] Core: `Services/IdleResolver.cs` with `IdleChoice` (§2)
- [x] Tests: `Core/IdleTrackerTests.cs`, `Core/DowntimeTests.cs`, `Core/IdleResolverTests.cs` (§5.1)
- [x] App: `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in [NaggingTimeTracker.App.csproj](src/NaggingTimeTracker.App/NaggingTimeTracker.App.csproj); `Idle/NativeMethods.cs` (§3.4)
- [x] App: `IIdleState.IsPromptOpen` + `NeverIdle` in [IIdleState.cs](src/NaggingTimeTracker.App/Reminders/IIdleState.cs); the `IsPromptOpen` argument in [ReminderHost.cs:101](src/NaggingTimeTracker.App/Reminders/ReminderHost.cs#L101) (§3.5)
- [x] App: `Windows/IdlePromptDialog.xaml(.cs)` with `IdlePromptKind`, and `Idle/IdlePromptLabels.cs` (§3.2, §3.3)
- [x] App: `Idle/IdleMonitor.cs` (§3.1)
- [x] App: `Idle/StartupDowntimeCheck.cs` and its call in `OnStartup` (§3.3)
- [x] App: DI changes in [App.xaml.cs:134-143](src/NaggingTimeTracker.App/App.xaml.cs#L134-L143) (§3.6)
- [x] App: `SettingsTab.Idle` in [WindowManager.cs](src/NaggingTimeTracker.App/Windows/WindowManager.cs), the Idle fields in [ReminderSettingsViewModel.cs](src/NaggingTimeTracker.App/ViewModels/ReminderSettingsViewModel.cs), the tab in [SettingsWindow.xaml](src/NaggingTimeTracker.App/Windows/SettingsWindow.xaml) (§4)
- [x] `dotnet build` (zero warnings) and `dotnet test --filter "Category!=UI"` (all green)
- [x] Commit the scripted UI checks for Phases 2–6 to [tools/ui-checks/](tools/ui-checks/README.md) (they were only in temp folders before), with a README; link it from [README.md](README.md)
- [x] Re-run the Phase 3–5 UI scripts from `tools/ui-checks/` (regression), then the §5.2 checks and the manual lock check; record the results in the commit message
- [x] Update [README.md](README.md) (§6)
- [x] Tick the parent's Phase 6 checklist in [plan-time-tracker-v1.md](plan-time-tracker-v1.md), fold in the §7 deviations (§1, §6.0, §7, §8), and flip this doc's Status to Shipped
- [x] Commit "Phase 6 — idle detection"

---

## Non-goals

- **No idle detection when no timer runs.** Nags are still suppressed while idle (that's the Phase 5 engine), but the prompt only appears when something was tracking across the absence.
- **No "assign the idle time to something else"** option (e.g. "that was a meeting"). The three parent options only; the user can add an entry in the editor afterwards.
- **No partial discard** (picking a different start or end than the detected span), and no per-entry choice when several are affected: one choice applies to all of them.
- **No reminder reset after the prompt** (stakeholder decision). Reminder bookkeeping doesn't advance while idle, so whatever fell due fires on the next 30-s tick after the prompt closes. That can be a check-in for a timer you just kept, or a not-tracking nag right after *Discard idle time & stop* during work hours. The prompt doesn't call `ReminderEngine.CheckedIn` or `SnoozeNotTracking`.
- **No batch API on `TrackingService`** to raise a single `RunningEntriesChanged` for a resolution (§2).
- **No prompt for downtime with idle detection turned off**, and no downtime check under `--no-background` (parent §7.3).
- **No suppression of the prompt during presentations.** You've just come back to the PC; the prompt is shown regardless of `PresentationState`.
- **No UI tests of lock/unlock or sleep/resume**, and no UI test project (Phase 8). The AutomationIds are set now.
- **No backup step at startup** — `BackupService` is Phase 7 and slots in before the downtime check (parent §1).
