# Scripted UI checks (Phases 2–7)

These are the per-phase manual-verification scripts: each one drives the real app through
Windows UI Automation against a throwaway `--data-dir` and prints `PASS` / `FAIL` lines, then
`DONE failed=N`. Their results are recorded in each phase's commit message.

They are **not** part of `dotnet test`. The proper UI test suite is
`tests/NaggingTimeTracker.UiTests` (FlaUI, [plan-time-tracker-v1.md](../../plan-time-tracker-v1.md)
§10), built in Phase 8; these scripts are reference material for it.

## Running

Build first, then run from Windows PowerShell 5.1 on an interactive desktop:

```powershell
dotnet build
$exe = 'src\NaggingTimeTracker.App\bin\Debug\net10.0-windows\NaggingTimeTracker.exe'
powershell -NoProfile -ExecutionPolicy Bypass -File tools\ui-checks\verify-phase6.ps1 -Exe $exe
```

- `-Out` (optional) is where screenshots go; it defaults to `%TEMP%\ntt-ui-checks`.
- The scripts kill any `NaggingTimeTracker` process started from that same exe path, so close
  a copy you are using from the build folder first. An installed copy is left alone.
- Run one script at a time; they all use the desktop and the same process.
- **Phases 5 and 6 wait on real timers.** Phase 5 takes about 8 minutes, Phase 6 about 4.
  During Phase 6's live-idle step, don't touch the keyboard or mouse for about 90 seconds.
- Phases 5–7 need `python` on `PATH`. Windows PowerShell 5.1 can't load the app's .NET 10
  SQLite library, so `p5db.py` / `p6db.py` (standard `sqlite3` module) read and write the scratch
  database, and they only write while the app is closed.

| Script | Covers |
|---|---|
| `verify-phase2.ps1` | Quick panel: start, too-short discard, parallel timers, Keep only this, continue; single instance; restart keeps a running timer; bad switch exits 2 |
| `verify-phase3.ps1` | Entries window, editor (errors, overlaps, continue, delete), filters, Customers/Task types archive |
| `verify-phase4.ps1` | CSV export: pre-fill from Entries, running filter, file contents, overwrite, locked file |
| `verify-phase5.ps1` | Reminder settings, nags, pause, `--no-background` |
| `verify-phase6.ps1` | Idle tab, startup downtime prompt, live idle prompt |
| `verify-phase7.ps1` | Daily backup and retention, General tab (data folder, Start with Windows disabled under `--data-dir`) |

Not scriptable, checked by hand: the tray icon; for Phase 6 a lock/unlock (Win+L) longer than
the idle threshold; for Phase 7 the Start with Windows registry value, the Release-only Run-key
self-heal, Open data folder and the exe icon (they touch your real profile, so the script leaves
them out).
