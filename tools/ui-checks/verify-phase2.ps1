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

# 9. Bad switch -> error box, exit code 2
$p = Start-Process $Exe -ArgumentList '--show', 'entries' -PassThru
$box = $null; $sw = [Diagnostics.Stopwatch]::StartNew()
while (-not $box -and $sw.ElapsedMilliseconds -lt 8000) {
    $box = $root.FindFirst($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)))
    Start-Sleep -Milliseconds 100
}
Check ($box -ne $null) "bad --show value shows an error box"
if ($box) { $box.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() }
$p.WaitForExit(5000) | Out-Null
Check ($p.ExitCode -eq 2) "bad --show value exits with code 2 (got $($p.ExitCode))"

# 1. Start with the quick panel shown
$app = Start-Process $Exe -ArgumentList '--data-dir', $data, '--show', 'quick-panel' -PassThru
$panel = TopWindow 'QuickPanel' 15000
Check ($panel -ne $null) "quick panel opens with --show quick-panel"
Check (Visible (Find $panel 'QuickPanel.NothingRunning')) "fresh DB shows 'Nothing running'"
Check (Visible (Find $panel 'QuickPanel.NoRecent')) "fresh DB shows 'No recent entries'"
$r = $panel.Current.BoundingRectangle
$wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
Log "panel at ($($r.Right),$($r.Bottom)), work area ends ($($wa.Right),$($wa.Bottom))"
Check (($wa.Right - $r.Right) -lt 40 -and ($wa.Bottom - $r.Bottom) -lt 40) "panel sits at the bottom-right of the work area"

# 2. Required validation, then start
Invoke (Find $panel 'QuickPanel.StartButton')
Start-Sleep -Milliseconds 300
Check ((Find $panel 'QuickPanel.RequiredError').Current.Name -like '*required*') "empty Start shows the Required error"
SetText (Find $panel 'QuickPanel.CustomerCombo') 'Acme'
SetText (Find $panel 'QuickPanel.TaskTypeCombo') 'Dev Work'
Invoke (Find $panel 'QuickPanel.StartButton')
$rows = Find $panel 'QuickPanel.RunningRow' -All
Check ($rows.Count -eq 1) "Start creates one running row"
$label = (Find $panel 'QuickPanel.RunningRow.Label').Current.Name
Check ($label -like 'Acme ? Dev Work') "running row label is 'Acme <dot> Dev Work'"
$e1 = (Find $panel 'QuickPanel.RunningRow.Elapsed').Current.Name; Start-Sleep -Milliseconds 2200
$e2 = (Find $panel 'QuickPanel.RunningRow.Elapsed').Current.Name
Check ($e1 -ne $e2) "elapsed time ticks ($e1 -> $e2)"

# 3. Too short -> Discard
$h = InvokeAsync (Find $panel 'QuickPanel.RunningRow.StopButton')
$dlg = TopWindow 'TooShortDialog' 5000
Check ($dlg -ne $null) "stopping within a minute offers the too-short prompt"
if (-not $dlg) {
    if ($h.IsCompleted) { Log "invoke finished" } else { Log "invoke still running" }
    $wins = $root.FindAll($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $app.Id)))
    foreach ($w in $wins) { Log "window: id='$($w.Current.AutomationId)' name='$($w.Current.Name)'" }
}
Invoke (Find $dlg 'TooShortDialog.DiscardButton')
Start-Sleep -Milliseconds 500
Check ((Find $panel 'QuickPanel.RunningRow' -All -timeoutMs 500).Count -eq 0) "Discard removes the running row"
Check (Visible (Find $panel 'QuickPanel.NoRecent')) "Discard leaves Recent empty"
$cust = (Find $panel 'QuickPanel.CustomerCombo')
Log "start form after discard: customer combo holds '$($cust.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value)'"

