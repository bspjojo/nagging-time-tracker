param([string]$Exe, [string]$Out = (Join-Path $env:TEMP 'ntt-ui-checks'))
$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
New-Item -ItemType Directory -Force $Out | Out-Null
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$root = $AE::RootElement

function Log($m) { Write-Output "  $m" }
function Check($cond, $msg) { if ($cond) { Write-Output "PASS $msg" } else { Write-Output "FAIL $msg"; $script:failed++ } }
$script:failed = 0

function Find($parent, $id, [int]$timeoutMs = 5000, [switch]$All) {
    if (-not $parent) { if ($All) { return ,@() } else { return $null } }
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ($All) { $r = $parent.FindAll($TS::Descendants, $cond); if ($r.Count -gt 0) { return ,$r } }
        else { $r = $parent.FindFirst($TS::Descendants, $cond); if ($r) { return $r } }
        Start-Sleep -Milliseconds 100
    } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
    if ($All) { return ,@() } else { return $null }
}
function TopWindow($id, [int]$timeoutMs = 5000) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    do {
        $w = $root.FindFirst($TS::Children, $cond); if ($w) { return $w }
        # Owned dialogs appear under their owner window, not at the top level.
        foreach ($top in $root.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
            if ($app -and $top.Current.ProcessId -eq $app.Id) { $w = $top.FindFirst($TS::Children, $cond); if ($w) { return $w } }
        }
        Start-Sleep -Milliseconds 100
    } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
    return $null
}
function Invoke($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function InvokeAsync($el) {
    # Invoke blocks while a modal dialog it opens is up, so do it from a background runspace.
    $ps = [PowerShell]::Create()
    $null = $ps.AddScript({ param($e) $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }).AddArgument($el)
    return $ps.BeginInvoke()
}
function SetText($el, $text) {
    $edit = $el
    try { $null = $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern) } catch {
        $edit = $el.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
    }
    $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
}
function Visible($el) { $el -and -not $el.Current.IsOffscreen }
function Shot($el, $file) {
    $r = $el.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $bmp.Save($file); $g.Dispose(); $bmp.Dispose()
}

