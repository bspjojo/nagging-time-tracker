# Phase 9 — Installer: WiX v5 MSI, build script, Sandbox install checks (implementation plan)

**Status:** Shipped — 1 commit. Build clean, unit tests 373/373, UI tests 6/6; the scripted Windows Sandbox install check passes 29/29 (`DONE failed=0`); the §6.3 by-hand list (installer dialogs in Sandbox; SmartScreen and UAC per scope on the real PC) was done by the stakeholder and reported OK. This PC was left with no installed copy, Run value or program folder.

**Sequencing note:** this doc expands **Phase 9** of [plan-time-tracker-v1.md](plan-time-tracker-v1.md) (§12, "Phase 9 — Installer") into implementable detail. It builds on Phases 1–8, all shipped (latest commit `fe16a08`). The parent stays the source of truth for the installer design (§11): scope choice, folders, package contents, the build script and signing. The Run value the uninstall removes is the one Phase 7 defined (`RegistryRunKeyStore.ValueName`, [plan-phase7-polish.md](plan-phase7-polish.md) §2.1). Deviations are listed in §8 of this doc and get folded back into the parent once this doc is approved. This is the last v1 phase.

## Overview

Phase 9 ships the app: a self-contained single-file publish, a WiX v5 MSI that installs it for *just me* (no admin) or *all users*, and `tools/build-installer.ps1` to build it. The parent's by-hand install check is turned into a **scripted Windows Sandbox run**, with a short by-hand list on the real PC for what Sandbox can't show.

**What exists today (confirmed by reading the code):**

