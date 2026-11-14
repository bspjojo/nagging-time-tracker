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
Add-Type -Namespace Win32 -Name Native -MemberDefinition '[DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);'
$data = Join-Path $env:TEMP ("ntt-manual-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$OVERLAP = [string][char]0x29C9
$CONT = [string][char]0x21AA

function Value($el) { $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Rows($win) {
    $grid = Find $win 'EntriesWindow.Grid'
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
    return ,@($grid.FindAll($TS::Children, $cond))
}
function CountText($parent, $text) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $text)
    return $parent.FindAll($TS::Descendants, $cond).Count
}
function SelectRow($row) { $row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select() }
function Child($parent, $id, [int]$timeoutMs = 5000) {
    # Owned dialogs sit under their owner window in the UIA tree.
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    do { $w = $parent.FindFirst($TS::Children, $cond); if ($w) { return $w }; Start-Sleep -Milliseconds 100 } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
    return $null
}
function MessageBoxIn($parent, $title, [int]$timeoutMs = 5000) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $title)))
    $sw = [Diagnostics.Stopwatch]::StartNew()
    do { $w = $parent.FindFirst($TS::Children, $cond); if ($w) { return $w }; Start-Sleep -Milliseconds 100 } while ($sw.ElapsedMilliseconds -lt $timeoutMs)
    return $null
}
function ClickNamed($parent, $name) {
    $cond = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
    $b = $parent.FindFirst($TS::Descendants, $cond)
    Invoke $b
}
function ComboItems($combo) {
    $p = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $p.Expand(); Start-Sleep -Milliseconds 300
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $names = @($combo.FindAll($TS::Descendants, $cond) | ForEach-Object { $_.Current.Name })
    $p.Collapse(); Start-Sleep -Milliseconds 200
    return ,$names
}
function SelectComboItem($combo, $name) {
    $p = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $p.Expand(); Start-Sleep -Milliseconds 300
    $item = $combo.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)))
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $p.Collapse(); Start-Sleep -Milliseconds 300
}
function SetDatePicker($picker, $date) {
    $box = $picker.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
    $box.SetFocus()
    $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($date.ToString('d'))
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
    Start-Sleep -Milliseconds 400
}

# 1. --show entries on a fresh database
$app = Start-Process $Exe -ArgumentList '--data-dir', $data, '--show', 'entries' -PassThru
$win = TopWindow 'EntriesWindow' 15000
Check ($win -ne $null) "--show entries opens the Entries window"
Check ((Find $win 'EntriesWindow.Summary').Current.Name -eq '0 entries · Total 0:00'.Replace('·', [string][char]0xB7)) "fresh DB: '0 entries · Total 0:00'"

# 2. New entry: blocking error keeps the dialog open; then save Acme / Research
$null = InvokeAsync (Find $win 'EntriesWindow.NewButton')
$ed = Child $win 'EntryEditor'
Check ($ed -ne $null) "New entry opens the editor"
$startTime = Value (Find $ed 'EntryEditor.StartTime')
$stopTime = Value (Find $ed 'EntryEditor.StopTime')
Log "editor defaults: $startTime - $stopTime"
SetText (Find $ed 'EntryEditor.StopTime') $startTime
$null = InvokeAsync (Find $ed 'EntryEditor.SaveButton'); Start-Sleep -Milliseconds 500
Check ((Find $ed 'EntryEditor.Error').Current.Name -eq 'Stop must be after start.') "stop = start shows the blocking error on Save"
Check ((Child $win 'EntryEditor' 500) -ne $null) "the editor stays open"
SetText (Find $ed 'EntryEditor.StopTime') $stopTime
SetText (Find $ed 'EntryEditor.CustomerCombo') 'Acme'
SetText (Find $ed 'EntryEditor.TaskTypeCombo') 'Research'
SetText (Find $ed 'EntryEditor.NotesBox') "First line`nsecond line"
$null = InvokeAsync (Find $ed 'EntryEditor.SaveButton'); Start-Sleep -Milliseconds 800
Check ((Child $win 'EntryEditor' 500) -eq $null) "valid Save closes the editor"
Check ((Rows $win).Count -eq 1) "the grid shows one entry"
Check ((CountText $win 'First line') -ge 1) "the Notes column shows only the first line"