trap { Write-Output "ERROR $_"; Get-Process NaggingTimeTracker -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force; Write-Output "DONE failed=$($script:failed + 1)"; exit 1 }
$data = Join-Path $env:TEMP ("ntt-manual-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$dbHelper = Join-Path $PSScriptRoot 'p5db.py'
$db = Join-Path $data 'tracker.db'
Add-Type -Namespace Win32 -Name Fg -MemberDefinition '[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();'

function Value($el) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Db { param([Parameter(ValueFromRemainingArguments = $true)] $rest) & python $dbHelper $db @rest }
function Child($parent, $id, [int]$timeoutMs = 5000) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    do { $w = $parent.FindFirst($TS::Children, $cond); if ($w) { return $w }; Start-Sleep -Milliseconds 100 } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
    return $null
}
function SelectTab($win, $id) {
    (Find $win $id).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300
}
function Commit($win, $id, $text) {
    # Settings text boxes save on LostFocus: set the value, then move focus away.
    $box = Find $win $id
    $box.SetFocus()
    $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
    (Find $win 'SettingsWindow.Tabs').SetFocus()
    Start-Sleep -Milliseconds 300
}
function SetCheck($win, $id, [bool]$on) {
    $t = (Find $win $id).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $isOn = $t.Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On
    if ($isOn -ne $on) { $t.Toggle(); Start-Sleep -Milliseconds 200 }
}
function Hm([int]$offsetMinutes) { (Get-Date).AddMinutes($offsetMinutes).ToString('HH:mm') }
function WaitNag([int]$seconds) { TopWindow 'NagWindow' ($seconds * 1000) }
function NoNagFor([int]$seconds) { (TopWindow 'NagWindow' ($seconds * 1000)) -eq $null }
function Launch([string[]]$extra) { Start-Process $Exe -ArgumentList (@('--data-dir', $data) + $extra) -PassThru }
function Quit { Get-Process NaggingTimeTracker -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force; Start-Sleep 1 }
function OpenSettings {
    # A second launch only signals the running instance (its quick panel opens): use the footer link.
    $null = Launch @()
    $panel = TopWindow 'QuickPanel' 10000
    Invoke (Find $panel 'QuickPanel.SettingsLink')
    return (TopWindow 'SettingsWindow' 10000)
}
function StartTimer($customer, [switch]$Parallel) {
    $null = Launch @()
    $panel = TopWindow 'QuickPanel' 10000
    SetText (Find $panel 'QuickPanel.CustomerCombo') $customer
    SetText (Find $panel 'QuickPanel.TaskTypeCombo') 'Dev Work'
    $null = InvokeAsync (Find $panel 'QuickPanel.StartButton'); Start-Sleep -Milliseconds 800
    if ($Parallel) {
        $cd = Child $panel 'ConcurrencyDialog'
        Invoke (Find $cd 'ConcurrencyDialog.RunInParallelButton'); Start-Sleep -Milliseconds 600
    }
    [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
}

# 1. Settings tabs
$app = Launch @('--show', 'settings')
$set = TopWindow 'SettingsWindow' 15000
Check ((Find $set 'SettingsWindow.WorkHoursTab').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) "--show settings opens on Work hours"
Commit $set 'SettingsWindow.WorkStart' '10:00'
Commit $set 'SettingsWindow.WorkEnd' '10:00'
Check ((Find $set 'SettingsWindow.WorkHoursError').Current.Name -eq "Start and end can't be the same.") "start = end shows the inline error"
$set.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep -Milliseconds 500
Quit
$app = Launch @('--show', 'settings')
$set = TopWindow 'SettingsWindow' 15000
$end = Value (Find $set 'SettingsWindow.WorkEnd')
Check ($end -eq '17:00') "the invalid end wasn't saved (reopened: '$end')"
Check ((Value (Find $set 'SettingsWindow.WorkStart')) -eq '10:00') "...while the valid start was"
Commit $set 'SettingsWindow.WorkStart' '22:00'
Commit $set 'SettingsWindow.WorkEnd' '06:00'
Check ((Find $set 'SettingsWindow.OvernightHint').Current.Name -like '(overnight*') "22:00–06:00 shows the overnight hint"

# 2. Not tracking: every day, now ± 1 h, every minute
foreach ($d in 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun') { SetCheck $set "SettingsWindow.WorkDay$d" $true }
Commit $set 'SettingsWindow.WorkStart' (Hm -60)
Commit $set 'SettingsWindow.WorkEnd' (Hm 60)
Commit $set 'SettingsWindow.NotTrackingInterval' '1'
$set.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep -Milliseconds 500
$fgBefore = [Win32.Fg]::GetForegroundWindow()
$nag = WaitNag 45
Check ($nag -ne $null) "the not-tracking nag appears within 45 s"
Check ((Find $nag 'NagWindow.Title').Current.Name -eq "You're not tracking anything.") "...with the not-tracking text"
Check ([Win32.Fg]::GetForegroundWindow() -eq $fgBefore) "...without taking focus"
Invoke (Find $nag 'NagWindow.Snooze15Button'); Start-Sleep -Milliseconds 500
Check ((TopWindow 'NagWindow' 500) -eq $null) "Snooze 15 min closes it"
Check (NoNagFor 40) "no nag while snoozed (40 s)"

# 3. Prune: a fresh start resets the in-memory snooze; starting a timer closes the nag by itself
Quit
$app = Launch @()
$nag = WaitNag 45
Check ($nag -ne $null) "after a restart the not-tracking nag is back"
StartTimer 'Acme'
Start-Sleep -Milliseconds 800
Check ((TopWindow 'NagWindow' 500) -eq $null) "starting a timer closes the not-tracking nag by itself"

# 4. Check-in every minute
$set = OpenSettings
SelectTab $set 'SettingsWindow.CheckInsTab'
Commit $set 'SettingsWindow.CheckInInterval' '1'
$set.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
$nag = WaitNag 110
Check ($nag -ne $null) "the check-in nag appears for the running timer"
Check ((Find $nag 'NagWindow.Title').Current.Name -eq 'Still working on this?') "...asking 'Still working on this?'"
Invoke (Find $nag 'NagWindow.CheckInRow.StillOnItButton'); Start-Sleep -Milliseconds 500
Check ((TopWindow 'NagWindow' 500) -eq $null) "Still on it closes it"
StartTimer 'Globex' -Parallel
$nag = $null; $sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt 150) {
    $nag = WaitNag 5
    if ($nag -and (Find $nag 'NagWindow.CheckInRow' -All -timeoutMs 500).Count -ge 2) { break }
    if ($nag) { Invoke (Find $nag 'NagWindow.CheckInRow.StillOnItButton'); $nag = $null; Start-Sleep -Milliseconds 500 }
}
Check ($nag -ne $null) "with two timers the check-in lists both rows"
Check (Visible (Find $nag 'NagWindow.ParallelHeader' 500)) "...under the parallel header"
$rows = Find $nag 'NagWindow.CheckInRow' -All
Log "rows: $(($rows | ForEach-Object { $_.Current.Name }) -join ' | ')"
Invoke ($rows[0].FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'NagWindow.CheckInRow.StopButton'))))
Start-Sleep -Milliseconds 800
Check ((Child $root 'TooShortDialog' 500) -eq $null) "stopping an older entry doesn't offer the too-short prompt"
$nag = TopWindow 'NagWindow' 500
if ($nag) {
    $left = Find $nag 'NagWindow.CheckInRow.StillOnItButton' -All -timeoutMs 500
    foreach ($b in $left) { if ($b.Current.IsEnabled) { Invoke $b; Start-Sleep -Milliseconds 400 } }
}
Check ((TopWindow 'NagWindow' 500) -eq $null) "the nag closes once every due row is answered"

