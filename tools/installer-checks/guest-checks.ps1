<#
  Runs inside Windows Sandbox at logon (started by run-sandbox.ps1; plan-phase9-installer.md §6.2).
  C:\ntt\in  (read-only): v1.msi, v2.msi, versions.txt ("<v1> <v2>"), seed\tracker.db*
  C:\ntt\out (writable):  results.txt, *.log, after-upgrade\, then DONE. Then it shuts Sandbox down.
#>
$ErrorActionPreference = 'Stop'
$in = 'C:\ntt\in'
$out = 'C:\ntt\out'
$results = Join-Path $out 'results.txt'

function Check($cond, $msg) {
    $line = if ($cond) { "PASS $msg" } else { "FAIL $msg" }
    Add-Content -Path $results -Value $line -Encoding UTF8
}
function Note($msg) { Add-Content -Path $results -Value "  $msg" -Encoding UTF8 }

function Msi([string]$arguments, [string]$log) {
    $p = Start-Process msiexec.exe -ArgumentList "$arguments /qn /l*v `"$out\$log`"" -Wait -PassThru
    return $p.ExitCode
}
function Ok($code) { $code -eq 0 -or $code -eq 3010 }

# Installed copies of this product: version and AssignmentType (0 = per-user, 1 = per-machine).
function Products() {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $found = @()
    foreach ($p in $installer.ProductsEx('', '', 7)) {
        if ($p.InstallProperty('ProductName') -eq 'Nagging Time Tracker') {
            $found += [pscustomobject]@{
                Version = $p.InstallProperty('VersionString')
                Assignment = [int]$p.InstallProperty('AssignmentType')
            }
        }
    }
    return , $found
}
function AppProcesses($exe) {
    @(Get-Process NaggingTimeTracker -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
}
function WaitFor([scriptblock]$cond, [int]$seconds) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $seconds) {
        if (& $cond) { return $true }
        Start-Sleep -Milliseconds 500
    }
    return [bool](& $cond)
}
function ShortcutTarget($lnk) { (New-Object -ComObject WScript.Shell).CreateShortcut($lnk).TargetPath }

try {
    $v1, $v2 = (Get-Content (Join-Path $in 'versions.txt') -Raw).Trim() -split '\s+'
    Note "versions: v1=$v1 v2=$v2"

    $userDir = Join-Path $env:LOCALAPPDATA 'Programs\NaggingTimeTracker'
    $userExe = Join-Path $userDir 'NaggingTimeTracker.exe'
    $machineDir = Join-Path $env:ProgramFiles 'NaggingTimeTracker'
    $machineExe = Join-Path $machineDir 'NaggingTimeTracker.exe'
    $userLnk = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Nagging Time Tracker.lnk'
    $machineLnk = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Nagging Time Tracker.lnk'
    $data = Join-Path $env:LOCALAPPDATA 'NaggingTimeTracker'
    $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

    # A. Per-user install. ALLUSERS="" because Sandbox's account is privileged (§6.2).
    $code = Msi "/i `"$in\v1.msi`" ALLUSERS=`"`"" 'install-user.log'
    Check (Ok $code) "A1 per-user install succeeds (exit $code)"
    Check (Test-Path $userExe) "A2 the exe is in $userDir"
    Check (-not (Test-Path (Join-Path $env:LOCALAPPDATA 'Apps\NaggingTimeTracker'))) "A2 nothing under %LOCALAPPDATA%\Apps"
    Check (-not (Test-Path $machineDir)) "A2 nothing under Program Files"
    Check ((Test-Path $userLnk) -and (ShortcutTarget $userLnk) -eq $userExe) "A3 the per-user Start Menu shortcut points at the exe"
    $reg = Get-ItemProperty 'HKCU:\Software\NaggingTimeTracker' -ErrorAction SilentlyContinue
    Check ($reg -and $reg.StartMenuShortcut -eq 1) "A4 HKCU\Software\NaggingTimeTracker StartMenuShortcut = 1"
    $p = Products
    Check ($p.Count -eq 1 -and $p[0].Version -eq $v1 -and $p[0].Assignment -eq 0) "A5 one per-user product, version $v1 ($(($p | ForEach-Object { "$($_.Version)/$($_.Assignment)" }) -join ', '))"

    # B. Seed data, a Start with Windows value, and launch from the shortcut.
    New-Item -ItemType Directory -Force $data | Out-Null
    Copy-Item (Join-Path $in 'seed\tracker.db*') $data
    Set-ItemProperty -Path $run -Name NaggingTimeTracker -Value "`"$userExe`""
    Start-Process $userLnk
    Check (WaitFor { (AppProcesses $userExe).Count -gt 0 } 15) "B the shortcut starts the installed exe"

    # C. Upgrade while it runs.
    $code = Msi "/i `"$in\v2.msi`" ALLUSERS=`"`"" 'upgrade.log'
    Check (Ok $code) "C1 upgrade succeeds (exit $code)"
    Check (WaitFor { (AppProcesses $userExe).Count -eq 0 } 10) "C2 the running app was closed"
    $ver = (Get-Item $userExe -ErrorAction SilentlyContinue).VersionInfo.ProductVersion
    Check ($ver -like "$v2*") "C3 the exe is now $v2 ($ver)"
    $p = Products
    Check ($p.Count -eq 1 -and $p[0].Version -eq $v2) "C4 one product, version $v2 ($(($p | ForEach-Object { $_.Version }) -join ', '))"
    Check ($null -ne (Get-ItemProperty $run -Name NaggingTimeTracker -ErrorAction SilentlyContinue)) "C5 the Run value survives the upgrade"
    $after = Join-Path $out 'after-upgrade'
    New-Item -ItemType Directory -Force $after | Out-Null
    Copy-Item (Join-Path $data 'tracker.db*') $after

    # D. Downgrade refused.
    $code = Msi "/i `"$in\v1.msi`" ALLUSERS=`"`"" 'downgrade.log'
    Check (-not (Ok $code)) "D1 installing $v1 over $v2 is refused (exit $code)"
    $p = Products
    Check ($p.Count -eq 1 -and $p[0].Version -eq $v2) "D2 still only $v2 installed"

    # E. Uninstall.
    $code = Msi "/x `"$in\v2.msi`"" 'uninstall-user.log'
    Check (Ok $code) "E1 per-user uninstall succeeds (exit $code)"
    Check (-not (Test-Path $userExe) -and -not (Test-Path $userLnk)) "E2 the exe and shortcut are gone"
    Check ((Products).Count -eq 0) "E2 no product left"
    Check (Test-Path (Join-Path $data 'tracker.db')) "E3 the data folder is kept"
    Check ($null -eq (Get-ItemProperty $run -Name NaggingTimeTracker -ErrorAction SilentlyContinue)) "E4 the Run value is removed"

    # F. All-users install and uninstall.
    $code = Msi "/i `"$in\v1.msi`" ALLUSERS=1" 'install-machine.log'
    Check (Ok $code) "F1 all-users install succeeds (exit $code)"
    Check (Test-Path $machineExe) "F2 the exe is in $machineDir"
    Check (-not (Test-Path (Join-Path ${env:ProgramFiles(x86)} 'NaggingTimeTracker'))) "F2 nothing under Program Files (x86)"
    Check ((Test-Path $machineLnk) -and (ShortcutTarget $machineLnk) -eq $machineExe) "F3 the all-users Start Menu shortcut points at the exe"
    $reg = Get-ItemProperty 'HKLM:\Software\NaggingTimeTracker' -ErrorAction SilentlyContinue
    Check ($reg -and $reg.StartMenuShortcut -eq 1) "F3 HKLM\Software\NaggingTimeTracker StartMenuShortcut = 1"
    $p = Products
    Check ($p.Count -eq 1 -and $p[0].Assignment -eq 1) "F4 one per-machine product"
    $code = Msi "/x `"$in\v1.msi`"" 'uninstall-machine.log'
    Check (Ok $code) "F5 all-users uninstall succeeds (exit $code)"
    Check (-not (Test-Path $machineExe) -and -not (Test-Path $machineLnk) -and (Products).Count -eq 0) "F5 the exe, shortcut and product are gone"
}
catch {
    Check $false "error: $($_.Exception.Message) (line $($_.InvocationInfo.ScriptLineNumber))"
}
finally {
    Set-Content -Path (Join-Path $out 'DONE') -Value 'done'
    Stop-Computer -Force
}