# 3. Overlapping manual entry for Globex: reason box appears; both rows get the overlap marker
$null = InvokeAsync (Find $win 'EntriesWindow.NewButton')
$ed = Child $win 'EntryEditor'
SetText (Find $ed 'EntryEditor.CustomerCombo') 'Globex'
SetText (Find $ed 'EntryEditor.LinkBox') 'JIRA-123'
Start-Sleep -Milliseconds 300
Check (Visible (Find $ed 'EntryEditor.OverlapReasonBox' 1000)) "an overlapping time shows the overlap reason box"
Check ((Find $ed 'EntryEditor.OverlapWarning').Current.Name -like 'Overlaps 1 other entry*') "the overlap warning counts 1 entry"
Check ((Find $ed 'EntryEditor.LinkHint').Current.Name -like 'Not a URL*') "a non-URL link shows the plain-text hint"
$null = InvokeAsync (Find $ed 'EntryEditor.SaveButton'); Start-Sleep -Milliseconds 800
Check ((Rows $win).Count -eq 2) "the grid shows two entries"
Log "overlap marker count in grid: $(CountText (Find $win 'EntriesWindow.Grid') $OVERLAP)"
Check ((CountText (Find $win 'EntriesWindow.Grid') $OVERLAP) -eq 4) "both rows show the overlap marker (2 rows + the header, which UIA lists twice)"

# 4. Edit notes via Edit button
SelectRow (Rows $win)[0]
$null = InvokeAsync (Find $win 'EntriesWindow.EditButton')
$ed = Child $win 'EntryEditor'
SetText (Find $ed 'EntryEditor.NotesBox') 'Changed notes'
SetText (Find $ed 'EntryEditor.OverlapReasonBox') 'pairing'
$null = InvokeAsync (Find $ed 'EntryEditor.SaveButton'); Start-Sleep -Milliseconds 800
Check ((CountText $win 'Changed notes') -ge 1) "edited notes appear in the grid"

# 5. Continue -> running row with badge and continuation marker
SelectRow (Rows $win)[0]
$null = InvokeAsync (Find $win 'EntriesWindow.ContinueButton'); Start-Sleep -Milliseconds 800
Check ((Rows $win).Count -eq 3) "Continue adds a row"
Check ((CountText $win 'running') -eq 1) "the new row shows the running badge"
Log "continuation marker count in grid: $(CountText (Find $win 'EntriesWindow.Grid') $CONT)"
Check ((CountText (Find $win 'EntriesWindow.Grid') $CONT) -eq 3) "the new row shows the continuation marker (1 row + the header, listed twice)"

# 6. Continue another while one runs -> concurrency dialog -> Run in parallel
$rows = Rows $win
SelectRow $rows[$rows.Count - 1]
$null = InvokeAsync (Find $win 'EntriesWindow.ContinueButton')
$cd = Child $win 'ConcurrencyDialog'
Check ($cd -ne $null) "continuing while one runs asks for confirmation"
Invoke (Find $cd 'ConcurrencyDialog.RunInParallelButton'); Start-Sleep -Milliseconds 800
Check ((CountText $win 'running') -eq 2) "two running rows, refreshed without a manual reload"
Shot $win (Join-Path $Out 'entries-parallel.png')

# 7. Delete with confirmation
$before = (Rows $win).Count
SelectRow (Rows $win)[0]
$null = InvokeAsync (Find $win 'EntriesWindow.DeleteButton')
$mb = MessageBoxIn $win 'Delete entry'
Check ($mb -ne $null) "Delete asks for confirmation"
# Win32 message-box buttons appear as panes to this UIA client (no Invoke): click via BM_CLICK.
$yes = $mb.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, '6')))
$null = [Win32.Native]::SendMessage([IntPtr]$yes.Current.NativeWindowHandle, 0xF5, [IntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep -Milliseconds 800
Check ((Rows $win).Count -eq $before - 1) "the confirmed delete removes one row"

# 8. Filters: Custom with to < from shows the inline error
SelectComboItem (Find $win 'EntriesWindow.PresetCombo') 'Custom'
SetDatePicker (Find $win 'EntriesWindow.ToDate') ([DateTime]::Today.AddDays(-30))
Check ((Find $win 'EntriesWindow.FilterError').Current.Name -like "*before the 'from' date*") "Custom with to < from shows the inline error"
SelectComboItem (Find $win 'EntriesWindow.PresetCombo') 'This week'
Check ((Find $win 'EntriesWindow.FilterError').Current.Name -eq '') "choosing a preset clears the error"

# 9. Settings via the quick-panel footer (second launch opens the quick panel)
$null = Start-Process $Exe -ArgumentList '--data-dir', $data -PassThru
$panel = TopWindow 'QuickPanel' 10000
Invoke (Find $panel 'QuickPanel.SettingsLink')
$set = TopWindow 'SettingsWindow' 10000
Check ($set -ne $null) "the quick-panel footer opens Settings"
# Settings now opens on Work hours (Phase 5): select the Customers tab first.
(Find $set 'SettingsWindow.CustomersTab').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300
$list = Find $set 'SettingsWindow.CustomersList'
SelectRow ($list.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, 'Acme'))))
$null = InvokeAsync (Find $set 'SettingsWindow.CustomerRenameButton')
$rd = Child $set 'RenameDialog'
SetText (Find $rd 'RenameDialog.NameBox') 'GLOBEX'
$null = InvokeAsync (Find $rd 'RenameDialog.OkButton'); Start-Sleep -Milliseconds 500
Check ((Find $rd 'RenameDialog.Error').Current.Name -like "A customer named 'GLOBEX' already exists*") "renaming to an existing name (other case) is rejected in the dialog"
SetText (Find $rd 'RenameDialog.NameBox') 'acme corp'
$null = InvokeAsync (Find $rd 'RenameDialog.OkButton'); Start-Sleep -Milliseconds 500
Check ((Child $set 'RenameDialog' 500) -eq $null) "a valid rename closes the dialog"
Check ((CountText $list 'acme corp') -ge 1) "the list shows the new name"

