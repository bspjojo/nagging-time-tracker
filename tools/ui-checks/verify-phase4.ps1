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
$outDir = Join-Path $env:TEMP ("ntt-export-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $outDir | Out-Null
$outFile = Join-Path $outDir 'out.csv'
Add-Type -Namespace Win32 -Name Native -MemberDefinition '[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);'

function Value($el) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Child($parent, $id, [int]$timeoutMs = 5000) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    do { $w = $parent.FindFirst($TS::Children, $cond); if ($w) { return $w }; Start-Sleep -Milliseconds 100 } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
    return $null
}
function Rows($win) {
    $grid = Find $win 'EntriesWindow.Grid'
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
    return ,@($grid.FindAll($TS::Children, $cond))
}
function SelectRow($row) { $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
function SelectComboItem($combo, $name) {
    $p = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $p.Expand(); Start-Sleep -Milliseconds 300
    $item = $combo.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $p.Collapse(); Start-Sleep -Milliseconds 400
}
function ComboSelection($combo) {
    $sel = $combo.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
    if ($sel.Length -gt 0) { return $sel[0].Current.Name } else { return $null }
}
function SaveAs($owner, $path, [switch]$ExpectOverwrite) {
    # The common Save dialog is driven by keyboard (its controls aren't reliably exposed to this UIA client).
    $null = InvokeAsync (Find $owner 'ExportWindow.ExportButton')
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
    $dlg = $null; $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not $dlg -and $sw.ElapsedMilliseconds -lt 8000) { $dlg = $owner.FindFirst($TS::Children, $cond); Start-Sleep -Milliseconds 200 }
    if (-not $dlg) { return $null }
    $name = $dlg.Current.Name
    $null = [Win32.Native]::SetForegroundWindow([IntPtr]$dlg.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 600
    [System.Windows.Forms.SendKeys]::SendWait('^a')
    [System.Windows.Forms.SendKeys]::SendWait($path)
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Milliseconds 1200
    if ($ExpectOverwrite) {
        [System.Windows.Forms.SendKeys]::SendWait('%y')
        Start-Sleep -Milliseconds 1200
    }
    return $name
}
function NewEntry($win, $customer, $notes) {
    $null = InvokeAsync (Find $win 'EntriesWindow.NewButton')
    $ed = Child $win 'EntryEditor'
    SetText (Find $ed 'EntryEditor.CustomerCombo') $customer
    SetText (Find $ed 'EntryEditor.TaskTypeCombo') 'Dev Work'
    SetText (Find $ed 'EntryEditor.NotesBox') $notes
    $null = InvokeAsync (Find $ed 'EntryEditor.SaveButton'); Start-Sleep -Milliseconds 800
}

# 1. --show export on a fresh DB
$app = Start-Process $Exe -ArgumentList '--data-dir', $data, '--show', 'export' -PassThru
$ex = TopWindow 'ExportWindow' 15000
Check ($ex -ne $null) "--show export opens the Export window"
Check ((Find $ex 'ExportWindow.Preview').Current.Name -eq 'No entries match these filters.') "fresh DB: 'No entries match these filters.'"
Check (-not (Find $ex 'ExportWindow.ExportButton').Current.IsEnabled) "Export is disabled when nothing matches"
$ex.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close(); Start-Sleep -Milliseconds 500

# 2. Seed: two stopped entries, one running continuation (all via the Entries window)
$quoted = "a, `"b`"`nline2"
$second = Start-Process $Exe -ArgumentList '--data-dir', $data -PassThru
$panel = TopWindow 'QuickPanel' 10000
Invoke (Find $panel 'QuickPanel.AllEntriesLink')
$win = TopWindow 'EntriesWindow' 10000
NewEntry $win 'Acme' '=SUM(A1)'
NewEntry $win 'Globex' $quoted
$globex = (Rows $win) | Where-Object { $_.Current.Name -like '*Globex*' } | Select-Object -First 1
SelectRow $globex
$null = InvokeAsync (Find $win 'EntriesWindow.ContinueButton'); Start-Sleep -Milliseconds 800
Check ((Rows $win).Count -eq 3) "seeded 2 stopped entries and 1 running"

# 3. Export from Entries with the customer filter on Acme -> pre-filled
SelectComboItem (Find $win 'EntriesWindow.CustomerFilter') 'Acme'
Invoke (Find $win 'EntriesWindow.ExportButton')
$ex = TopWindow 'ExportWindow' 10000
Check ((ComboSelection (Find $ex 'ExportWindow.CustomerFilter')) -eq 'Acme') "Export opened from Entries carries the customer filter"
Check ((ComboSelection (Find $ex 'ExportWindow.PresetCombo')) -eq 'This week') "...and the date preset"
Check ((Find $ex 'ExportWindow.Preview').Current.Name -eq '1 entry will be exported.') "preview counts only Acme's stopped entry"

# 4. All customers -> 2 (+1 running excluded); include running -> 3
SelectComboItem (Find $ex 'ExportWindow.CustomerFilter') 'All customers'
$p = (Find $ex 'ExportWindow.Preview').Current.Name
Check ($p -eq '2 entries will be exported (1 running not included).') "preview: '$p'"
(Find $ex 'ExportWindow.IncludeRunning').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Toggle(); Start-Sleep -Milliseconds 400
Check ((Find $ex 'ExportWindow.Preview').Current.Name -eq '3 entries will be exported.') "including running entries counts 3"

# 5. Export via the save dialog
$dlgName = SaveAs $ex $outFile
Log "save dialog: '$dlgName'"
Check ($dlgName -ne $null) "Export opens the save dialog"
$result = (Find $ex 'ExportWindow.Result' 3000).Current.Name
Check ($result -eq 'Exported 3 entries to out.csv.') "result: '$result'"
Check (Visible (Find $ex 'ExportWindow.OpenFileButton')) "Open file / Open folder are shown"

# 6. File contents
$bytes = [IO.File]::ReadAllBytes($outFile)
Check ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) "the file starts with the UTF-8 BOM"
$firstLine = ([IO.File]::ReadAllLines($outFile))[0]
Check ($firstLine -eq 'entry_id,date,start,stop,duration_hours,duration_minutes,customer,task_type,external_link,notes,continued_from_id,overlap_reason') "the header matches"
$records = @(Import-Csv -Path $outFile -Encoding UTF8)
Check ($records.Count -eq 3) "3 records"
Check (@($records | Where-Object { $_.stop -eq '' }).Count -eq 1) "the running record has a blank stop"
Check (@($records | Where-Object { $_.notes -eq "'=SUM(A1)" }).Count -eq 1) "the formula-like note is escaped as '=SUM(A1)"
Check (@($records | Where-Object { $_.notes.Replace("`r`n", "`n") -eq $quoted }).Count -eq 1) "the quoted multi-line note round-trips"
Check (@($records | Where-Object { $_.continued_from_id -ne '' }).Count -eq 1) "the continuation carries continued_from_id"

