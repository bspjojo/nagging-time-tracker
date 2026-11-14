# Phase 5 — Reminders (implementation plan)

**Status:** Shipped — 1 commit. Build clean, tests 297/297 green. The Phase 3 and 4 UI scripts still pass (34/34, 27/27), the 28 Phase 5 UI checks of §7.2 pass on real ticks, and the tray attention icon was checked by hand.

**Sequencing note:** this doc expands **Phase 5** of [plan-time-tracker-v1.md](plan-time-tracker-v1.md) (§12, "Phase 5 — Reminders") into implementable detail. It builds on Phases 1–4, all shipped (latest commit `6ef4eae`). The parent plan stays the source of truth for reminder behaviour: §6.0–6.4 (engine, the three reminder kinds, the nag window), §4.1 (attention icons) and §8 (settings keys). Phase 6 (idle detection) plugs into the `IIdleState` seam defined here (§3.3) and reads the heartbeat written here (§3.1). Deviations are listed in §8 of this doc and get folded back into the parent once this doc is approved.

## Overview

Phase 5 makes the app nag. Three reminder kinds:

- **Not tracking during work hours**, including overnight shifts.
- **A periodic "Still working on X?" check-in** while timers run.
- **User-defined scheduled reminders.**

Nags appear in the app's own topmost window, one at a time, queued while you're presenting. The tray turns red while one is waiting. Settings gains *Work hours*, *Check-ins* and *Reminders* tabs.

This is one of the two riskiest phases the parent plan deliberately held back (timers, OS state, windows that interrupt). So the design keeps every rule pure and unit-tested in Core: the shift calculation, the engine and its bookkeeping, the nag queue, and the scheduled-reminder save rule. The App side is a thin tick loop and window code, checked with UI Automation on real timings.

**What exists today (confirmed by reading the code):**

