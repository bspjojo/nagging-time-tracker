<#
  Publishes the app (self-contained, single file) and builds the MSI into artifacts\.
  plan-time-tracker-v1.md §11, plan-phase9-installer.md §3.

    powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-installer.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-installer.ps1 -Version 1.0.1

  With no -Version, the version is the App csproj's <Version>. -Version builds another version
  (e.g. to test an upgrade) without editing the csproj, and stamps the exe with it too.
#>
param([string]$Version)
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$appProject = Join-Path $root 'src\NaggingTimeTracker.App\NaggingTimeTracker.App.csproj'
$installerProject = Join-Path $root 'installer\NaggingTimeTracker.Installer.wixproj'
$publishDir = Join-Path $root 'src\NaggingTimeTracker.App\bin\publish\win-x64\'

function Invoke-Dotnet([string[]]$arguments) {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($arguments[0]) failed (exit code $LASTEXITCODE)" }
}

if (-not $Version) {
    $Version = ([xml](Get-Content -Raw $appProject)).Project.PropertyGroup |
        ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { throw "No <Version> in $appProject" }
}
Write-Output "Building Nagging Time Tracker $Version"

# 1. Publish. A clean folder, so only this build's exe goes into the MSI.
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
Invoke-Dotnet @('publish', $appProject, '-c', 'Release', '-p:PublishProfile=win-x64', "-p:Version=$Version")

# 2. The payload.
$exe = Join-Path $publishDir 'NaggingTimeTracker.exe'
if (-not (Test-Path $exe)) { throw "Publish didn't produce $exe" }

# 3. Signing: not yet. Sign the exe here, e.g.
#    signtool sign /fd SHA256 /tr <timestamp url> /td SHA256 /f <cert> "$exe"

# 4. The MSI.
Invoke-Dotnet @('build', $installerProject, '-c', 'Release', "-p:ProductVersion=$Version", "-p:PublishDir=$publishDir")
$msi = Join-Path $root 'installer\bin\x64\Release\NaggingTimeTracker.msi'
if (-not (Test-Path $msi)) { throw "The installer build didn't produce $msi" }

# 5. Signing: not yet. Sign the MSI here, the same way as the exe.

# 6. Into artifacts\ (git-ignored).
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force $artifacts | Out-Null
$target = Join-Path $artifacts "NaggingTimeTracker-$Version-x64.msi"
Copy-Item -Force $msi $target
Write-Output "MSI: $target"