# 7. Overwrite
$before = (Get-Item $outFile).LastWriteTimeUtc
Start-Sleep -Milliseconds 1100
$null = SaveAs $ex $outFile -ExpectOverwrite
Check ((Get-Item $outFile).LastWriteTimeUtc -gt $before) "exporting to the same path replaces the file after the overwrite prompt"
Check (-not (Test-Path "$outFile.tmp")) "no .tmp file is left behind"

# 8. Locked file -> inline error, window stays, no .tmp
$lock = [IO.File]::Open($outFile, 'Open', 'Read', 'None')
try {
    $null = SaveAs $ex $outFile -ExpectOverwrite
    $err = (Find $ex 'ExportWindow.Error').Current.Name
    Check ($err -like "Couldn't write the file:*") "a locked file shows the inline error ('$($err.Substring(0, [Math]::Min(60, $err.Length)))…')"
    Check ((TopWindow 'ExportWindow' 500) -ne $null) "the Export window stays open"
    Check (-not (Test-Path "$outFile.tmp")) "no .tmp file is left after the failure"
}
finally { $lock.Dispose() }

# 9. Pre-fill re-applies to the open Export window
SelectComboItem (Find $win 'EntriesWindow.PresetCombo') 'Today'
Invoke (Find $win 'EntriesWindow.ExportButton'); Start-Sleep -Milliseconds 500
$cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'ExportWindow')
Check ($root.FindAll($TS::Children, $cond).Count -eq 1) "still a single Export window"
Check ((ComboSelection (Find $ex 'ExportWindow.PresetCombo')) -eq 'Today') "the open Export window now shows 'Today'"

# 10. Quick-panel footer Export link activates the same window
$null = Start-Process $Exe -ArgumentList '--data-dir', $data -PassThru
$panel = TopWindow 'QuickPanel' 10000
Invoke (Find $panel 'QuickPanel.ExportLink'); Start-Sleep -Milliseconds 600
Check ($root.FindAll($TS::Children, $cond).Count -eq 1) "the footer Export link keeps a single Export window"

Shot $ex (Join-Path $Out 'export-window.png')
Get-Process NaggingTimeTracker -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $Exe } | Stop-Process -Force
Start-Sleep 1
Remove-Item -Recurse -Force $data, $outDir -ErrorAction SilentlyContinue
Write-Output "DONE failed=$script:failed"