- **`SettingsService`** has `Load()` / `Save(AppSettings)`, which raises `SettingsChanged`, and `GetInstant` / `SetInstant` for the state keys `heartbeat_utc`, `reminders_paused_until_utc` and `not_working_until_utc`. Those don't raise the event ([SettingsService.cs:18-66](src/NaggingTimeTracker.Core/Settings/SettingsService.cs#L18-L66)). `AppSettings` already holds every Phase 5 setting with the §8 defaults ([AppSettings.cs](src/NaggingTimeTracker.Core/Settings/AppSettings.cs)).
- **The tray's Pause reminders submenu** already writes `reminders_paused_until_utc` (Phase 2). Nothing reads it yet.
- **`IReminderRepository`** has `GetAll`, `GetById`, `Insert`, `Update`, `Delete` and `SetLastFired`, tested in Phase 1 ([Repositories.cs:86-96](src/NaggingTimeTracker.Core/Abstractions/Repositories.cs#L86-L96)). The `reminders` table and `ScheduledReminder` record exist.
- **`TrayHost.SetAttention(bool)`** exists, but nothing calls it yet ([TrayHost.cs:76](src/NaggingTimeTracker.App/Tray/TrayHost.cs#L76)). The 11 `attention-*` icons are generated and selected by `TrayIconSelector`.
- **UI flows:**
  - `TrackingActions.StopSingle` handles the TooShort prompt and `ContinueLast` the confirm-and-retry flow ([TrackingActions.cs:30-37](src/NaggingTimeTracker.App/Tracking/TrackingActions.cs#L30-L37)).
  - `EntryEditorFactory.Show(id, owner)` opens the editor ([EntryEditorFactory.cs:16](src/NaggingTimeTracker.App/ViewModels/EntryEditorFactory.cs#L16)).
  - `TrayHost.ShowQuickPanel()` opens the quick panel ([TrayHost.cs:66](src/NaggingTimeTracker.App/Tray/TrayHost.cs#L66)).
- **`--no-background`** is parsed into `StartupOptions.NoBackground`, but nothing reads it yet (confirmed by grep).
- **The Settings window** selects its tab **by index** (`SelectTab(tab) => SelectedTabIndex = (int)tab`, [SettingsWindow.xaml.cs:21](src/NaggingTimeTracker.App/Windows/SettingsWindow.xaml.cs#L21)). It has two tabs ([SettingsWindow.xaml:41-44](src/NaggingTimeTracker.App/Windows/SettingsWindow.xaml#L41-L44)). Inserting tabs before them would silently break `SettingsTab.Customers`, so §6.1 switches to selecting by tag.
- **DST helpers:** `LocalDateRange` already moves a midnight inside a DST gap forward to the first valid minute, and resolves an ambiguous time to the earlier instant ([LocalDateRange.cs:21-27](src/NaggingTimeTracker.Core/Time/LocalDateRange.cs#L21-L27)). `LocalTime.ToUtc` *rejects* gap times, which is right for the editor ([LocalTime.cs:36-40](src/NaggingTimeTracker.Core/Time/LocalTime.cs#L36-L40)). Shift times need the lenient behaviour (parent §6.1), so that logic becomes a shared helper (§1.1).
- **Host wiring:** the Generic Host is built in `App.BuildHost` and started at [App.xaml.cs:57](src/NaggingTimeTracker.App/App.xaml.cs#L57). There are no hosted services yet.

**Decisions already made (givens):**

- **From the parent:** everything in §6. The engine evaluates every 30 s. Suppression goes pause → prompt open or queued → idle → presentation (queued, not dropped). Bookkeeping is in memory, except `last_fired_utc` and the `*_until_utc` settings. Nags are the app's own window, not toasts, with no auto-dismiss, and ✕ counts as Snooze 15 min.
- **Stakeholder decisions for this phase:**
  - The Settings tabs **apply immediately**: each valid change is saved when the field loses focus or a box is ticked, an invalid value shows an inline error and isn't saved, and there's no Save button.
  - The check-in nag offers **Snooze 15 / 30 / 60 min**, like the not-tracking nag.

**Commit granularity:** one commit, "Phase 5 — reminders", per the parent's one-commit-per-phase rule.

---

## 1. Core: time and shifts

### 1.1 `LocalTime.ToUtcLenient`

Add to [LocalTime.cs](src/NaggingTimeTracker.Core/Time/LocalTime.cs):

```csharp
/// A local date+time to UTC that never fails: a time inside a DST gap moves forward to the
/// first valid minute; an ambiguous time resolves to the earlier instant.
public static DateTimeOffset ToUtcLenient(DateOnly date, TimeOnly time, TimeZoneInfo tz);
```

`LocalDateRange.StartOfLocalDay` is rewritten as `LocalTime.ToUtcLenient(day, TimeOnly.MinValue, tz)`, so both share one implementation. `LocalDateRangeTests` keeps passing unchanged, which acts as its regression test.

### 1.2 `Core/Reminders/WorkSchedule.cs`

```csharp
public sealed record Shift(DateTimeOffset StartUtc, DateTimeOffset EndUtc);

public static class WorkSchedule
{
    public static string? Validate(TimeOnly start, TimeOnly end);   // "Start and end can't be the same." or null
    public static bool IsOvernight(TimeOnly start, TimeOnly end) => end < start;
    public static Shift? CurrentShift(DateTimeOffset nowUtc, AppSettings s, TimeZoneInfo tz);
}
```

`CurrentShift`, following parent §6.1:

1. Returns `null` if `Validate(s.WorkStart, s.WorkEnd)` fails.
2. Takes `todayLocal` = the local date of `nowUtc`.
3. Checks the candidate start days `todayLocal - 1`, then `todayLocal`. A candidate counts only when its weekday bit is set in `s.WorkDaysMask`, since the checkboxes refer to the day a shift **starts**.
4. Each candidate shift runs from `ToUtcLenient(D, start)` to `ToUtcLenient(end > start ? D : D + 1, end)`.
5. Returns the first shift with `StartUtc <= nowUtc < EndUtc`, otherwise `null`.

---

## 2. Core: engine, queue, reminder service

### 2.1 `Core/Reminders/Nag.cs`

```csharp
public enum NagKind { NotTracking, CheckIn, Scheduled }

public sealed record CheckInItem(Entry Entry, bool Due);

public sealed record Nag(
    NagKind Kind,
    string Title,
    string Message,
    long? ReminderId = null,                      // Scheduled
    IReadOnlyList<CheckInItem>? CheckIns = null,  // CheckIn: due first, then not due
    Shift? Shift = null);                         // NotTracking: for "Not working this shift"
```

**Text:**

- **NotTracking:** title *"You're not tracking anything."*, empty message.
- **CheckIn:** title *"Still working on this?"* with one item, or *"Still working on these?"* with several. The App renders the amber *"⚠ N timers running in parallel"* header itself when ≥2 items are listed (parent §4.1).
- **Scheduled:** the reminder's title (plus the missed suffix, §2.3) and its message.

### 2.2 `Core/Reminders/ReminderState.cs`

The inputs, built fresh each tick by `ReminderHost`:

```csharp
public sealed record ReminderState(
    DateTimeOffset NowUtc,
    TimeZoneInfo Tz,
    AppSettings Settings,
    IReadOnlyList<Entry> Running,
    DateTimeOffset? LastStopUtc,                 // most recent stop of any entry (GetRecentStopped(1))
    IReadOnlyList<ScheduledReminder> Reminders,  // all; the engine checks Enabled
    DateTimeOffset? PausedUntilUtc,
    DateTimeOffset? NotWorkingUntilUtc,
    bool IsUserIdle,
    bool IsPresentationBusy,
    bool IsPromptOpen);
```

### 2.3 `Core/Reminders/ReminderEngine.cs`

The parent puts the in-memory bookkeeping inside `ReminderState`. Here the engine is a **class that owns its bookkeeping**, registered once as a singleton, and `ReminderState` carries only inputs. That keeps the engine's memory private, and the nag actions call engine methods instead of editing shared state (§8).

```csharp
public sealed class ReminderEngine(DateTimeOffset appStartUtc)
{
    public IReadOnlyList<Nag> Evaluate(ReminderState s);

    // Actions from the nag window:
    public void SnoozeNotTracking(DateTimeOffset untilUtc);
    public void CheckedIn(long entryId, DateTimeOffset nowUtc);                                   // Still on it
    public void SnoozeCheckIns(IEnumerable<long> entryIds, TimeSpan snooze, DateTimeOffset nowUtc, TimeSpan interval);
    public void SnoozeScheduled(Nag nag, DateTimeOffset untilUtc);
}
```

**Bookkeeping fields:**

- `_notTrackingLastFired` and `_notTrackingSnoozedUntil` (`DateTimeOffset?`);
- `_lastCheckIn` (`Dictionary<long, DateTimeOffset>`);
- `_snoozedScheduled` (`List<(Nag Nag, DateTimeOffset Until)>`).

**`Evaluate(s)`**, in parent §6.0's order:

1. `PausedUntilUtc > now` → `[]`.
2. `IsPromptOpen` → `[]`, without touching any bookkeeping.
3. `IsUserIdle` → `[]`.
4. `IsPresentationBusy` does **not** suppress anything here. It's the queue's concern (§2.4).
5. Prune `_lastCheckIn` keys that aren't running any more.
6. Collect, in this order:
   - due **snoozed scheduled** nags (`Until <= now`), removed from the list;
   - due **scheduled** reminders;
   - **not tracking**;
   - **check-in**.

**Scheduled** (parent §6.3), for each enabled reminder whose `DaysMask` has today's local weekday bit:

- `occurrence = ToUtcLenient(todayLocal, r.TimeLocal, tz)`.
- It's due when `occurrence <= now` and (`LastFiredUtc is null || LastFiredUtc < occurrence`).
- The title is `r.Title`, plus `" (missed at {local occurrence:t})"` when `now - occurrence > 2 min` (`t` in the current culture). `ReminderId = r.Id`.
- The engine never writes `last_fired_utc`; `ReminderHost` does (§3.1).

**Not tracking** (parent §6.1) fires when:

- `Settings.NagNotTrackingEnabled` is on;
- `NotWorkingUntilUtc` is null or `<= now`;
- `_notTrackingSnoozedUntil` is null or `<= now`;
- `shift = CurrentShift(...)` isn't null;
- `Running` is empty;
- and `now >= max(_notTrackingLastFired, LastStopUtc, shift.StartUtc) + interval`.

When it fires: `_notTrackingLastFired = now`, and the nag carries `Shift = shift`.

**Check-in** (parent §6.2):

- **Due entries:** an entry is due when `Settings.CheckinEnabled` is on and `now - max(e.StartUtc, _lastCheckIn.GetValueOrDefault(e.Id, appStartUtc)) >= interval`. Using `appStartUtc` as the default means a restart doesn't produce a burst of check-ins.
- **One nag per tick:** if any entry is due, a single nag lists **all** running entries, due first (each by `StartUtc`), then not due.
- **No bookkeeping on fire:** the open nag suppresses further evaluation (`IsPromptOpen`) until the user acts or the nag is pruned (§2.4).

**Actions:**

- **`CheckedIn(id, now)`** sets `_lastCheckIn[id] = now`.
- **`SnoozeCheckIns(ids, S, now, interval)`** sets `_lastCheckIn[id] = now + S - interval` for each id, so each comes due again exactly S later. That's parent §6.2's formula.
- **`SnoozeNotTracking(until)`** sets `_notTrackingSnoozedUntil = until`.
- **`SnoozeScheduled(nag, until)`** adds the nag to `_snoozedScheduled`. It reappears unchanged, with the same title and no new suffix.

### 2.4 `Core/Reminders/NagQueue.cs`

The pure part of the parent's `NagWindowManager`, so the queue rules are unit-tested:

```csharp
public sealed class NagQueue
{
    public Nag? Current { get; }                  // the nag on screen
    public bool HasPending { get; }               // Current or anything queued → IsPromptOpen, attention icon
    public void Enqueue(IEnumerable<Nag> nags);
    public Nag? ShowNextIfIdle(bool presentationBusy);   // promotes the head to Current when nothing is shown and not busy
    public void CloseCurrent();
    public Nag? Prune(IReadOnlySet<long> runningIds);    // returns the updated Current (or null if it was removed)
    public void ResolveCheckIn(long entryId);    // a due row was handled; Current closes when no due rows remain
    public IReadOnlyList<long> UnansweredDueCheckIns();  // Current's due rows not yet handled: what a check-in snooze delays
}
```

**`Prune`** runs on `RunningEntriesChanged`:

- **Not-tracking nags** are dropped, both shown and queued, as soon as anything is running. Starting a timer answers them.
- **Check-in nags** lose the items whose entries are no longer running. A check-in nag left with no **due** items is dropped.
- When the shown nag is dropped, `Current` becomes `null`. The manager then calls `ShowNextIfIdle`.

**`ResolveCheckIn`:** after **Still on it**, **Stop** and the rest, the row is marked handled. The nag closes once every due row is handled. Not-due rows are only context and never keep it open.

### 2.5 `Core/Reminders/ScheduledReminderService.cs`

Parent §6.3's single save path:

```csharp
public sealed class ScheduledReminderService(IReminderRepository repo, TimeProvider time)
{
    public ScheduledReminder Save(ScheduledReminder r);   // insert when Id == 0, else update
    public IReadOnlyList<ScheduledReminder> GetAll();
    public bool Delete(long id);
    public bool SetEnabled(long id, bool enabled);        // through Save, so the no-instant-fire rule applies
}
```

**`Save`:**

- **Validation:** throws `ArgumentException` for an empty or whitespace title, or `DaysMask` outside 1–127.
- **No instant fire:** if today's local weekday bit is set and today's occurrence (`ToUtcLenient`) `<= now`, it sets `LastFiredUtc = now` before writing. The reminder then first fires at its next occurrence.
- **Otherwise `LastFiredUtc` stays as it is:** the incoming value for an update, `null` for an insert. In particular, moving a reminder that already fired today to a *later* time today makes it fire again at the new time, which is the intended behaviour, and moving it to an *earlier*, already-passed time doesn't.
- **Trimming:** the title is trimmed, and a `null` message becomes `""`.

---

## 3. App: tick loop, presentation and idle state

### 3.1 `App/Reminders/ReminderHost.cs`

An `IHostedService`. **It isn't registered under `--no-background`**, which is the first consumer of that switch.

- **`StartAsync`** starts a background `Task` that loops on `PeriodicTimer(TimeSpan.FromSeconds(30))` and also ticks once immediately. Each tick runs `Tick()` **on the UI thread** through `await dispatcher.InvokeAsync(Tick)`, where `dispatcher` is `Application.Current.Dispatcher`, captured in `StartAsync`. Awaiting means ticks never overlap. Everything the tick touches (`NagWindowManager`, the engine's bookkeeping, the repositories) is then used from a single thread, so nothing needs locking. The repository reads are local SQLite calls of a few milliseconds.
- **`StopAsync`** cancels the token and awaits the loop.
- **Shutdown:** the loop exits on `OperationCanceledException`. It also skips the dispatch when `dispatcher.HasShutdownStarted` is set, which avoids a `TaskCanceledException` from `InvokeAsync` while the app is exiting.
- **`Tick()`:**
  1. Builds the state:
     - `now = time.NowUtc()`;
     - `settings.Load()`;
     - `tracking.GetRunning()`;
     - `entries.GetRecentStopped(1)` for its `StopUtc`;
     - `reminders.GetAll()`;
     - the two instants `GetInstant(RemindersPausedUntilUtc)` and `GetInstant(NotWorkingUntilUtc)`;
     - `idle.IsUserIdle`, `presentation.IsBusy()` and `nags.HasPending`.
  2. Calls `engine.Evaluate(state)`.
  3. For each nag with `ReminderId`, calls `reminders.SetLastFired(id, now)`.
  4. Calls `nags.Enqueue(nags)`.
  5. Writes the heartbeat: `settings.SetInstant(HeartbeatUtc, now)` when at least 60 s have passed since the last write. The first tick always writes it.
  6. Catches and swallows any exception, writing it to `Debug.WriteLine`. A transient failure such as a locked database must not kill the loop. The next tick retries.

### 3.2 `App/Reminders/PresentationState.cs`

```csharp
public sealed class PresentationState
{
    public bool IsBusy();   // SHQueryUserNotificationState: QUNS_BUSY (2), QUNS_RUNNING_D3D_FULL_SCREEN (3), QUNS_PRESENTATION_MODE (4)
}
```

It uses `[DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);`, which goes in `App/Reminders/NativeMethods.cs`. A failed HRESULT is treated as "not busy". `QUNS_APP` (7) is deliberately excluded (parent §6.0).

### 3.3 `App/Reminders/IIdleState.cs`

```csharp
public interface IIdleState { bool IsUserIdle { get; } }
public sealed class NeverIdle : IIdleState { public bool IsUserIdle => false; }
```

`NeverIdle` is registered as a singleton in this phase. Phase 6's `IdleMonitor` implements `IIdleState` and replaces that registration. This is the seam the sequencing note refers to.

---

## 4. App: nag window

### 4.1 `App/Reminders/NagWindowManager.cs`

A singleton. Constructor: `(ReminderEngine engine, TrackingService tracking, TrackingActions actions, EntryEditorFactory editors, CatalogService catalog, SettingsService settings, PresentationState presentation, TrayHost tray, TimeProvider time)`.

- **Queue:**
  - Owns a `NagQueue` and exposes `HasPending`.
  - **`Enqueue(nags)`:** queue enqueue, then `ShowNext()`, then `tray.SetAttention(HasPending)`.
  - **`ShowNext()`:** `queue.ShowNextIfIdle(presentation.IsBusy())`. If a nag is promoted, it opens a `NagWindow` for it. While the queue holds nags but none is shown because of presentation mode, a 30-s `DispatcherTimer` re-checks `IsBusy()` and stops itself once something is shown (parent §6.0).
- **Pruning:** subscribes to `tracking.RunningEntriesChanged`, calls `queue.Prune(runningIds)` and refreshes the shown window: it updates the check-in rows, or closes the window if its nag was dropped. Then `ShowNext()`, then attention.
- **Opening other windows:** any action that opens the quick panel or the editor **closes the nag first** (parent §6.4), and the nag counts as handled.
- **Action handlers**, called by the window:

  | Nag | Action | Effect |
  |---|---|---|
  | NotTracking | **Start…** | close; `tray.ShowQuickPanel()` |
  | NotTracking | **Continue {customer · type}** | close; `actions.ContinueLast(owner: null)`. The label comes from `GetRecentStopped(1)` and its names; the button is hidden if there are no stopped entries |
  | NotTracking | **Snooze 15 / 30 / 60 min** | close; `engine.SnoozeNotTracking(now + S)` |
  | NotTracking | **Not working this shift** | close; `settings.SetInstant(NotWorkingUntilUtc, nag.Shift.EndUtc)` |
  | CheckIn (per row) | **Still on it** | `engine.CheckedIn(id, now)`; `queue.ResolveCheckIn(id)` |
  | CheckIn (per row) | **Stop** | `actions.StopSingle(id, owner: null)`, which offers TooShort (parent §3); then `queue.ResolveCheckIn(id)`. The prompt has **no owner**: the stop raises `RunningEntriesChanged`, and pruning can close the nag window, which would take an owned dialog down with it |
  | CheckIn (per row) | **Stop & add note** | close the nag; `tracking.Stop(id)`; `editors.Show(id, owner: null)` |
  | CheckIn (per row) | **Switch…** | close the nag; `tracking.Stop(id)`; `tray.ShowQuickPanel()` |
  | CheckIn | **Snooze 15 / 30 / 60 min** | take `queue.UnansweredDueCheckIns()` (the due rows not yet handled; read before closing, which clears the handled set), then close; `engine.SnoozeCheckIns(those ids, S, now, interval)`. Answered and not-due rows keep their own clock (parent §6.2) |
  | Scheduled | **Dismiss** | close |
  | Scheduled | **Snooze 15 min** | close; `engine.SnoozeScheduled(nag, now + 15 min)` |
  | any | **✕** (window close) | the kind's 15-min snooze (parent §6.4) |

  *Stop & add note* and *Switch…* stop the entry first and then open the editor or quick panel, so the TooShort prompt isn't offered there. A one-second entry can be deleted from the editor or the Entries window. Of the four check-in actions, only plain **Stop** goes through `StopSingle`.

  "Close" means `queue.CloseCurrent()`, then closing the window, `ShowNext()` and the attention update.

### 4.2 `App/Windows/NagWindow.xaml(.cs)` + `App/ViewModels/NagViewModel.cs`

- **Window:** `WindowStyle=None`, `ResizeMode=NoResize`, `Topmost=True`, `ShowActivated=False`, `ShowInTaskbar=False`, `Width=380`, `SizeToContent=Height`, a 1-px border and a 4-px accent strip on the left. The strip is red for nags, matching the attention icon. The window sits bottom-right of `SystemParameters.WorkArea`, 8 px in, placed after layout (`SizeChanged`), as the quick panel does.
- **The header row:** the title and a ✕ button.
- **Body by kind:**
  - **NotTracking:** **Start…**, **Continue {label}** (when there is one), a *Snooze:* row of **15 min / 30 min / 1 h**, and **Not working this shift**.
  - **CheckIn:** the amber parallel header when there are ≥2 items. Then one row per item: `customer · type — elapsed` (elapsed via `DurationFormat.Short`) and **Still on it / Stop / Stop & add note / Switch…**. A handled row greys out and disables its buttons. The *Snooze:* row comes last.
  - **Scheduled:** the message, wrapped, then **Dismiss** and **Snooze 15 min**.
- **The view-model** exposes the kind flags, the rows (`NagCheckInRowViewModel` with `IsHandled`) and commands that call the manager's handlers.

**AutomationIds:**

- **Window and header:** `NagWindow`, `NagWindow.Title`, `.Message`, `.CloseButton`, `.ParallelHeader`.
- **NotTracking buttons:** `.StartButton`, `.ContinueButton`, `.NotWorkingButton`.
- **Snooze buttons:** `.Snooze15Button`, `.Snooze30Button`, `.Snooze60Button`.
- **Check-in rows:** `.CheckInRow` (on the item container, with `AutomationProperties.Name` = the row label, per the Phase 3 convention), `.CheckInRow.StillOnItButton`, `.CheckInRow.StopButton`, `.CheckInRow.StopAndNoteButton`, `.CheckInRow.SwitchButton`.
- **Scheduled:** `.DismissButton`.

---

## 5. Wiring

[App.xaml.cs](src/NaggingTimeTracker.App/App.xaml.cs) `BuildHost`:

- `services.AddSingleton(sp => new ReminderEngine(sp.GetRequiredService<TimeProvider>().NowUtc()))`, which fixes the app start time.
- `services.AddSingleton<ScheduledReminderService>()`, `AddSingleton<PresentationState>()`, `AddSingleton<IIdleState, NeverIdle>()` and `AddSingleton<NagWindowManager>()`.
- `if (!options.NoBackground) services.AddHostedService<ReminderHost>();`

`ReminderHost` dispatches to `Application.Current.Dispatcher`, which exists before `host.StartAsync()` runs ([App.xaml.cs:57](src/NaggingTimeTracker.App/App.xaml.cs#L57)). Its first tick runs after `OnStartup` returns, because `InvokeAsync` queues behind the startup code. So the tray already exists when the first nag needs `SetAttention`.

---

## 6. Settings tabs

### 6.1 Tab selection by tag

- `SettingsTab` becomes `WorkHours, CheckIns, Reminders, Customers, TaskTypes`.
- Each `TabItem` gets `Tag="{x:Static windows:SettingsTab.X}"`.
- `SettingsWindow.SelectTab(tab)` selects the `TabItem` whose `Tag` equals `tab`. `SelectedTabIndex` is removed from `SettingsViewModel`.
- Phases 6–7 can then insert *Idle* and *General* without breaking any caller.
- `ShowSettings()` with no argument opens **Work hours**, the first tab.

### 6.2 `App/ViewModels/ReminderSettingsViewModel.cs`

One view-model for the *Work hours* and *Check-ins* tabs, both of which edit `AppSettings`. It applies immediately (stakeholder decision).

- **Loading:** one `settings.Load()` when the window opens.
- **Fields:**
  - seven `bool` day properties, Mon…Sun (the UI order; the bits per §2 of the parent);
  - `WorkStartText` / `WorkEndText`, as `HH:mm` text;
  - `NotTrackingEnabled` and `NotTrackingIntervalText`;
  - `CheckInEnabled` and `CheckInIntervalText`;
  - read-only `OvernightHint` (*"(overnight — ends the next day)"* when the end is before the start) and `WorkHoursError` / `CheckInsError`.
- **`TryApply()`** runs from the setter of each checkbox, and from each text box on `LostFocus` (the default `UpdateSourceTrigger`):
  - It parses the times with `LocalTime.TryParseTime`, and the intervals as integers from 1 to 1440.
  - It runs `WorkSchedule.Validate`.
  - On success: `settings.Save(current with { … })`.
  - On failure: sets the tab's error (*"Times must be like 09:00."*, *"Start and end can't be the same."*, *"Interval must be 1–1440 minutes."*) and saves nothing. The other tab's valid values are unaffected, because each save writes the last *valid* value of every field.
- **No work day ticked** is valid. Its hint reads *"No work days — the not-tracking reminder never fires."*

### 6.3 `App/ViewModels/ScheduledRemindersViewModel.cs` + `App/Windows/ReminderEditorDialog.xaml(.cs)`

- **The Reminders tab:**
  - a `DataGrid`, read-only and single-select, with the columns **On** (a checkbox that calls `SetEnabled` when toggled), **Time** (`HH:mm`), **Days** (e.g. `Mon–Fri`, `Mon, Wed`, `Every day`, from a `DaysMaskText` helper in Core with tests) and **Title**;
  - the buttons **Add…**, **Edit…** (also double-click) and **Delete** (with confirmation, *"Delete the reminder '{title}'?"*).
- **`ReminderEditorDialog`:**
  - **Fields:** Title (required), Message (multiline, optional), seven day checkboxes Mon…Sun (all on for a new reminder) and a Time box (`HH:mm`; a new reminder defaults to the next whole hour).
  - **Saving:** Save goes through `ScheduledReminderService.Save`. Validation errors show inline and the dialog stays open, like the entry editor: *"A title is required."*, *"Pick at least one day."*, *"Time must be like 16:30."*
  - **The rule, visible:** the hint *"Today's time has passed — the first reminder will be on the next selected day."* appears when the no-instant-fire rule will apply, so the user can see it.

**AutomationIds:**

- **Work hours tab:** `SettingsWindow.WorkHoursTab`, `.WorkDayMon`…`.WorkDaySun`, `.WorkStart`, `.WorkEnd`, `.OvernightHint`, `.NotTrackingEnabled`, `.NotTrackingInterval`, `.WorkHoursError`.
- **Check-ins tab:** `SettingsWindow.CheckInsTab`, `.CheckInEnabled`, `.CheckInInterval`, `.CheckInsError`.
- **Reminders tab:** `SettingsWindow.RemindersTab`, `.RemindersGrid`, `.ReminderAddButton`, `.ReminderEditButton`, `.ReminderDeleteButton`.
- **Reminder editor:** `ReminderEditor`, `ReminderEditor.TitleBox`, `.MessageBox`, `.DayMon`…`.DaySun`, `.TimeBox`, `.Hint`, `.Error`, `.SaveButton`, `.CancelButton`.

---

## 7. Tests

### 7.1 Unit tests (`tests/NaggingTimeTracker.Tests/`)

All use `FakeTimeProvider` with `America/New_York` unless noted.

| File | Cases |
|---|---|
| `Core/LocalTimeTests.cs` (add) | `ToUtcLenient`: a normal time; 2026-03-08 02:30 → 03:00 EDT (07:00Z); 2026-11-01 01:30 → the earlier instant (05:30Z). |
| `Core/WorkScheduleTests.cs` | Day shift Mon 09–17: in at Mon 10:00, out at Mon 08:59 and at 17:00 (end exclusive). Overnight Fri 22:00–06:00 (Mon–Fri): in at Sat 03:00, out at Sat 07:00, out at Sat 23:00 (Saturday isn't a work day). Overnight with Sunday unticked: Mon 02:00 is out, because that shift would have started Sunday. Spring forward: a shift starting at 02:30 on 2026-03-08 starts at 03:00 EDT. `Validate(09:00, 09:00)` → error, and `CurrentShift` is `null`. `IsOvernight`. |
| `Core/ReminderEngineTests.cs` | See the list below. |
| `Core/NagQueueTests.cs` | FIFO order. `HasPending` covers shown + queued. Presentation-busy holds the queue, then releases it. Pruning drops not-tracking nags when something runs. Pruning removes stopped entries from a check-in, and drops the nag when no due item remains. `ResolveCheckIn` closes the nag only after the last due row. Pruning the shown nag clears `Current`. |
| `Core/ScheduledReminderServiceTests.cs` | Saving after today's time sets `LastFiredUtc = now`. Saving before it leaves `null`. A weekday not in the mask leaves `null`. An empty title and `DaysMask = 0` are rejected. Update keeps the id. `SetEnabled` re-applies the rule. |
| `Core/DaysMaskTextTests.cs` | 62 → `Mon–Fri`; 127 → `Every day`; 65 → `Sat, Sun`; 10 → `Mon, Wed`; 0 → `Never`. |

`ReminderEngineTests.cs` cases:

- **Not tracking:**
  - fires only inside a shift;
  - the interval is measured from the shift start and from the last stop;
  - honours its snooze;
  - honours "not working this shift" (`NotWorkingUntilUtc` = the shift end), and fires again after that end, including an overnight end;
  - honours pause;
  - is silent when disabled, and silent while anything runs.
- **Check-in:**
  - an entry is due after the interval from its start;
  - two entries due on the same tick give one nag;
  - with 2 running and 1 due, the nag lists both, due first;
  - `CheckedIn` resets an entry's clock;
  - `SnoozeCheckIns` brings it due again exactly S later;
  - no burst after a restart (an entry started long before `appStartUtc` isn't due until `appStartUtc + interval`);
  - stopped entries are pruned from the bookkeeping;
  - silent when disabled.
- **Scheduled:**
  - fires once per occurrence, given `LastFiredUtc` updated as `ReminderHost` would;
  - a missed occurrence the same day fires with *" (missed at …)"*;
  - an occurrence fired within 2 minutes has no suffix;
  - yesterday's occurrence never fires;
  - a weekday not in the mask is skipped, and so is a disabled reminder;
  - the nag carries `ReminderId`;
  - `SnoozeScheduled` re-emits the same nag after 15 min.
- **Suppression:**
  - pause, idle and prompt-open (whether the nag is shown or only queued) return nothing and don't advance bookkeeping, so the same nag comes back once they clear;
  - presentation-busy still returns nags.

### 7.2 UI checks (scripted with UI Automation against a scratch `--data-dir`, as in Phases 2–4)

These run on real time: the 30-s tick is the real one. Windows PowerShell 5.1 can't load the app's .NET 10 SQLite library, so the script uses `python -c` with the standard `sqlite3` module whenever it reads or writes the scratch database. It writes only while the app is closed, and only reads while the app runs. The script sets short intervals through the Settings UI, then waits. The longest wait is about 2.5 minutes for one check-in round.

1. **Settings tabs:**
   - `--show settings` opens on *Work hours*.
   - Set start = end → the inline error appears and nothing is saved (re-open the window to confirm).
   - Set 22:00–06:00 → the overnight hint appears.
   - Customers and Task types still work: the Phase 3 script's steps 9–10 rely on them, so that script is re-run as a regression check.
2. **Not tracking:**
   - Tick every day, set start to now − 1 h and end to now + 1 h, and set the interval to 1 min.
   - Within 40 s a `NagWindow` appears with *"You're not tracking anything."*, and it **didn't take focus**: `GetForegroundWindow()` (user32 P/Invoke in the script) returns the same handle before and after it appears.
   - **Snooze 15 min** closes it.
   - Untick the check, then re-tick it with interval 1, start a timer from the quick panel → no nag appears while it runs.
3. **Prune:** set up the not-tracking condition again, wait for the nag, then start a timer → the nag closes by itself.
4. **Check-in:**
   - Set the check-in interval to 1 min and start one timer → within about 100 s the check-in nag lists it.
   - **Still on it** → the nag closes.
   - Start a second timer with *Run in parallel*, wait → the nag shows the amber parallel header and two rows.
   - **Stop** on one row → the TooShort prompt doesn't appear, because the entry is over a minute old. The row greys out, and the nag closes once both due rows are handled.
5. **Scheduled:**
   - Add a reminder via the Reminders tab for the next minute.
   - The editor shows no hint, because the time hasn't passed.
   - Within about 90 s the nag shows its title and message. **Dismiss** closes it.
   - The Reminders grid still lists it, and the database has `last_fired_utc` set (read by the script).
6. **Pause:** the tray's *Pause reminders ▸ 1 hour* can't be clicked from the script. Instead, with the app closed, the script writes `reminders_paused_until_utc` (now + 1 h, epoch ms) and the not-tracking settings (every work day, now ± 1 h, interval 1) straight into the scratch database, then relaunches. No nag appears for 70 s.
7. **Background off:** with the app closed, the script deletes the pause row and relaunches with `--no-background`. Under the same conditions, no nag appears within 70 s. A final relaunch without the switch shows the nag, which confirms the conditions really were met.
8. **Attention icon:** can't be checked by UI Automation (the tray). It's covered by `NagQueue.HasPending` tests, and a person checks it by hand.

---

### Implementation notes

- **`TrackingService.LastStopped()`** was added for the not-tracking nag's *Continue {customer · type}* label.
- **`Nag` is a class, not a record.** The queue tracks nags by reference, and pruning rewrites a check-in nag's rows in place (`CheckIns` has an `internal` setter).
- **A user close of the nag window** (Alt+F4) is turned into the ✕ snooze in `NagWindow.OnClosing`. Only `NagWindowManager` closes nag windows for real (`CloseQuietly`).
- **The reminder editor's message box is named `MessageText`** in code, so it doesn't hide WPF's `MessageBox` class. Its AutomationId stays `ReminderEditor.MessageBox`.
- **Opening Settings in the UI script:** a second launch with `--show settings` only signals the running instance, which opens its quick panel (by design, parent §4.2). The script opens Settings through the quick panel's footer link instead. The Phase 3 script now selects the Customers tab first, because Settings opens on *Work hours*.
- **The UI checks take about 8 minutes** of real time. They ran in the background, and `p5db.py` (Python `sqlite3`) wrote the scratch database while the app was closed.

## 8. Deviations from the parent (fold into [plan-time-tracker-v1.md](plan-time-tracker-v1.md))

1. **The engine owns its bookkeeping.** `ReminderEngine` is a singleton class with action methods; `ReminderState` carries inputs only. The parent §6.0 puts the bookkeeping in `ReminderState`.
2. **New pure types:** `NagQueue` (the queue, prune and resolve rules, extracted from `NagWindowManager`), `IIdleState` / `NeverIdle` (the Phase 6 seam), `LocalTime.ToUtcLenient` (shared with `LocalDateRange`), `DaysMaskText`, and `Shift`.
3. **The check-in nag closes when every *due* row is handled.** Pruning drops a not-tracking nag once a timer starts, and drops check-in rows for stopped entries. The parent doesn't define either. Check-in snooze offers **15 / 30 / 60 min** (stakeholder decision; the parent says "Snooze S min").
4. **The ticks run on the UI thread** (dispatched from the `PeriodicTimer` loop), and a failed tick is swallowed so the next tick retries.
5. **The Settings tabs apply immediately** (stakeholder decision). `SettingsTab` gains WorkHours, CheckIns and Reminders, and tabs are selected by tag, not index.
6. **Stop & add note and Switch… skip the TooShort prompt.** They open another window straight after stopping. Only **Stop** goes through `TrackingActions.StopSingle`. The parent §3 lists all three as single stops.

---

## Steps

- [x] Core: `LocalTime.ToUtcLenient` in [LocalTime.cs](src/NaggingTimeTracker.Core/Time/LocalTime.cs), and rewrite `LocalDateRange.StartOfLocalDay` on top of it (§1.1)
- [x] Core: `Reminders/WorkSchedule.cs` with `Shift` (§1.2)
- [x] Core: `Reminders/Nag.cs`, `Reminders/ReminderState.cs`, `Reminders/ReminderEngine.cs` (§2.1–2.3)
- [x] Core: `Reminders/NagQueue.cs` (§2.4), `Reminders/ScheduledReminderService.cs` (§2.5), `Formatting/DaysMaskText.cs`
- [x] Tests: the five new files and the `LocalTimeTests` additions from §7.1
- [x] App: `Reminders/IIdleState.cs`, `Reminders/PresentationState.cs` + `Reminders/NativeMethods.cs`, `Reminders/ReminderHost.cs` (§3)
- [x] App: `Reminders/NagWindowManager.cs`, `ViewModels/NagViewModel.cs`, `Windows/NagWindow.xaml(.cs)` (§4)
- [x] App: DI and the `--no-background` gate in [App.xaml.cs](src/NaggingTimeTracker.App/App.xaml.cs) (§5)
- [x] App: `SettingsTab` and tab selection by tag in [WindowManager.cs](src/NaggingTimeTracker.App/Windows/WindowManager.cs), [SettingsWindow.xaml(.cs)](src/NaggingTimeTracker.App/Windows/SettingsWindow.xaml) and [SettingsViewModel.cs](src/NaggingTimeTracker.App/ViewModels/SettingsViewModel.cs) (§6.1)
- [x] App: `ViewModels/ReminderSettingsViewModel.cs` and the Work hours and Check-ins tabs (§6.2)
- [x] App: `ViewModels/ScheduledRemindersViewModel.cs`, the Reminders tab, `Windows/ReminderEditorDialog.xaml(.cs)` (§6.3)
- [x] `dotnet build` (zero warnings) and `dotnet test --filter "Category!=UI"` (all green)
- [x] Re-run the Phase 3 and Phase 4 UI scripts (regression), then the §7.2 checks; record the results in the commit message
- [x] Update [README.md](README.md): the status line (Phases 1–5, reminders); the `--no-background` row (now effective); add Conventions bullets on the UI-thread tick and the engine owning its bookkeeping
- [x] Tick the parent's Phase 5 checklist in [plan-time-tracker-v1.md](plan-time-tracker-v1.md), fold in the §8 deviations (§3, §6, §8), and flip this doc's Status to Shipped
- [x] Commit "Phase 5 — reminders"

---

## Non-goals

- **No idle detection** and no downtime prompt. `IIdleState` always reports "not idle" until Phase 6 replaces it, and the heartbeat is written now but read in Phase 6.
- **No Windows toast notifications** (parent Non-goals and §6.4).
- **No sounds,** and no flashing beyond the attention icon.
- **No per-day or split work hours** (parent Non-goals).
- **No snooze or "not working" across restarts,** except `not_working_until_utc` and pause, which the parent persists. Other snoozes are in memory.
- **No reminder recurrence beyond weekly weekdays,** no dates and no one-off reminders.
- **No catching up on earlier days' missed reminders** (parent §6.3).
- **No multi-monitor placement.** The nag sits on the primary work area, like the quick panel.
- **No UI test project** (Phase 8). The AutomationIds are set now.