- **App identity:** `<AssemblyName>NaggingTimeTracker</AssemblyName>`, `<Version>1.0.0</Version>`, `<ApplicationIcon>Assets\app.ico</ApplicationIcon>` ([NaggingTimeTracker.App.csproj:7-12](src/NaggingTimeTracker.App/NaggingTimeTracker.App.csproj#L7-L12)). There's no `Properties/` folder and no publish profile yet.
- **Icons:** `Assets/app.ico` (16–256 px, Phase 7) and `Assets/tray/tracking-1.ico` both exist.
- **Single-file hazards:** nothing in `src/` uses `Assembly.Location`. The exe path comes from `Environment.ProcessPath` ([App.xaml.cs:142](src/NaggingTimeTracker.App/App.xaml.cs#L142), [GeneralSettingsViewModel.cs:49](src/NaggingTimeTracker.App/ViewModels/GeneralSettingsViewModel.cs#L49)), which is correct for a single-file exe.
- **Run value:** `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `NaggingTimeTracker` ([RegistryRunKeyStore.cs:9-10](src/NaggingTimeTracker.App/Startup/RegistryRunKeyStore.cs#L9-L10)). Release builds on the default data folder repoint an existing value at the running exe on every start ([App.xaml.cs:136-155](src/NaggingTimeTracker.App/App.xaml.cs#L136-L155)).
- **`.gitignore`** already ignores `artifacts/` (line 66) and `*.msi` (lines 400, 473), **but also `*.pubxml` (line 195)**, so the publish profile needs an exception to be committed.
- **Warnings are errors** for every MSBuild project under the repo (`Directory.Build.props`). That includes a `.wixproj`: the WiX SDK honours `TreatWarningsAsErrors`, so ICE warnings fail the build.
- **WiX packages:** `WixToolset.Sdk`, `WixToolset.UI.wixext` and `WixToolset.Util.wixext` **5.0.2** are the newest 5.x (nuget.org; 6.x and 7.x exist) and are already in the local NuGet cache. The parent's folder analysis (§11) was done against `wixtoolset.ui.wixext` 5.0.2.
- **Seeding helper:** `tools/ui-checks/p6db.py seed <start_min_ago> <heartbeat_min_ago>` writes one running Acme · Dev Work entry into an existing DB with Python's `sqlite3` ([p6db.py](tools/ui-checks/p6db.py)). `python` is already a prerequisite of the Phase 5–7 scripts.
- **Windows Sandbox** is available on this PC (`%windir%\System32\WindowsSandbox.exe`). There's **no `wsb` CLI** on this build, so a run is driven by a `.wsb` config: mapped folders plus a `LogonCommand`, results written to a mapped folder, and the guest shuts itself down to close the window. Sandbox runs as the built-in admin `WDAGUtilityAccount` with no UAC prompt, and mapped files have no Mark of the Web.

**Decisions already made (givens), from the parent §11:** WiX v5 SDK-style project; self-contained single-file `win-x64` payload; `Scope="perUserOrMachine"` + `WixUI_Advanced`; per-user to `%LOCALAPPDATA%\Programs\NaggingTimeTracker\`, per-machine to `%ProgramFiles%\NaggingTimeTracker\`, through the two `SetProperty` overrides; `MajorUpgrade`; a Start Menu shortcut with an HKMU key path; the optional launch checkbox; `util:CloseApplication`; removing the uninstalling user's Run value on uninstall (the mechanism changes, §2.2); user data never touched; unsigned, with a placeholder signing step.

**Stakeholder decisions for this phase:**

- **The installer shows a short as-is notice** (`installer/License.rtf`), not a repo license. WixUI_Advanced always shows a license page, and without a file WiX shows its own placeholder text. No `LICENSE` file is added to the repo.
- **Install checks run in Windows Sandbox** (§6), scripted end to end.
- **UAC and SmartScreen are checked by hand on this PC** (§6.3). Sandbox can't show either.

**Commit granularity:** one commit, "Phase 9 — installer", per the parent's one-commit-per-phase rule.

---

## 1. Publish profile: `src/NaggingTimeTracker.App/Properties/PublishProfiles/win-x64.pubxml`

```xml
<Project>
  <PropertyGroup>
    <Configuration>Release</Configuration>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <PublishDir>bin\publish\win-x64\</PublishDir>
    <DebugType>none</DebugType>
  </PropertyGroup>
</Project>
```

- `DebugType=none` keeps the `.pdb` out of the publish folder, so the folder holds just `NaggingTimeTracker.exe` (the MSI installs only that file, parent §11).
- `.gitignore`: add `!src/NaggingTimeTracker.App/Properties/PublishProfiles/win-x64.pubxml` directly after the `*.pubxml` line (195), so this one profile is tracked. The file holds no secrets.
- Not trimmed (`PublishTrimmed` stays off): WPF and EF Core don't support trimming.
- **Check:** `dotnet publish src/NaggingTimeTracker.App -c Release -p:PublishProfile=win-x64` succeeds with zero warnings (the single-file analyzer runs on publish, and warnings are errors), and `bin\publish\win-x64\` contains exactly `NaggingTimeTracker.exe`. That exe started with `--data-dir <temp> --show quick-panel` opens the quick panel. Native SQLite (`e_sqlite3`) and WPF's native DLLs load from the bundle.

---

## 2. Installer project: `installer/`

### 2.1 `installer/NaggingTimeTracker.Installer.wixproj`

```xml
<Project Sdk="WixToolset.Sdk/5.0.2">
  <PropertyGroup>
    <OutputName>NaggingTimeTracker</OutputName>
    <Platform>x64</Platform>
    <InstallerPlatform>x64</InstallerPlatform>
    <DefineConstants>ProductVersion=$(ProductVersion);PublishDir=$(PublishDir);AssetsDir=$(MSBuildThisFileDirectory)..\src\NaggingTimeTracker.App\Assets\</DefineConstants>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="WixToolset.UI.wixext" Version="5.0.2" />
    <PackageReference Include="WixToolset.Util.wixext" Version="5.0.2" />
  </ItemGroup>
</Project>
```

- **Not added to `NaggingTimeTracker.sln`.** It needs `ProductVersion` and `PublishDir` from the script, so a plain `dotnet build` of the solution must not try to build it. `tools/build-installer.ps1` (§3) is the only way it's built.
- The project fails fast if either property is missing: a `<Target Name="CheckInputs" BeforeTargets="Build">` with `<Error Condition="'$(ProductVersion)' == '' or '$(PublishDir)' == ''" Text="Build this through tools/build-installer.ps1 (ProductVersion and PublishDir are required)." />`.
- **ICE warnings:** warnings stay errors. If validation reports ICE38, ICE43, ICE57 or ICE91 (false positives for a dual-purpose package: they assume a per-user location means an HKCU key path, but the shortcut's key path is HKMU on purpose, parent §11), add only those IDs that fire to `<SuppressIces>` in this project, with an XML comment naming the reason. Any other ICE is a real defect and gets fixed in `Package.wxs`. The implementation notes record which IDs were suppressed.

### 2.2 `installer/Package.wxs`

The structure, in WiX v5 syntax (namespaces `http://wixtoolset.org/schemas/v4/wxs`, `/ui`, `/util`):

```xml
<Package Name="Nagging Time Tracker" Manufacturer="Nagging Time Tracker" Version="$(ProductVersion)"
         UpgradeCode="{GENERATED-ONCE}" Scope="perUserOrMachine" Compressed="yes">
  <MajorUpgrade DowngradeErrorMessage="A newer version is already installed." />
  <MediaTemplate EmbedCab="yes" />

  <Icon Id="AppIcon" SourceFile="$(AssetsDir)app.ico" />
  <Property Id="ARPPRODUCTICON" Value="AppIcon" />

  <Property Id="ApplicationFolderName" Value="NaggingTimeTracker" />
  <Property Id="WixAppFolder" Value="WixPerUserFolder" />
  <SetProperty Id="WixPerUserFolder" Value="[LocalAppDataFolder]Programs\[ApplicationFolderName]"
               Before="WixSetPerUserFolder" Sequence="both" />
  <SetProperty Id="WixPerMachineFolder" Value="[ProgramFiles64Folder][ApplicationFolderName]"
               Before="WixSetPerUserFolder" Sequence="both" />

  <WixVariable Id="WixUILicenseRtf" Value="License.rtf" />
  <ui:WixUI Id="WixUI_Advanced" />
  <Property Id="WIXUI_EXITDIALOGOPTIONALCHECKBOXTEXT" Value="Launch Nagging Time Tracker" />
  <Property Id="WIXUI_EXITDIALOGOPTIONALCHECKBOX" Value="1" />
  <Property Id="WixShellExecTarget" Value="[#AppExe]" />
  <CustomAction Id="LaunchApplication" BinaryRef="Wix4UtilCA_X64" DllEntry="WixShellExec" Impersonate="yes" />
  <UI>
    <Publish Dialog="ExitDialog" Control="Finish" Event="DoAction" Value="LaunchApplication"
             Condition="WIXUI_EXITDIALOGOPTIONALCHECKBOX = 1 and NOT Installed" />
  </UI>

  <!-- Uninstall only (not upgrades): delete the uninstalling user's Start with Windows value. -->
  <SetProperty Id="RemoveRunValue" Sequence="execute" Before="RemoveRunValue"
               Value="&quot;[System64Folder]reg.exe&quot; delete &quot;HKCU\Software\Microsoft\Windows\CurrentVersion\Run&quot; /v NaggingTimeTracker /f" />
  <CustomAction Id="RemoveRunValue" BinaryRef="Wix4UtilCA_X64" DllEntry="WixQuietExec"
                Execute="deferred" Impersonate="yes" Return="ignore" />
  <InstallExecuteSequence>
    <Custom Action="RemoveRunValue" After="InstallInitialize"
            Condition="REMOVE~=&quot;ALL&quot; AND NOT UPGRADINGPRODUCTCODE" />
  </InstallExecuteSequence>

  <util:CloseApplication Id="CloseApp" Target="NaggingTimeTracker.exe"
                         EndSessionMessage="yes" TerminateProcess="1" RebootPrompt="no" />

  <StandardDirectory Id="ProgramFiles64Folder">
    <Directory Id="APPLICATIONFOLDER" Name="NaggingTimeTracker">
      <Component Id="AppExeComponent">
        <File Id="AppExe" Source="$(PublishDir)NaggingTimeTracker.exe" KeyPath="yes" />
      </Component>
    </Directory>
  </StandardDirectory>

  <StandardDirectory Id="ProgramMenuFolder">
    <Component Id="StartMenuShortcutComponent">
      <Shortcut Id="StartMenuShortcut" Name="Nagging Time Tracker" Target="[#AppExe]"
                WorkingDirectory="APPLICATIONFOLDER" Icon="AppIcon" />
      <RemoveFolder Id="RemoveProgramMenuFolder" On="uninstall" />
      <RegistryValue Root="HKMU" Key="Software\NaggingTimeTracker" Name="StartMenuShortcut"
                     Type="integer" Value="1" KeyPath="yes" />
    </Component>
  </StandardDirectory>

  <Feature Id="Main">
    <ComponentRef Id="AppExeComponent" />
    <ComponentRef Id="StartMenuShortcutComponent" />
  </Feature>
</Package>
```

Notes the implementer must keep:

- **`UpgradeCode`:** generate once with `[guid]::NewGuid()` at implementation, write it in braces, upper case, and never change it. It's the identity every future MSI upgrades through.
- **The Run value is removed by a custom action, not `RemoveRegistryValue`.** Windows Installer processes the `RemoveRegistry` table when a component is *installed*, never on uninstall. The parent's `RemoveRegistryValue` (§11) would therefore wipe the user's *Start with Windows* on install and leave it behind on uninstall. Instead, `RemoveRunValue` runs `reg.exe delete … /v NaggingTimeTracker /f` through `WixQuietExec`:
  - It's **deferred and impersonated**, so `HKCU` is the uninstalling user's hive even in an elevated all-users uninstall.
  - It has **`Return="ignore"`**, because `reg.exe` exits 1 when the value doesn't exist, and that must not fail the uninstall.
  - It's **skipped during a major upgrade** (`NOT UPGRADINGPRODUCTCODE`): the exe path doesn't change, so the value stays valid.

  Deviation §8.3.
- **`ARPPRODUCTICON` and the shortcut use `app.ico`**, not `tracking-1.ico` (deviation §8.2, matching Phase 7's exe icon).
- **The exit-dialog checkbox starts ticked** (`WIXUI_EXITDIALOGOPTIONALCHECKBOX=1`; parent §11 doesn't say). The custom action only runs from the UI, so silent installs (§6) never launch the app on their own.
- **`CloseApplication` closes the app before the files are replaced.** The tray-only app has no top-level window that accepts `WM_CLOSE` (the quick panel cancels close and hides, [QuickPanelWindow.xaml.cs:71-77](src/NaggingTimeTracker.App/Windows/QuickPanelWindow.xaml.cs#L71-L77)). So it's the `EndSessionMessage` (WPF shuts down on session end) or, failing that, `TerminateProcess` that ends it. Either is safe: running timers are already in the DB (parent §4, §7.3). §6.2 checks that the process is gone after the upgrade.

### 2.3 `installer/License.rtf`

Plain RTF (`{\rtf1\ansi …}`), one heading and three short paragraphs:

1. *Nagging Time Tracker is provided "as is", without warranty of any kind. Use it at your own risk.*
2. *It keeps all of its data on this PC, in `%LOCALAPPDATA%\NaggingTimeTracker`. It makes no network connections.*
3. *Uninstalling removes the program but keeps your data. To remove that too, delete the folder above.*

WixUI_Advanced's first page (*AdvancedWelcomeEulaDlg*) shows it with an "I accept" checkbox.

---

## 3. Build script: `tools/build-installer.ps1`

Windows PowerShell 5.1 compatible (like `make-icons.ps1`), `$ErrorActionPreference = 'Stop'`, run from anywhere (paths resolved from `$PSScriptRoot\..`).

`param([string]$Version)`. With no `-Version`, it reads `<Version>` from the App csproj (`([xml](Get-Content …)).Project.PropertyGroup.Version`, the first non-empty one). With `-Version 1.0.1`, it builds that version **without editing the csproj**, which is how §6 makes the upgrade MSI.

1. `dotnet publish src/NaggingTimeTracker.App -c Release -p:PublishProfile=win-x64 -p:Version=$Version`. The `-c Release` is explicit because `dotnet publish` settles the configuration before it imports the profile, so the profile's `<Configuration>` alone isn't enough. `-p:Version` stamps the exe's file and product version, so an upgraded exe can be told apart. Fail on a non-zero exit code.
2. Check that `src/NaggingTimeTracker.App/bin/publish/win-x64/NaggingTimeTracker.exe` exists.
3. `# Signing:` a commented, no-op block: where `signtool sign /fd SHA256 …` for the exe goes later.
4. `dotnet build installer/NaggingTimeTracker.Installer.wixproj -c Release -p:ProductVersion=$Version -p:PublishDir=<absolute publish dir with trailing \>`.
5. `# Signing:` the same placeholder for the MSI.
6. Copy `installer/bin/x64/Release/NaggingTimeTracker.msi` to `artifacts/NaggingTimeTracker-$Version-x64.msi` (creating `artifacts/`), and print the full path.

The script exits non-zero if any step fails. `artifacts/` is already git-ignored (§Overview), so `.gitignore` needs no change for it.

---

## 4. App code

**No app changes.** The single-file publish needs none (§Overview: no `Assembly.Location`), and the Run value name and exit behaviour are as Phase 7 left them.

---

## 5. Docs

[README.md](README.md):

- **Status block:** "Phases 1–9 of 9 have shipped"; replace "There is no installer yet." with one line pointing at *Install* below; add the Phase 9 plan link to the list of phase plans; drop "This README gains usage and install instructions as those phases land."
- **"What v1 will do"** → **"What it does"**, with the verbs in the present tense.
- **New "Install" section** (before "Build, test, run"):
  - Run `NaggingTimeTracker-<version>-x64.msi`. **Install** installs just for you (no admin rights, to `%LOCALAPPDATA%\Programs\NaggingTimeTracker\`). **Advanced** offers *for all users* (asks for admin, installs to `C:\Program Files\NaggingTimeTracker\`).
  - Builds are unsigned: SmartScreen says *Windows protected your PC*; choose *More info → Run anyway*.
  - Upgrading: run a newer MSI of the same scope; it closes the running app first, and running timers are kept. To change scope, uninstall first (parent Non-goals).
  - Your data: `%LOCALAPPDATA%\NaggingTimeTracker\` (`tracker.db`, `backups\` with the newest 7 daily copies), per user, whichever scope you chose. Uninstall keeps it.
  - Uninstall from *Settings → Apps*. It removes your own *Start with Windows* entry; an upgrade keeps it. For an all-users install, other users' entries stay until they untick it or delete the value.
- **"Build, test, run":** add `powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-installer.ps1 [-Version x.y.z]` → `artifacts\NaggingTimeTracker-<v>-x64.msi`; and the Sandbox check command (§6.1).
- **Layout table:** rows for `installer/` and `tools/build-installer.ps1`, and for `tools/installer-checks/`.

---

## 6. Install checks

### 6.1 Host runner: `tools/installer-checks/run-sandbox.ps1`

Windows PowerShell 5.1, `param([string]$Msi, [string]$UpgradeMsi, [switch]$Interactive)`. Prints `PASS` / `FAIL` lines and `DONE failed=N` in the `tools/ui-checks` style.

1. **Preconditions:** fail if any process named `WindowsSandbox*` is running (only one Sandbox can run at a time), if either MSI is missing, or if `src/NaggingTimeTracker.App/bin/publish/win-x64/NaggingTimeTracker.exe` is missing (step 3 needs it; `build-installer.ps1` creates it).
2. Create `%TEMP%\ntt-sandbox-<8 hex>\` with `in\` and `out\`. Copy the two MSIs to `in\v1.msi` and `in\v2.msi`, and copy `guest-checks.ps1` into `in\`.
3. **Seed DB:** start `src/NaggingTimeTracker.App/bin/publish/win-x64/NaggingTimeTracker.exe --data-dir in\seed --no-background`; wait (up to 15 s) for `in\seed\tracker.db`; wait 2 s more (migrations finish); kill it; then `python tools/ui-checks/p6db.py in\seed\tracker.db seed 5 0`. That leaves one running Acme · Dev Work entry, started 5 minutes ago.
4. Write `in\..\checks.wsb`:
   ```xml
   <Configuration>
     <Networking>Disable</Networking>
     <MappedFolders>
       <MappedFolder><HostFolder>…\in</HostFolder><SandboxFolder>C:\ntt\in</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
       <MappedFolder><HostFolder>…\out</HostFolder><SandboxFolder>C:\ntt\out</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
     </MappedFolders>
     <LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\ntt\in\guest-checks.ps1</Command></LogonCommand>
   </Configuration>
   ```
   With `-Interactive` the `LogonCommand` is left out. The runner opens Sandbox with the MSIs mapped, prints "install by hand from C:\ntt\in", and returns. That's the by-hand dialog check in §6.3.
5. `Start-Process WindowsSandbox.exe checks.wsb`. Wait for `out\DONE` for up to 20 minutes (poll every 5 s). A full run takes about 10–12 minutes, mostly unpacking the 150 MB exe on each install. On timeout: `FAIL guest didn't finish`, keep the folder for inspection, and leave Sandbox running for the user to close. Killing the `WindowsSandbox*` processes orphans the VM (`vmmemWindowsSandbox`), which then blocks the next Sandbox; step 1 checks for that process too.
6. Print `out\results.txt` (the guest's `PASS`/`FAIL` lines) and count its failures.
7. **Host-side check of the upgraded DB:** `python tools/ui-checks/p6db.py out\after-upgrade\tracker.db entries` prints one row whose stop field is empty → `PASS the running timer survives the upgrade`.
8. Print `DONE failed=N` (guest + host failures). Delete the temp folder only when N is 0.

### 6.2 Guest script: `tools/installer-checks/guest-checks.ps1`

Runs inside Sandbox at logon. It writes each result to `C:\ntt\out\results.txt`, keeps verbose MSI logs in `C:\ntt\out\*.log`, writes `C:\ntt\out\DONE` last, then `Stop-Computer -Force` (which closes the Sandbox window). The whole body sits in `try/finally`, so `DONE` and the shutdown always happen; an exception is written as `FAIL error: <message>`.

Helpers: `Msi($args, $log)` runs `msiexec.exe` with `/qn /l*v C:\ntt\out\$log` through `Start-Process -Wait -PassThru` and returns the exit code (0 and 3010 count as success). `Products()` lists installed products whose name is *Nagging Time Tracker* via the `WindowsInstaller.Installer` COM object's `ProductsEx('', '', 7)` (all contexts), returning version and context (2 = per-user, 4 = per-machine).

Paths: `$userDir = "$env:LOCALAPPDATA\Programs\NaggingTimeTracker"`, `$machineDir = "$env:ProgramFiles\NaggingTimeTracker"`, `$userLnk = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Nagging Time Tracker.lnk"`, `$machineLnk = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Nagging Time Tracker.lnk"`, `$data = "$env:LOCALAPPDATA\NaggingTimeTracker"`, `$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'`.

**A. Per-user install** — `Msi('/i C:\ntt\in\v1.msi ALLUSERS=""', 'install-user.log')`:
1. exit code 0
2. `$userDir\NaggingTimeTracker.exe` exists; `$env:LOCALAPPDATA\Apps\NaggingTimeTracker` doesn't; `$machineDir` doesn't
3. `$userLnk` exists, and its target (`WScript.Shell` `CreateShortcut(...).TargetPath`) is the exe above
4. `HKCU:\Software\NaggingTimeTracker` has `StartMenuShortcut = 1`
5. `Products()` shows one entry, version `1.0.0`, context 2

**B. Launch and seed** — create `$data`, copy `C:\ntt\in\seed\tracker.db*` into it; `reg add` the Run value (`"<$userDir exe>"`, as *Start with Windows* would); `Start-Process $userLnk`; wait (up to 15 s) for a `NaggingTimeTracker` process whose `Path` is the per-user exe → PASS.

**C. Upgrade while running** — `Msi('/i C:\ntt\in\v2.msi ALLUSERS=""', 'upgrade.log')`:
1. exit code 0
2. no `NaggingTimeTracker` process within 10 s (CloseApplication closed it)
3. the exe's `VersionInfo.ProductVersion` starts with the v2 version
4. `Products()` shows one entry (the old one was removed), with the v2 version
5. the Run value `NaggingTimeTracker` is still there (an upgrade isn't an uninstall)
6. copy `$data\tracker.db*` to `C:\ntt\out\after-upgrade\` (checked on the host, §6.1 step 7)

**D. Downgrade refused** — `Msi('/i C:\ntt\in\v1.msi ALLUSERS=""', 'downgrade.log')` returns a non-zero exit code, and `Products()` still shows only v2.

**E. Uninstall** — `Msi('/x C:\ntt\in\v2.msi', 'uninstall-user.log')`:
1. exit code 0
2. `$userDir\NaggingTimeTracker.exe` and `$userLnk` are gone; `Products()` is empty
3. `$data\tracker.db` still exists
4. the Run value `NaggingTimeTracker` is gone

**F. All-users install** — `Msi('/i C:\ntt\in\v1.msi ALLUSERS=1', 'install-machine.log')`:
1. exit code 0
2. `$machineDir\NaggingTimeTracker.exe` exists, and `${env:ProgramFiles(x86)}\NaggingTimeTracker` doesn't
3. `$machineLnk` exists; `HKLM:\Software\NaggingTimeTracker` has `StartMenuShortcut = 1`
4. `Products()` shows one entry, context 4
5. `Msi('/x C:\ntt\in\v1.msi', 'uninstall-machine.log')` returns 0, and the exe, the shortcut and the product are gone

**Why `ALLUSERS=""` for the per-user steps:** WixUI_Advanced's folder actions are conditioned on `ALLUSERS` and `Privileged` (from the 5.0.2 wixlib, `WixUI_Advanced.wxs` lines 49–50). `WixSetPerUserFolder` runs when `ACTION="INSTALL" AND APPLICATIONFOLDER="" AND (ALLUSERS="" OR (ALLUSERS=2 AND (NOT Privileged)))`, and `WixSetPerMachineFolder` when `… (ALLUSERS=1 OR (ALLUSERS=2 AND Privileged))`. Sandbox's account is privileged, so the package default `ALLUSERS=2` would pick the per-machine folder. `ALLUSERS=""` forces a per-user install in the per-user folder, the same state the dialog's *just for you* choice produces. The non-admin default path (`ALLUSERS=2`, not privileged) is checked by hand on this PC (§6.3). The upgrade and downgrade must use the same `ALLUSERS=""`, because Windows Installer only finds related products in the same context (parent Non-goals). If A.2 still finds the exe outside the per-user folder, that's a `Package.wxs` defect to fix, not a check to relax.

### 6.3 By hand

**In Sandbox** (`run-sandbox.ps1 -Msi … -Interactive`), double-click `C:\ntt\in\v1.msi`:
1. The first page shows the as-is notice (§2.3); **Install** stays disabled until it's accepted.
2. **Advanced** → the *Install just for you / for all users* page; *just for you* is preselected. Back, then **Install**.
3. The finish page shows *Launch Nagging Time Tracker*, ticked; **Finish** starts the app (a tray icon appears).
4. *Settings → Apps* lists *Nagging Time Tracker* with the green icon.

**On this PC** (the stakeholder agreed to one install/uninstall per scope on the real profile; user data under `%LOCALAPPDATA%\NaggingTimeTracker\` isn't touched):
1. Copy the v1 MSI to `%TEMP%`, mark it as downloaded (`Set-Content -Path <copy> -Stream Zone.Identifier -Value "[ZoneTransfer]`nZoneId=3"`), and double-click it → SmartScreen shows *Windows protected your PC*. Choose *More info → Run anyway*.
2. **Install** (just for you) → **no UAC prompt**, and the exe lands in `%LOCALAPPDATA%\Programs\NaggingTimeTracker\` (this is the non-admin default path, `ALLUSERS=2` without privileges); then uninstall from *Settings → Apps*.
3. Run it again, **Advanced → for all users** → **a UAC prompt**; files land in `C:\Program Files\NaggingTimeTracker\`; then uninstall.
4. If *Start with Windows* was ticked from a development build before, the installed Release exe repointed it at itself on first start (self-heal, parent §8), and the uninstall then removed it. Re-tick it from the development build if wanted.

---

## 7. Tests

- **No unit or UI test changes.** The installer is checked by §6; the app code doesn't change (§4).
- Regression before commit: `dotnet build` (zero warnings), `dotnet test --filter "Category!=UI"` (373), and the UI suite once (6/6), because the build is the same.

### Implementation notes

- **ICE results.** The first MSI build reported three ICEs (warnings are errors):
  - **ICE57** (HKMU key path): suppressed, as §2.1 expected.
  - **ICE69** (the shortcut's `Target="[#AppExe]"` names a file in another component): fixed instead, with `Target="[APPLICATIONFOLDER]NaggingTimeTracker.exe"`.
  - **ICE105**: suppressed. It flags `Wix4CloseApplicationsDeferred_X64`, WixToolset.Util's own non-impersonating action behind `util:CloseApplication`, not ours (found by decompiling the MSI with `wix msi decompile`). In a non-elevated per-user install, Windows Installer runs it with the user's rights; Sandbox check C2 confirms the running app is closed.

  So `<SuppressIces>ICE57;ICE105</SuppressIces>`, each with a comment in the wixproj.
- **Referenced projects' `.pdb` files** (Core, Data) were published next to the exe despite `DebugType=none`, which applies to the App only. The profile adds `<AllowedReferenceRelatedFileExtensions>none</AllowedReferenceRelatedFileExtensions>`, so the publish folder holds only `NaggingTimeTracker.exe` (150 MB; the MSI is about 55 MB).
- **The exe's product version carries the commit hash** (`1.0.0+fe16a08…`, SourceLink). The Sandbox check compares with "starts with", so that's fine.
- **`WixQuietExec` vs `WixQuietExec64`:** the 5.0.2 util wixlib exports both from `Wix4UtilCA_X64`; `WixQuietExec` is used (the x64 binary already runs 64-bit). The decompiled MSI shows `RemoveRunValue` deferred and impersonated, and Sandbox check E4 confirms the value is removed.
- **A full Sandbox run takes about 10–12 minutes.** Each install or upgrade unpacks the 150 MB exe in about 2 minutes. The first run hit the original 10-minute limit during step F, hence 20 (§6.1).
- **Killing Sandbox's window processes orphans its VM** (`vmmemWindowsSandbox` keeps running), and the next Sandbox then fails to connect. The runner no longer kills anything on timeout, refuses to start while that VM exists, and waits for it to exit before deleting the run folder (the VM holds the mapped folders open until it's gone).
- **Error 1316 during one all-users install was self-inflicted.** Windows Installer's own text for 1316 is *"A network error occurred while attempting to read from the file"*, which the log showed as the system string *"The specified account already exists"*. The source MSI in the mapped folder had been deleted during the install. A one-off Sandbox diagnostic then ran the all-users install after four histories: fresh; per-user install and uninstall; plus an upgrade; plus a refused downgrade. All installed cleanly, and so did step F in the next full run.
- **Results:** the full Sandbox run passes 29/29 (28 guest checks plus the host-side running-timer check), `DONE failed=0`.

---

## 8. Deviations from the parent (fold into [plan-time-tracker-v1.md](plan-time-tracker-v1.md))

1. **The install check runs in Windows Sandbox** (scripted, §6.1–6.2), plus a by-hand list for the dialogs, UAC and SmartScreen (§6.3). The parent's Phase 9 checklist has one by-hand pass.
2. **`ARPPRODUCTICON` and the shortcut use `Assets/app.ico`**, not `tracking-1.ico` (§11; follows Phase 7's exe icon).
3. **The Run value is removed by an uninstall-only `reg.exe` custom action** (deferred, impersonated, `Return="ignore"`, skipped on upgrade), not `RemoveRegistryValue`, which only acts on install (§11 is wrong about this; §2.2).
4. **The installer shows `installer/License.rtf`**, a short as-is notice (stakeholder decision; §11 doesn't mention the license page).
5. **The launch checkbox starts ticked** (§11 doesn't say).
6. **The wixproj isn't in the solution** and refuses to build without `ProductVersion` / `PublishDir` (§11 doesn't say).
7. **`build-installer.ps1 -Version` builds another version without editing the csproj**, and stamps the exe with it (§11 step 2 only reads the csproj).
8. **The publish profile sets `DebugType=none`** and needs a `.gitignore` exception (`*.pubxml` is ignored) (§12 Phase 9 checklist doesn't say).
9. **A downgrade check** (§6.2 D) is added to the install check.
10. **ICE57 and ICE105 are suppressed** in the wixproj, and the shortcut targets `[APPLICATIONFOLDER]NaggingTimeTracker.exe` to satisfy ICE69 (implementation notes; §11 doesn't mention validation).

---

## Steps

- [x] [.gitignore:195](.gitignore#L195): add the `!src/NaggingTimeTracker.App/Properties/PublishProfiles/win-x64.pubxml` exception (§1)
- [x] `src/NaggingTimeTracker.App/Properties/PublishProfiles/win-x64.pubxml` (§1); publish once and run the §1 check
- [x] `installer/NaggingTimeTracker.Installer.wixproj` (§2.1), `installer/Package.wxs` with a freshly generated `UpgradeCode` (§2.2), `installer/License.rtf` (§2.3)
- [x] `tools/build-installer.ps1` (§3); build `1.0.0` and `-Version 1.0.1`; settle any ICE suppressions per §2.1 and note them
- [x] `tools/installer-checks/run-sandbox.ps1` (§6.1) and `tools/installer-checks/guest-checks.ps1` (§6.2)
- [x] Run `run-sandbox.ps1 -Msi artifacts\NaggingTimeTracker-1.0.0-x64.msi -UpgradeMsi artifacts\NaggingTimeTracker-1.0.1-x64.msi` until `DONE failed=0`
- [x] The §6.3 by-hand list (Sandbox `-Interactive`, then this PC), done by the stakeholder; record the results in the commit message
- [x] `dotnet build`, `dotnet test --filter "Category!=UI"` (373), `dotnet test tests/NaggingTimeTracker.UiTests` (6/6) (§7)
- [x] Update [README.md](README.md) (§5)
- [x] Tick the parent's Phase 9 checklist in [plan-time-tracker-v1.md](plan-time-tracker-v1.md), fold in the §8 deviations (§1 tools list, §11, §12), set its Status to Shipped (all phases), and flip this doc's Status to Shipped
- [x] Commit "Phase 9 — installer"

---

## Non-goals

- **No code signing.** Placeholder steps only (parent §11, Non-goals).
- **No MSIX, auto-update or update check** (parent Non-goals).
- **No scope switching on upgrade** (parent Non-goals); the README says to uninstall first.
- **No custom installer UI.** The stock WixUI_Advanced flow stays, so the scope choice sits behind **Advanced** (§6.3), and the license page stays (with our notice).
- **No repo `LICENSE` file** (stakeholder decision). The installer notice is installer-only.
- **No removal of user data on uninstall**, and no "also delete my data" option (parent §11).
- **No cleanup of other users' Run values** after an all-users uninstall (parent §11).
- **No installer build in CI**, and no wixproj in the solution (§2.1).
- **No ARM64 or x86 MSI.** `win-x64` only.
- **No Desktop shortcut.** Start Menu only (parent §11).
- **No automated UAC or SmartScreen checks.** Sandbox can't show them (§6.3).