# 4. Parallel: start Acme, then Globex -> concurrency dialog -> Run in parallel with reason
SetText (Find $panel 'QuickPanel.CustomerCombo') 'Acme'
SetText (Find $panel 'QuickPanel.TaskTypeCombo') 'Dev Work'
Invoke (Find $panel 'QuickPanel.StartButton')
SetText (Find $panel 'QuickPanel.CustomerCombo') 'Globex'
SetText (Find $panel 'QuickPanel.TaskTypeCombo') 'Meetings'
$h = InvokeAsync (Find $panel 'QuickPanel.StartButton')
$cd = TopWindow 'ConcurrencyDialog' 5000
Check ($cd -ne $null) "starting a second timer asks for confirmation"
SetText (Find $cd 'ConcurrencyDialog.ReasonBox') 'pairing'
Invoke (Find $cd 'ConcurrencyDialog.RunInParallelButton')
Start-Sleep -Milliseconds 600
Check ((Find $panel 'QuickPanel.RunningRow' -All).Count -eq 2) "Run in parallel gives two running rows"
Check (Visible (Find $panel 'QuickPanel.ParallelBanner')) "the parallel banner is shown"
$reasons = Find $panel 'QuickPanel.RunningRow.OverlapReason' -All
Check (@($reasons | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name -eq 'pairing' }).Count -eq 1) "the overlap reason shows under the second row"
Shot $panel (Join-Path $Out 'panel-parallel.png')

# 8. Single instance: a second launch signals the first and exits
$second = Start-Process $Exe -ArgumentList '--data-dir', $data -PassThru
$second.WaitForExit(10000) | Out-Null
Check ($second.HasExited -and $second.ExitCode -eq 0) "a second launch with the same data dir exits (code $($second.ExitCode))"
Check (-not $app.HasExited) "the first instance keeps running"

# Keep only this
$keep = Find $panel 'QuickPanel.RunningRow.KeepOnlyButton' -All
Invoke $keep[0]
Start-Sleep -Milliseconds 500
Check ((Find $panel 'QuickPanel.RunningRow' -All).Count -eq 1) "Keep only this leaves one running row"
Check (-not (Visible (Find $panel 'QuickPanel.ParallelBanner' -timeoutMs 300))) "the banner disappears"

# 5. Continue from Recent while one runs -> confirmation -> Stop others & start
$recent = Find $panel 'QuickPanel.RecentRow.ContinueButton' -All
Check ($recent.Count -ge 1) "the stopped Globex entry is in Recent"
$h = InvokeAsync $recent[0]
$cd = TopWindow 'ConcurrencyDialog' 5000
Check ($cd -ne $null) "Continue while another runs asks for confirmation"
Invoke (Find $cd 'ConcurrencyDialog.StopOthersButton')
Start-Sleep -Milliseconds 600
$rows = Find $panel 'QuickPanel.RunningRow.Label' -All
Check ($rows.Count -eq 1) "Stop others & start leaves exactly one running"
Log "running after continue: $($rows[0].Current.Name)"

# 7. Restart: running timer survives (process killed, as a crash would)
Stop-Process -Id $app.Id -Force; $app.WaitForExit()
$app = Start-Process $Exe -ArgumentList '--data-dir', $data, '--show', 'quick-panel' -PassThru
$panel = TopWindow 'QuickPanel' 15000
$rows = Find $panel 'QuickPanel.RunningRow' -All
Check ($rows.Count -eq 1) "after restart the running timer is still running"
$cust = (Find $panel 'QuickPanel.CustomerCombo').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
Check ($cust -ne '') "start form is pre-filled from the latest started entry ('$cust')"

# Esc hides the panel
$panel.SetFocus()
[System.Windows.Forms.SendKeys]::SendWait('{ESC}')
Start-Sleep -Milliseconds 500
Check ((TopWindow 'QuickPanel' 500) -eq $null -or (TopWindow 'QuickPanel' 500).Current.IsOffscreen) "Esc hides the panel"

Stop-Process -Id $app.Id -Force
Remove-Item -Recurse -Force $data -ErrorAction SilentlyContinue
Write-Output "DONE failed=$script:failed"