# 5. Scheduled reminder for the next minute (check-ins and not-tracking off first)
$set = OpenSettings
SetCheck $set 'SettingsWindow.NotTrackingEnabled' $false
SelectTab $set 'SettingsWindow.CheckInsTab'
SetCheck $set 'SettingsWindow.CheckInEnabled' $false
SelectTab $set 'SettingsWindow.RemindersTab'
$null = InvokeAsync (Find $set 'SettingsWindow.ReminderAddButton')
$ed = Child $set 'ReminderEditor'
$when = if ((Get-Date).Second -gt 40) { Hm 2 } else { Hm 1 }
SetText (Find $ed 'ReminderEditor.TitleBox') 'Phase5 test'
SetText (Find $ed 'ReminderEditor.MessageBox') 'Check the export'
SetText (Find $ed 'ReminderEditor.TimeBox') $when
Check ((Find $ed 'ReminderEditor.Hint').Current.Name -eq '') "a time later today shows no 'already passed' hint"
$null = InvokeAsync (Find $ed 'ReminderEditor.SaveButton'); Start-Sleep -Milliseconds 600
Check ((Child $set 'ReminderEditor' 500) -eq $null) "the reminder saves"
$set.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
$nag = WaitNag 160
Check ($nag -ne $null -and (Find $nag 'NagWindow.Title').Current.Name -eq 'Phase5 test') "the scheduled reminder fires at $when"
Check ((Find $nag 'NagWindow.Message').Current.Name -eq 'Check the export') "...with its message"
Invoke (Find $nag 'NagWindow.DismissButton'); Start-Sleep -Milliseconds 500
Check ((TopWindow 'NagWindow' 500) -eq $null) "Dismiss closes it"
Check ((Db lastfired 'Phase5 test') -ne 'none') "last_fired_utc is recorded"

# 6. Pause (written while the app is closed)
Quit
Db stopall
Db set 'work_days_mask=127' "work_start=$(Hm -60)" "work_end=$(Hm 60)" 'nag_not_tracking_enabled=true' 'nag_not_tracking_interval_min=1' 'checkin_enabled=false' 'reminders_paused_until_utc={now+1h}'
$app = Launch @()
Check (NoNagFor 70) "no nag for 70 s while reminders are paused"

# 7. --no-background, then the control run
Quit
Db unset 'reminders_paused_until_utc'
$app = Launch @('--no-background')
Check (NoNagFor 70) "no nag for 70 s under --no-background"
Quit
$app = Launch @()
Check ((WaitNag 45) -ne $null) "control: the same conditions do nag without the switch"

Quit
Remove-Item -Recurse -Force $data -ErrorAction SilentlyContinue
Write-Output "DONE failed=$script:failed"
