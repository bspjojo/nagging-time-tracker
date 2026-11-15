<#
  Install checks for the MSI in Windows Sandbox (plan-phase9-installer.md §6.1).

    powershell -NoProfile -ExecutionPolicy Bypass -File tools\installer-checks\run-sandbox.ps1 `
      -Msi artifacts\NaggingTimeTracker-1.0.0-x64.msi -UpgradeMsi artifacts\NaggingTimeTracker-1.0.1-x64.msi

  Seeds a database with a running timer, opens Sandbox, and guest-checks.ps1 installs, upgrades,
  downgrades and uninstalls (per-user and all users) on its own, then closes Sandbox. Prints PASS /
  FAIL lines and DONE failed=N. Takes about 10-15 minutes; don't close the Sandbox window.

  -Interactive opens Sandbox with the MSIs in C:\ntt\in and runs nothing, for the by-hand checks.

  Needs: tools\build-installer.ps1 run first (for the published exe), python on PATH, and no
  other Sandbox open.
#>
param([Parameter(Mandatory)][string]$Msi, [string]$UpgradeMsi, [switch]$Interactive)
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$publishedExe = Join-Path $root 'src\NaggingTimeTracker.App\bin\publish\win-x64\NaggingTimeTracker.exe'
$dbTool = Join-Path $root 'tools\ui-checks\p6db.py'
$script:failed = 0
function Check($cond, $msg) { if ($cond) { Write-Output "PASS $msg" } else { Write-Output "FAIL $msg"; $script:failed++ } }

function MsiVersion($path) {
    if ((Split-Path -Leaf $path) -match '^NaggingTimeTracker-(.+)-x64\.msi$') { return $Matches[1] }
    throw "Can't read the version from '$path'; expected NaggingTimeTracker-<version>-x64.msi from build-installer.ps1"
}

# 1. Preconditions.
# The VM can outlive the window (vmmemWindowsSandbox), and a second Sandbox can't start while it runs.
if (Get-Process -Name 'WindowsSandbox*', 'vmmemWindowsSandbox' -ErrorAction SilentlyContinue) {
    throw 'Windows Sandbox is still running (its window, or its VM shutting down); close it or wait a minute.'
}
$Msi = (Resolve-Path $Msi).Path
if (-not $Interactive) {
    if (-not $UpgradeMsi) { throw '-UpgradeMsi is required unless -Interactive.' }
    $UpgradeMsi = (Resolve-Path $UpgradeMsi).Path
    if (-not (Test-Path $publishedExe)) { throw "Missing $publishedExe; run tools\build-installer.ps1 first." }
}

# 2. Folders.
$work = Join-Path $env:TEMP ('ntt-sandbox-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$in = Join-Path $work 'in'
$out = Join-Path $work 'out'
New-Item -ItemType Directory -Force $in, $out | Out-Null
Copy-Item $Msi (Join-Path $in 'v1.msi')
if ($UpgradeMsi) { Copy-Item $UpgradeMsi (Join-Path $in 'v2.msi') }

$logon = ''
if (-not $Interactive) {
    Copy-Item (Join-Path $PSScriptRoot 'guest-checks.ps1') $in
    Set-Content -Path (Join-Path $in 'versions.txt') -Value "$(MsiVersion $Msi) $(MsiVersion $UpgradeMsi)"

    # 3. Seed DB: let the app create and migrate it, then add one running Acme entry.
    $seed = Join-Path $in 'seed'
    $app = Start-Process $publishedExe -ArgumentList '--data-dir', "`"$seed`"", '--no-background' -PassThru
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path (Join-Path $seed 'tracker.db')) -and $sw.Elapsed.TotalSeconds -lt 15) { Start-Sleep -Milliseconds 300 }
    Start-Sleep -Seconds 2
    Stop-Process -Id $app.Id -Force
    $app.WaitForExit()
    if (-not (Test-Path (Join-Path $seed 'tracker.db'))) { throw "The app didn't create $seed\tracker.db" }
    & python $dbTool (Join-Path $seed 'tracker.db') seed 5 0 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Seeding the database failed' }

    $logon = '<LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\ntt\in\guest-checks.ps1</Command></LogonCommand>'
}

# 4. Sandbox config.
$wsb = Join-Path $work 'checks.wsb'
Set-Content -Path $wsb -Encoding UTF8 -Value @"
<Configuration>
  <Networking>Disable</Networking>
  <MappedFolders>
    <MappedFolder><HostFolder>$in</HostFolder><SandboxFolder>C:\ntt\in</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$out</HostFolder><SandboxFolder>C:\ntt\out</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  $logon
</Configuration>
"@

Start-Process (Join-Path $env:windir 'System32\WindowsSandbox.exe') -ArgumentList "`"$wsb`""
if ($Interactive) {
    Write-Output "Sandbox is opening. Install by hand from C:\ntt\in (v1.msi$(if ($UpgradeMsi) { ', v2.msi' }))."
    Write-Output "Host folder: $work (delete it when you're done)."
    return
}

# 5. Wait for the guest.
$done = Join-Path $out 'DONE'
$sw = [Diagnostics.Stopwatch]::StartNew()
while (-not (Test-Path $done) -and $sw.Elapsed.TotalMinutes -lt 20) { Start-Sleep -Seconds 5 }
if (-not (Test-Path $done)) {
    # Don't kill the Sandbox processes: that orphans its VM, which then blocks the next Sandbox.
    Check $false "the guest didn't finish within 20 minutes; close the Sandbox window yourself"
}

# 6. The guest's results.
$results = Join-Path $out 'results.txt'
if (Test-Path $results) {
    foreach ($line in Get-Content $results -Encoding UTF8) {
        Write-Output $line
        if ($line -like 'FAIL *') { $script:failed++ }
    }
} else {
    Check $false 'the guest wrote no results'
}

# 7. The running timer survived the upgrade (read on the host; Sandbox has no SQLite tool).
$afterDb = Join-Path $out 'after-upgrade\tracker.db'
if (Test-Path $afterDb) {
    $rows = @(& python $dbTool $afterDb entries)
    Check ($rows.Count -eq 1 -and ($rows[0] -split '\|')[2] -eq '') "the running timer survives the upgrade ($($rows -join '; '))"
} else {
    Check $false 'no database was copied out after the upgrade'
}

# 8. Done.
Write-Output "DONE failed=$($script:failed)"
if ($script:failed -eq 0) {
    # The VM keeps the mapped folders open until it has fully shut down.
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ((Get-Process -Name 'vmmemWindowsSandbox' -ErrorAction SilentlyContinue) -and $sw.Elapsed.TotalSeconds -lt 120) { Start-Sleep -Seconds 3 }
    try { Remove-Item -Recurse -Force $work -ErrorAction Stop }
    catch { Write-Output "Couldn't delete $work yet (Sandbox still has it open); delete it later." }
} else {
    Write-Output "Kept for inspection: $work"
}
exit $script:failed
