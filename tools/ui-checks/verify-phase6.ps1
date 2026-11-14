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
$dbHelper = Join-Path $PSScriptRoot 'p6db.py'
$db = Join-Path $data 'tracker.db'
Add-Type -Namespace Win32 -Name Input -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
[StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }   // MOUSEINPUT is the largest union member
[StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
[DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO p);
public static double IdleSeconds() {
    var i = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
    GetLastInputInfo(ref i);
    return unchecked((uint)Environment.TickCount - i.dwTime) / 1000.0;
}
[DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
public static uint Nudge() {
    var i = new INPUT { type = 0, mi = new MOUSEINPUT { dx = 1, dy = 0, dwFlags = 1 } };
    return SendInput(1, new[] { i }, Marshal.SizeOf(typeof(INPUT)));
}
'@

function Value($el) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Db { param([Parameter(ValueFromRemainingArguments = $true)] $rest) & python $dbHelper $db @rest }
function SelectTab($win, $id) {
    (Find $win $id).GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300
}
function Commit($win, $id, $text) {
    $box = Find $win $id
    $box.SetFocus()
    $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
    (Find $win 'SettingsWindow.Tabs').SetFocus()
    Start-Sleep -Milliseconds 300
}
function Launch([string[]]$extra) { Start-Process $Exe -ArgumentList (@('--data-dir', $data) + $extra) -PassThru }
function Quit { Get-Process NaggingTimeTracker -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force; Start-Sleep 1 }
function CloseWin($w) { $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep -Milliseconds 500 }
function Rows { return ,@(Db entries | ForEach-Object { $f = $_ -split '\|'; [pscustomobject]@{ Id = $f[0]; Start = $f[1]; Stop = $f[2]; From = $f[3] } }) }

# 1. Idle tab (also creates the scratch DB)
$app = Launch @('--show', 'settings', '--no-background')
$set = TopWindow 'SettingsWindow' 15000
SelectTab $set 'SettingsWindow.IdleTab'
Check ((Value (Find $set 'SettingsWindow.IdleThreshold')) -eq '10') "the Idle tab shows the default threshold 10"
Commit $set 'SettingsWindow.IdleThreshold' '0'
Check ((Find $set 'SettingsWindow.IdleError').Current.Name -eq 'Threshold must be 1–1440 minutes.') "threshold 0 shows the inline error"
CloseWin $set
Quit
$app = Launch @('--show', 'settings', '--no-background')
$set = TopWindow 'SettingsWindow' 15000
SelectTab $set 'SettingsWindow.IdleTab'
Check ((Value (Find $set 'SettingsWindow.IdleThreshold')) -eq '10') "...and 0 wasn't saved"
Commit $set 'SettingsWindow.IdleThreshold' '1'
CloseWin $set
Quit
Check ((Db get 'idle_threshold_min') -eq '1') "threshold 1 is saved"

# 2. Startup downtime → Discard & stop
$beat = Db seed 60 30
$app = Launch @()
$p = TopWindow 'IdlePrompt' 15000
Check ($p -ne $null) "a stale heartbeat with a running timer shows the prompt at startup"
$msg = (Find $p 'IdlePrompt.Message').Current.Name
Log "message: $msg"
Check ($msg -like "The app wasn't running from * to * (30m) while tracking Acme · Dev Work.") "...worded 'The app wasn't running', customer first"
$null = InvokeAsync (Find $p 'IdlePrompt.DiscardStopButton'); Start-Sleep -Milliseconds 800
Check ((TopWindow 'IdlePrompt' 500) -eq $null) "Discard & stop closes it"
Quit
$r = Rows
Check ($r.Count -eq 1 -and $r[0].Stop -eq $beat) "...and the entry stopped at the heartbeat"

# 3a. Discard & continue
$beat = Db seed 60 30
$app = Launch @()
$p = TopWindow 'IdlePrompt' 15000
$null = InvokeAsync (Find $p 'IdlePrompt.DiscardContinueButton'); Start-Sleep -Milliseconds 800
Quit
$r = Rows
Check ($r.Count -eq 2 -and $r[0].Stop -eq $beat -and $r[1].Stop -eq '' -and $r[1].From -eq $r[0].Id) "Discard & continue: old stops at the heartbeat, new running row continues it"

# 3b. Keep
$null = Db seed 60 30
$before = Rows
$app = Launch @()
$p = TopWindow 'IdlePrompt' 15000
$null = InvokeAsync (Find $p 'IdlePrompt.KeepButton'); Start-Sleep -Milliseconds 800
Check ((TopWindow 'IdlePrompt' 500) -eq $null) "Keep it closes it"
Quit
$after = Rows
Check ($after.Count -eq 1 -and $after[0].Stop -eq '' -and $after[0].Start -eq $before[0].Start) "...and changes nothing"

# 4. Short downtime: no prompt (threshold is 1 min now)
$null = Db seed 60 0.5
$app = Launch @()
Check ((TopWindow 'IdlePrompt' 10000) -eq $null) "a 30-s downtime doesn't prompt"
Quit

# 5. --no-background
$null = Db seed 60 30
$app = Launch @('--no-background')
Check ((TopWindow 'IdlePrompt' 10000) -eq $null) "no startup prompt under --no-background"
Quit

# 6. Live idle: a fresh running timer, no input for 85 s, then a mouse nudge
$null = Db seed 1 0
$app = Launch @()
Check ((TopWindow 'IdlePrompt' 8000) -eq $null) "no startup prompt with a fresh heartbeat"
[System.Windows.Forms.SendKeys]::SendWait('{SCROLLLOCK}{SCROLLLOCK}')   # a known last-input moment
Start-Sleep -Seconds 85
Check ((TopWindow 'IdlePrompt' 500) -eq $null) "no prompt while still away"
Log ("idle before nudge: {0:N0} s" -f [Win32.Input]::IdleSeconds())
$sent = [Win32.Input]::Nudge()
Start-Sleep -Milliseconds 300
Log ("SendInput sent {0}; idle after nudge: {1:N1} s" -f $sent, [Win32.Input]::IdleSeconds())
Check ($sent -eq 1) "the script's return nudge was accepted by SendInput"
$p = TopWindow 'IdlePrompt' 25000
Check ($p -ne $null) "returning after the threshold shows the idle prompt"
if ($p) {
    $msg = (Find $p 'IdlePrompt.Message').Current.Name
    Log "message: $msg"
    Check ($msg -like 'You were away from * while tracking Acme · Dev Work.') "...worded 'You were away'"
    $null = InvokeAsync (Find $p 'IdlePrompt.KeepButton'); Start-Sleep -Milliseconds 800
    Check ((TopWindow 'IdlePrompt' 500) -eq $null) "Keep it closes it"
}
Quit
$r = Rows
Check ($r.Count -eq 1 -and $r[0].Stop -eq '') "...and the timer is still running"

Remove-Item -Recurse -Force $data -ErrorAction SilentlyContinue
Write-Output "DONE failed=$script:failed"
