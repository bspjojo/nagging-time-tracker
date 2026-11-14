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
$backups = Join-Path $data 'backups'
$today = (Get-Date).ToString('yyyy-MM-dd')
$todayFile = Join-Path $backups "tracker-$today.db"

function Db { param([Parameter(ValueFromRemainingArguments = $true)] $rest) & python $dbHelper $db @rest }
function Value($el) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Launch([string[]]$extra) { Start-Process $Exe -ArgumentList (@('--data-dir', $data) + $extra) -PassThru }
function Quit { Get-Process NaggingTimeTracker -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force; Start-Sleep 1 }
function WaitFile($path, [int]$seconds) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $seconds) { if (Test-Path $path) { return $true }; Start-Sleep -Milliseconds 200 }
    return $false
}
function BackupNames { @(Get-ChildItem $backups -Filter 'tracker-*.db' | Where-Object { $_.Name -match '^tracker-\d{4}-\d{2}-\d{2}\.db$' } | Sort-Object Name | ForEach-Object Name) }

# 1. A backup on first start, and only one a day
$app = Launch @('--no-background')
Check (WaitFile $todayFile 15) "the first start writes backups\tracker-$today.db"
Start-Sleep 2
Quit
$stamp = (Get-Item $todayFile).LastWriteTimeUtc
Check ((Db get 'last_backup_date') -eq $today) "...and records last_backup_date"
$app = Launch @('--no-background')
Start-Sleep 6
Quit
$names = BackupNames
Check ($names.Count -eq 1) "a second start the same day adds no backup ($($names -join ', '))"
Check ((Get-Item $todayFile).LastWriteTimeUtc -eq $stamp) "...and doesn't rewrite today's"
Check (@(Get-ChildItem $backups -Filter '*.tmp').Count -eq 0) "no .tmp file is left"

# 2. Retention: 8 old dummies + today's -> the newest 7
Db unset 'last_backup_date'
foreach ($d in 1..8) { Set-Content -Path (Join-Path $backups ("tracker-2026-01-0{0}.db" -f $d)) -Value 'old' }
Set-Content -Path (Join-Path $backups 'tracker-copy.db') -Value 'mine'
$app = Launch @('--no-background')
$sw = [Diagnostics.Stopwatch]::StartNew()
while ($sw.Elapsed.TotalSeconds -lt 15 -and (Get-Item $todayFile).LastWriteTimeUtc -eq $stamp) { Start-Sleep -Milliseconds 200 }
Start-Sleep 1
Quit
$names = BackupNames
Log "kept: $($names -join ', ')"
$expected = @('tracker-2026-01-03.db', 'tracker-2026-01-04.db', 'tracker-2026-01-05.db', 'tracker-2026-01-06.db', 'tracker-2026-01-07.db', 'tracker-2026-01-08.db', "tracker-$today.db")
Check (($names -join ',') -eq ($expected -join ',')) "retention keeps today's and the 6 newest older backups"
Check (Test-Path (Join-Path $backups 'tracker-copy.db')) "...and leaves other files alone"

# 3. General tab
$app = Launch @('--show', 'settings', '--no-background')
$set = TopWindow 'SettingsWindow' 15000
Check ((Find $set 'SettingsWindow.GeneralTab').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) "--show settings opens on General"
Check ((Value (Find $set 'SettingsWindow.DataFolder')) -eq $data) "the data folder shown is the --data-dir folder"
$sww = Find $set 'SettingsWindow.StartWithWindows'
Check (-not $sww.Current.IsEnabled) "Start with Windows is disabled under --data-dir"
Check (Visible (Find $set 'SettingsWindow.StartWithWindowsHint' 1000)) "...with the default-folder hint"
Check ((Find $set 'SettingsWindow.OpenDataFolderButton') -ne $null) "the Open data folder button is there"
Quit

Remove-Item -Recurse -Force $data -ErrorAction SilentlyContinue
Write-Output "DONE failed=$script:failed"