# Archive 'Research': gone from the quick-panel picker, still on its entries
$null = InvokeAsync (Find $set 'SettingsWindow.TaskTypesTab'); Start-Sleep -Milliseconds 300
(Find $set 'SettingsWindow.TaskTypesTab').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300
$tlist = Find $set 'SettingsWindow.TaskTypesList'
SelectRow ($tlist.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, 'Research'))))
Invoke (Find $set 'SettingsWindow.TaskTypeArchiveButton'); Start-Sleep -Milliseconds 300
Check ((CountText $tlist 'Research (archived)') -ge 1) "archived task type is labelled '(archived)'"

# 10. Archived customer stays archived when an entry of it is edited
$list = (Find $set 'SettingsWindow.CustomersTab'); $list.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select(); Start-Sleep -Milliseconds 300
$clist = Find $set 'SettingsWindow.CustomersList'
SelectRow ($clist.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, 'acme corp'))))
Invoke (Find $set 'SettingsWindow.CustomerArchiveButton'); Start-Sleep -Milliseconds 300
SetText (Find $win 'EntriesWindow.SearchBox') 'First line'; Start-Sleep -Milliseconds 800
$rows = Rows $win
Check ($rows.Count -ge 1 -and (CountText $win 'acme corp') -ge 1) "the Entries window shows the renamed customer after a reload"
SelectRow $rows[0]
$null = InvokeAsync (Find $win 'EntriesWindow.EditButton')
$ed = Child $win 'EntryEditor'
Log "editing: $((Find $ed 'EntryEditor.CustomerCombo').Current.Name) / $(Value ((Find $ed 'EntryEditor.CustomerCombo').FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))))"
SetText (Find $ed 'EntryEditor.NotesBox') 'First line, edited'
$null = InvokeAsync (Find $ed 'EntryEditor.SaveButton'); Start-Sleep -Milliseconds 800
Check ((Child $win 'EntryEditor' 500) -eq $null) "editing an archived customer's entry saves"
$cb = Find $set 'SettingsWindow.CustomersShowArchived'
$tog = $cb.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern); $tog.Toggle(); Start-Sleep -Milliseconds 200; $tog.Toggle(); Start-Sleep -Milliseconds 300
Check ((CountText (Find $set 'SettingsWindow.CustomersList') 'acme corp (archived)') -ge 1) "the customer is still archived after the edit"
$items = ComboItems (Find $win 'EntriesWindow.CustomerFilter')
Check ($items -contains 'acme corp (archived)') "the customer filter lists archived customers with '(archived)'"
$panelItems = $null

# 11. Single Entries window
$null = Start-Process $Exe -ArgumentList '--data-dir', $data -PassThru
$panel = TopWindow 'QuickPanel' 10000
Invoke (Find $panel 'QuickPanel.AllEntriesLink'); Start-Sleep -Milliseconds 500
$null = Start-Process $Exe -ArgumentList '--data-dir', $data -PassThru
$panel = TopWindow 'QuickPanel' 10000
$tt = ComboItems (Find $panel 'QuickPanel.TaskTypeCombo')
Check (-not ($tt -contains 'Research')) "the archived task type is gone from the quick-panel picker ($($tt -join ', '))"
Invoke (Find $panel 'QuickPanel.AllEntriesLink'); Start-Sleep -Milliseconds 500
$cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'EntriesWindow')
Check ($root.FindAll($TS::Children, $cond).Count -eq 1) "opening All entries twice keeps a single Entries window"

Stop-Process -Id $app.Id -Force
Start-Sleep 1
Remove-Item -Recurse -Force $data -ErrorAction SilentlyContinue
Write-Output "DONE failed=$script:failed"
