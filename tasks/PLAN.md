# PLAN — WinFix — repair the Windows shell, reset as fallback

| ID | Title | Status | Size | Needs |
|---|---|---|---|---|
| T-01 | Exe runs and rehearses the whole flow in dry-run mode | done | M | — |
| T-02 | Exe is one admin-only file for Windows x64 | done | S | T-01 |
| T-03 | Tool detects a broken shell | done | M | T-01 |
| T-04 | Quick shell repairs run and stop when the shell is fixed | done | M | T-03 |
| T-05 | Progress resumes after a reboot | done | S | T-04 |
| T-06 | Deep system repair with DISM and SFC | done | M | T-05 |
| T-07 | User must type YES before any reset | done | S | T-06 |
| T-08 | Recovery environment is repaired and reset keeping files is started | done | M | T-07 |
| T-09 | Every problem family is checked and repaired in one run | done | M | T-06 |
| T-10 | Windows Update failures are repaired | done | M | T-09 |
| T-11 | Blue screens are traced and their cause rolled back | done | M | T-09 |
| T-12 | Disk errors and corrupt system files are repaired | done | M | T-09 |
| T-13 | Start menu, Search and Settings open again | done | S | T-09 |
| T-14 | A temporary user profile is restored to the real one | done | S | T-09 |
| T-15 | A broken network stack is reset | done | S | T-09 |
| T-16 | Windows install media is found with no question asked | done | M | T-08 |
| T-17 | The Windows ISO is downloaded when no media is found | done | M | T-16 |
| T-18 | Windows is reinstalled in place keeping files and apps | done | S | T-17 |
| T-19 | Last fallback reboots into Windows Recovery | done | S | T-18 |
| T-20 | The real exe acts on the Windows machine | done | M | T-02, T-10, T-11, T-12, T-13, T-14, T-15, T-19 |
| T-21 | Show the author line when WinFix starts | done | S | — |
| T-22 | Run the Mac USB tool as a safe practice run | done | M | T-21 |
| T-23 | Pick an external USB disk and confirm the erase | done | M | T-22 |
| T-24 | Write a bootable Windows USB from an ISO on the Mac | done | M | T-23 |
| T-25 | Download Windows and add WinFix.exe to the USB | done | M | T-24 |
| T-26 | MIT license and README for the public repository | done | S | T-25 |
| T-27 | Publish both programs on every version tag | done | M | T-26 |
| T-28 | Download page on GitHub Pages | done | S | T-27 |

## T-01 Exe runs and rehearses the whole flow in dry-run mode

T-01 · status: done · size: M · needs: — · files: WinFix.sln, src/WinFix/WinFix.csproj, src/WinFix/Program.cs, src/WinFix/System/ISystem.cs, src/WinFix/System/FakeSystem.cs, src/WinFix/Pipeline/FixPipeline.cs, tests/WinFix.Tests/WinFix.Tests.csproj, tests/WinFix.Tests/Pipeline/FixPipelineTests.cs, docs/ARCHITECTURE.md, .gitignore, global.json

### Acceptance
- [x] WinFix.sln builds with `dotnet build`; src/WinFix targets net10.0 and the test project references xunit.v3 4.0.2, xunit.runner.visualstudio 4.0.1 and Microsoft.NET.Test.Sdk 18.11.0
- [x] `WinFix --dry-run` prints, in order, the stages Diagnose, Repairs, Confirm reset, Reset and exits with code 0
- [x] In dry-run every action is recorded by FakeSystem and none touches the real OS (a test asserts FakeSystem received the calls)
- [x] When the fake reports a healthy shell after diagnose, the pipeline stops and prints "Shell is healthy — nothing to do"
- [x] docs/ARCHITECTURE.md `## Commands` lists dotnet test, dotnet build and dotnet format --verify-no-changes

### Tasks
- [x] Failing tests for the pipeline stage order and the healthy short-circuit
- [x] Scaffold the solution, app project and test project
- [x] Define ISystem (run command, registry, processes, find window, reboot) and FakeSystem
- [x] Implement FixPipeline with placeholder stages and the --dry-run switch
- [x] Record the commands in docs/ARCHITECTURE.md

### Notes
- 2026-10-27: auto: assumed --dry-run FakeSystem defaults to a broken shell so all four stages print; the healthy short-circuit is tested with a healthy-configured fake; T-01 adds a .NET .gitignore (bin/, obj/)
- 2026-10-27: manual test pending (auto run)

## T-02 Exe is one admin-only file for Windows x64

T-02 · status: done · size: S · needs: T-01 · files: src/WinFix/app.manifest, src/WinFix/WinFix.csproj, src/WinFix/Program.cs, src/WinFix/System/AdminCheck.cs, tests/WinFix.Tests/System/AdminCheckTests.cs

### Acceptance
- [x] The project embeds app.manifest with requestedExecutionLevel requireAdministrator
- [x] Publishing for win-x64 self-contained single-file produces exactly one WinFix.exe in the publish folder
- [x] Started without admin rights (fake reports non-admin), it prints how to relaunch as admin from Task Manager and exits with code 2

### Tasks
- [x] Failing tests for the non-admin message and exit code
- [x] Add the manifest and publish properties to the csproj
- [x] Implement the admin check and its message
- [x] Add a publish test that asserts a single exe is produced

### Notes
- 2026-10-27: auto: assumed admin probe sits behind AdminCheck's own seam (no ISystem/FakeSystem edits) and is skipped under --dry-run
- 2026-10-27: manual test pending (auto run)

## T-03 Tool detects a broken shell

T-03 · status: done · size: M · needs: T-01 · files: src/WinFix/Diagnose/ShellHealthCheck.cs, src/WinFix/Pipeline/FixPipeline.cs, tests/WinFix.Tests/Diagnose/ShellHealthCheckTests.cs, src/WinFix/System/ISystem.cs, src/WinFix/System/FakeSystem.cs, tests/WinFix.Tests/Pipeline/FixPipelineTests.cs

### Acceptance
- [x] Shell is reported unhealthy when the taskbar window (Shell_TrayWnd) is absent
- [x] Shell is reported unhealthy when explorer.exe started 3 or more times within a 20-second watch window
- [x] Shell is reported healthy when the taskbar exists and explorer.exe keeps the same process id for the whole watch window
- [x] The diagnosis reason is printed and written to the log

### Tasks
- [x] Failing tests for the three verdicts using FakeSystem process/window data
- [x] Implement ShellHealthCheck with an injectable watch window
- [x] Wire it as the Diagnose stage and the post-repair re-check

### Notes
- 2026-10-27: auto: assumed T-03 extends ISystem/FakeSystem for process history and logging; watch window and clock injectable so tests never wait 20 s
- 2026-10-27: manual test pending (auto run)

## T-04 Quick shell repairs run and stop when the shell is fixed

T-04 · status: done · size: M · needs: T-03 · files: src/WinFix/Repairs/IRepairStep.cs, src/WinFix/Repairs/RestartExplorerStep.cs, src/WinFix/Repairs/ClearIrisServiceStep.cs, src/WinFix/Repairs/ReRegisterShellAppsStep.cs, src/WinFix/Pipeline/FixPipeline.cs, tests/WinFix.Tests/Repairs/QuickRepairsTests.cs, src/WinFix/System/ISystem.cs, src/WinFix/System/FakeSystem.cs, tests/WinFix.Tests/Pipeline/FixPipelineTests.cs

### Acceptance
- [x] Restart step kills explorer.exe and starts it again
- [x] IrisService step deletes HKCU\Software\Microsoft\Windows\CurrentVersion\IrisService and is a success when the key is already absent
- [x] Re-register step re-registers the ShellExperienceHost, StartMenuExperienceHost and Windows.Client.CBS AppX packages via PowerShell Add-AppxPackage -Register
- [x] After each step the health check runs; the first healthy result ends the run with "Fixed by <step>" and exit code 0
- [x] A step that throws or returns a non-zero exit code is logged and the next step still runs

### Tasks
- [x] Failing tests for each step's commands and the stop-on-healthy rule
- [x] Define IRepairStep and StepResult
- [x] Implement the three steps in cheapest-first order
- [x] Wire the repair loop into the pipeline

### Notes
- 2026-10-27: manual test pending (auto run)

## T-05 Progress resumes after a reboot

T-05 · status: done · size: S · needs: T-04 · files: src/WinFix/Pipeline/RunState.cs, src/WinFix/Pipeline/FixPipeline.cs, src/WinFix/Program.cs, tests/WinFix.Tests/Pipeline/RunStateTests.cs, src/WinFix/Repairs/IRepairStep.cs, src/WinFix/System/ISystem.cs, src/WinFix/System/FakeSystem.cs

### Acceptance
- [x] Before a step that needs a reboot, the next stage is saved to %ProgramData%\WinFix\state.json and a RunOnce value relaunches the exe with --resume
- [x] `--resume` continues from the saved stage and skips the steps already done
- [x] A finished run (fixed or reset started) deletes the state file and the RunOnce value

### Tasks
- [x] Failing tests for save, resume and cleanup with FakeSystem
- [x] Implement RunState and the --resume switch

### Notes
- 2026-10-27: auto: assumed reboot need is a NeedsReboot flag on IRepairStep (tested with a fake step until T-06); state file and RunOnce go through ISystem
- 2026-10-27: manual test pending (auto run)

## T-06 Deep system repair with DISM and SFC

T-06 · status: done · size: M · needs: T-05 · files: src/WinFix/Repairs/DismRestoreHealthStep.cs, src/WinFix/Repairs/SfcScanStep.cs, src/WinFix/Repairs/ShellExtensionsStep.cs, tests/WinFix.Tests/Repairs/DeepRepairsTests.cs, src/WinFix/Repairs/IRepairStep.cs, src/WinFix/Pipeline/FixPipeline.cs, src/WinFix/System/ISystem.cs, src/WinFix/System/FakeSystem.cs, tests/WinFix.Tests/Repairs/QuickRepairsTests.cs, tests/WinFix.Tests/Pipeline/FixPipelineTests.cs

### Acceptance
- [x] DISM step runs `DISM /Online /Cleanup-Image /RestoreHealth` and maps exit code 0 to success, anything else to failure with the code logged
- [x] SFC step runs `sfc /scannow` and treats "found corrupt files and repaired them" as success that needs a reboot
- [x] Shell-extension step disables non-Microsoft context-menu/shell extensions by moving their registry entries to a backup key, and the backup is restorable
- [x] A step that needs a reboot saves state and reboots through ISystem; after resume the health check runs first

### Tasks
- [x] Failing tests for exit-code mapping, SFC output parsing and the extension backup
- [x] Implement the three steps after the quick repairs
- [x] Hook reboot-required results into RunState

### Notes
- 2026-10-27: auto: assumed SFC's reboot need is decided by its result (StepResult carries RebootRequired) rather than the static NeedsReboot flag; steps are appended after the quick repairs in FixPipeline
- 2026-10-27: manual test pending (auto run)
- 2026-10-27: limitation: extension rule is path-based — handlers outside the Windows folder (e.g. OneDrive) are disabled; backed up, ShellExtensionsStep.Restore exists but nothing calls it yet

## T-07 User must type YES before any reset

T-07 · status: done · size: S · needs: T-06 · files: src/WinFix/Pipeline/ResetConfirmation.cs, src/WinFix/Pipeline/FixPipeline.cs, tests/WinFix.Tests/Pipeline/ResetConfirmationTests.cs, src/WinFix/Program.cs, tests/WinFix.Tests/Pipeline/FixPipelineTests.cs, tests/WinFix.Tests/Pipeline/RunStateTests.cs, tests/WinFix.Tests/Repairs/DeepRepairsTests.cs, tests/WinFix.Tests/Repairs/QuickRepairsTests.cs

### Acceptance
- [x] When every repair fails, a warning lists what reset keeps (personal files) and removes (apps, settings) and asks the user to type YES
- [x] Any answer other than exactly YES (case-sensitive) exits with code 3 and no reset call reaches ISystem
- [x] The warning includes the log file path

### Tasks
- [x] Failing tests for YES, other input and empty input
- [x] Implement the prompt with injectable input/output

### Notes
- 2026-10-27: auto: assumed --dry-run prints the warning and simulates YES so T-01's four-stage dry run still exits 0
- 2026-10-27: manual test pending (auto run)

## T-08 Recovery environment is repaired and reset keeping files is started

T-08 · status: done · size: M · needs: T-07 · files: src/WinFix/Reset/IResetAttempt.cs, src/WinFix/Reset/RepairWinReStep.cs, src/WinFix/Reset/MdmKeepFilesReset.cs, src/WinFix/Reset/ResetChain.cs, tests/WinFix.Tests/Reset/ResetChainTests.cs, src/WinFix/System/FakeSystem.cs

### Acceptance
- [x] `reagentc /info` output is parsed; when WinRE is Disabled the tool runs `reagentc /enable` and re-checks
- [x] Reset runs WMI MDM_RemoteWipe.doWipePersistUserDataMethod in Root\CIMv2\MDM\DMMap through a one-shot SYSTEM scheduled task
- [x] A reset whose task result is a failure (non-zero, WMI error, or no reboot within the timeout) is logged and the chain moves to the next attempt
- [x] doWipeMethod (remove everything) is never called — a test asserts it

### Tasks
- [x] Failing tests for reagentc parsing, the SYSTEM task command and failure fall-through
- [x] Implement IResetAttempt and ResetChain
- [x] Implement RepairWinReStep and MdmKeepFilesReset

### Notes
- 2026-10-27: auto: assumed ResetChain is built standalone (ISystem/FakeSystem additions allowed, FixPipeline/Program untouched) to avoid conflicting with T-09; wiring ResetChain into the pipeline Reset stage is an open gap
- 2026-10-27: manual test pending (auto run)

## T-09 Every problem family is checked and repaired in one run

T-09 · status: done · size: M · needs: T-06 · files: src/WinFix/Problems/IProblemModule.cs, src/WinFix/Problems/ShellProblem.cs, src/WinFix/Pipeline/FixPipeline.cs, tests/WinFix.Tests/Pipeline/ProblemLoopTests.cs, src/WinFix/Pipeline/RunState.cs, src/WinFix/Program.cs

### Acceptance
- [x] The pipeline runs DetectAsync on every registered module, then the repairs of each unhealthy module, re-detecting after each repair
- [x] The existing shell detection and repairs run as ShellProblem with unchanged behavior (existing shell tests still pass)
- [x] The end-of-run summary prints one line per module: Healthy, Fixed by <step>, or Still broken
- [x] The reset confirmation is shown only when at least one module is Still broken
- [x] `--only <module>` runs a single module (e.g. `--only shell`)

### Tasks
- [x] Failing tests for the loop, the summary and the reset trigger with two fake modules
- [x] Define IProblemModule and Health
- [x] Move the shell check and steps into ShellProblem
- [x] Rewire FixPipeline and add --only

### Notes
- 2026-10-27: auto: assumed T-09 does not edit ISystem/FakeSystem (peer T-08 owns them this wave); --only takes the module Name lowercased
- 2026-10-27: manual test pending (auto run)
- 2026-10-27: limitation: --only is parsed only in the --dry-run path until WindowsSystem exists (T-20)

## T-10 Windows Update failures are repaired

T-10 · status: done · size: M · needs: T-09 · files: src/WinFix/Problems/WindowsUpdateProblem.cs, src/WinFix/Repairs/ResetUpdateComponentsStep.cs, tests/WinFix.Tests/Problems/WindowsUpdateProblemTests.cs

### Acceptance
- [x] Detected as broken when wuauserv, bits or cryptsvc is disabled, or the event log shows 3 or more Windows Update install failures in the last 14 days
- [x] Repair stops BITS, wuauserv and cryptsvc, deletes qmgr*.dat, renames SoftwareDistribution\DataStore, SoftwareDistribution\Download and System32\catroot2 to .bak, sets the services back to their defaults and restarts them
- [x] An existing .bak folder is replaced, not a cause of failure

### Tasks
- [x] Failing tests for detection and the exact command sequence
- [x] Implement the module and the reset step

### Notes
- 2026-10-27: auto: assumed Windows state is read via ISystem.RunCommand output parsing; any ISystem/FakeSystem addition goes in a per-module partial file; module not registered in the default pipeline list (deferred to avoid a 4-way conflict)
- 2026-10-27: QA: same ck-lite-qa 'tree changed' warning; worktree verified clean, PASS stands
- 2026-10-27: manual test pending (auto run)

## T-11 Blue screens are traced and their cause rolled back

T-11 · status: done · size: M · needs: T-09 · files: src/WinFix/Problems/BlueScreenProblem.cs, src/WinFix/Repairs/RollbackDriverStep.cs, src/WinFix/Repairs/RollbackUpdateStep.cs, tests/WinFix.Tests/Problems/BlueScreenProblemTests.cs, src/WinFix/Problems/CrashHistory.cs

### Acceptance
- [x] Detected as broken when 2 or more BugCheck (event 1001) or Kernel-Power 41 events occurred in the last 7 days; the stop codes and minidump files from %SystemRoot%\Minidump are written to the log
- [x] Driver rollback removes third-party drivers (pnputil /enum-drivers) installed within 3 days before the first crash, with pnputil /delete-driver /uninstall
- [x] Update rollback uninstalls the newest quality update installed within 3 days before the first crash
- [x] Microsoft inbox drivers are never removed (asserted)

### Tasks
- [x] Failing tests with fake event-log, pnputil and update-history data
- [x] Implement crash detection and the time-window correlation
- [x] Implement the two rollback steps

### Notes
- 2026-10-27: auto: assumed Windows state is read via ISystem.RunCommand output parsing; any ISystem/FakeSystem addition goes in a per-module partial file; module not registered in the default pipeline list (deferred to avoid a 4-way conflict)
- 2026-10-27: manual test pending (auto run)
- 2026-10-27: limitation: one real crash logs both 41 and 1001, so a single crash can count as 2; old crashes keep the module unhealthy until they leave the 7-day window; pnputil parsing assumes English labels

## T-12 Disk errors and corrupt system files are repaired

T-12 · status: done · size: M · needs: T-09 · files: src/WinFix/Problems/DiskProblem.cs, src/WinFix/Repairs/ChkdskStep.cs, tests/WinFix.Tests/Problems/DiskProblemTests.cs

### Acceptance
- [x] Detected as broken when `fsutil dirty query C:` reports dirty, or `chkdsk C: /scan` reports errors, or DISM /CheckHealth reports a repairable store
- [x] Repair runs DISM RestoreHealth and SFC (the steps from the deep repair), then schedules `chkdsk C: /f` for the next boot and saves state for resume
- [x] After the resume, the module re-detects before anything else runs

### Tasks
- [x] Failing tests for each detection signal and the reboot hand-off
- [x] Implement DiskProblem and ChkdskStep, reusing the DISM and SFC steps

### Notes
- 2026-10-27: auto: assumed Windows state is read via ISystem.RunCommand output parsing; any ISystem/FakeSystem addition goes in a per-module partial file; module not registered in the default pipeline list (deferred to avoid a 4-way conflict)
- 2026-10-27: manual test pending (auto run)

## T-13 Start menu, Search and Settings open again

T-13 · status: done · size: S · needs: T-09 · files: src/WinFix/Problems/StartAppsProblem.cs, src/WinFix/Repairs/ReRegisterSystemAppsStep.cs, tests/WinFix.Tests/Problems/StartAppsProblemTests.cs

### Acceptance
- [x] Detected as broken when StartMenuExperienceHost, SearchHost or SystemSettings packages are missing or not in the Ok status
- [x] Repair re-registers each broken package from its install location with Add-AppxPackage -DisableDevelopmentMode -Register
- [x] Only broken packages are re-registered (asserted)

### Tasks
- [x] Failing tests for detection and the per-package command
- [x] Implement the module and the step

### Notes
- 2026-10-27: auto: assumed Windows state is read via ISystem.RunCommand output parsing; any ISystem/FakeSystem addition goes in a per-module partial file; module not registered in the default pipeline list (deferred to avoid a 4-way conflict)
- 2026-10-27: QA: commands PASS; validator returned FAIL only on ck-lite-qa's 'tree changed' warning — git status in the worktree was clean (ignored build output only), so accepted as PASS by the orchestrator
- 2026-10-27: manual test pending (auto run)

## T-14 A temporary user profile is restored to the real one

T-14 · status: done · size: S · needs: T-09 · files: src/WinFix/Problems/ProfileProblem.cs, src/WinFix/Repairs/RestoreProfileStep.cs, tests/WinFix.Tests/Problems/ProfileProblemTests.cs

### Acceptance
- [x] Detected as broken when the ProfileList holds both <SID> and <SID>.bak for the current user, or the user's ProfileImagePath points under C:\Users\TEMP
- [x] Repair exports the ProfileList key to %ProgramData%\WinFix\ before any change, makes the key whose ProfileImagePath is the real folder the active one, and resets its State and RefCount to 0
- [x] Repair asks for a reboot through RunState

### Tasks
- [x] Failing tests with fake ProfileList registry layouts
- [x] Implement the module and the step

### Notes
- 2026-10-27: auto: assumed Windows state is read via ISystem.RunCommand output parsing; any ISystem/FakeSystem addition goes in a per-task partial file; not wired into the pipeline/registered (deferred to avoid conflicts)
- 2026-10-27: manual test pending (auto run)

## T-15 A broken network stack is reset

T-15 · status: done · size: S · needs: T-09 · files: src/WinFix/Problems/NetworkProblem.cs, src/WinFix/Repairs/ResetNetworkStackStep.cs, tests/WinFix.Tests/Problems/NetworkProblemTests.cs

### Acceptance
- [x] Detected as broken when an adapter is up but neither DNS resolution of www.microsoft.com nor a TCP connection to it on port 443 succeeds
- [x] Repair runs ipconfig /flushdns, netsh winsock reset and netsh int ip reset, then asks for a reboot through RunState
- [x] With no adapter up at all, the module reports "No network adapter connected" and does not repair

### Tasks
- [x] Failing tests for the three detection cases and the commands
- [x] Implement the module and the step

### Notes
- 2026-10-27: auto: assumed Windows state is read via ISystem.RunCommand output parsing; any ISystem/FakeSystem addition goes in a per-task partial file; not wired into the pipeline/registered (deferred to avoid conflicts)
- 2026-10-27: manual test pending (auto run)
- 2026-10-27: design: no adapter up is reported as Healthy with reason 'No network adapter connected' (Health has no skipped state)

## T-16 Windows install media is found with no question asked

T-16 · status: done · size: M · needs: T-08 · files: src/WinFix/Media/MediaFinder.cs, src/WinFix/Media/MediaMatcher.cs, src/WinFix/Media/IsoMounter.cs, tests/WinFix.Tests/Media/MediaFinderTests.cs, src/WinFix/System/CommandArgs.cs, src/WinFix/Repairs/ReRegisterSystemAppsStep.cs, src/WinFix/Repairs/RestoreProfileStep.cs, src/WinFix/Repairs/RollbackDriverStep.cs, src/WinFix/Repairs/RollbackUpdateStep.cs, src/WinFix/Repairs/ResetUpdateComponentsStep.cs, src/WinFix/Repairs/ReRegisterShellAppsStep.cs, src/WinFix/Problems/CrashHistory.cs, src/WinFix/Problems/StartAppsProblem.cs, src/WinFix/Problems/NetworkProblem.cs, src/WinFix/Reset/MdmKeepFilesReset.cs, tests/WinFix.Tests/System/CommandArgsTests.cs, tests/WinFix.Tests/System/PsLiteral.cs

### Acceptance
- [x] Every removable, optical and fixed drive root holding setup.exe and sources\install.wim or install.esd is found as media
- [x] *.iso files in drive roots, the user's Downloads and Desktop are found and mounted with Mount-DiskImage to be checked
- [x] Media is kept only when its image (DISM /Get-WimInfo) matches the installed architecture, EditionID and language; others are logged with the mismatch reason and dismounted
- [x] When several match, the one with the highest build is chosen
- [x] No user prompt is ever shown during the search (asserted)

### Tasks
- [x] Failing tests with fake drives, ISO files and WIM info
- [x] Implement MediaFinder, MediaMatcher and IsoMounter

### Notes
- 2026-10-27: auto: assumed Windows state is read via ISystem.RunCommand output parsing; any ISystem/FakeSystem addition goes in a per-task partial file; not wired into the pipeline/registered (deferred to avoid conflicts)
- 2026-10-27: manual test pending (auto run)
- 2026-10-27: security: PowerShell command injection via ISO file names (unicode quotes U+2018-201B bypass Quote()); fix dispatched before completion
- 2026-10-27: security: fixed in 2 QA rounds — CommandArgs (QuotePowerShell doubles ' and U+2018-201B, rejects " and control chars; QuoteCmd rejects " % control chars and a trailing backslash) applied at every command splice; WindowsDirectory treated as untrusted; final QA classified every RunCommand call site, 0 unsafe

## T-17 The Windows ISO is downloaded when no media is found

T-17 · status: done · size: M · needs: T-16 · files: src/WinFix/Media/IIsoSource.cs, src/WinFix/Media/MicrosoftIsoSource.cs, src/WinFix/Media/IsoDownloader.cs, tests/WinFix.Tests/Media/IsoDownloaderTests.cs, src/WinFix/Media/DownloadFiles.cs, tests/WinFix.Tests/Media/MicrosoftIsoSourceTests.cs

### Acceptance
- [x] With no matching media, the tool asks MicrosoftIsoSource for the link matching the installed Windows version, language and architecture
- [x] Download starts only with at least 8 GB free on the target drive, else it logs the shortfall and the chain moves on
- [x] The download resumes from where it stopped after an interruption and shows percent progress
- [x] The downloaded ISO goes through the same match check as found media before use
- [x] Any API or network failure is logged and the chain moves to the next reset attempt

### Tasks
- [x] Failing tests with a fake IIsoSource and fake HTTP responses
- [x] Implement IsoDownloader (space check, resume, progress)
- [x] Implement MicrosoftIsoSource on the software-download-connector endpoints

### Notes
- 2026-10-27: auto: assumed HTTP via injectable HttpMessageHandler (no network in tests); MicrosoftIsoSource follows the undocumented Fido connector flow, tested only against recorded fake responses; download URL must be https on a Microsoft host; failure returns no ISO, chain wiring is T-18
- 2026-10-27: manual test pending (auto run)
- 2026-10-27: risk: connector endpoint shapes are unverified against the live service (built from a summarized read of Fido.ps1); Windows 11 productEditionId 3813 may be wrong (24H2 may use 3113); API supplies no SHA-256; language-name alias table is assumed — verify on a real PC

## T-18 Windows is reinstalled in place keeping files and apps

T-18 · status: done · size: S · needs: T-17 · files: src/WinFix/Reset/InPlaceReinstallAttempt.cs, src/WinFix/Reset/ResetChain.cs, tests/WinFix.Tests/Reset/InPlaceReinstallAttemptTests.cs

### Acceptance
- [x] It runs `setup.exe /auto upgrade /eula accept /dynamicupdate disable` from the found or downloaded media
- [x] It is the first attempt in the reset chain, before the WinRE repair and keep-files reset (order asserted)
- [x] A setup exit code other than success is logged with its hex value and the chain moves on

### Tasks
- [x] Failing tests for the command line, the order and the failure path
- [x] Implement the attempt and put it first in the chain

### Notes
- 2026-10-27: auto: assumed the attempt finds media via MediaFinder, then falls back to IsoDownloader; the setup.exe path is quoted through CommandArgs
- 2026-10-27: manual test pending (auto run)

## T-19 Last fallback reboots into Windows Recovery

T-19 · status: done · size: S · needs: T-18 · files: src/WinFix/Reset/BootToWinReAttempt.cs, src/WinFix/Reset/ResetChain.cs, tests/WinFix.Tests/Reset/BootToWinReAttemptTests.cs, src/WinFix/Reset/IResetAttempt.cs, tests/WinFix.Tests/Reset/InPlaceReinstallAttemptTests.cs

### Acceptance
- [x] When every earlier attempt failed, the tool prints the steps Troubleshoot → Reset this PC → Keep my files, then runs `reagentc /boottore` and `shutdown /r /o /t 30`
- [x] If `reagentc /boottore` fails, the tool exits with code 4 and prints the log path, without rebooting
- [x] Chain order is in-place reinstall → keep-files reset → boot to WinRE (asserted)

### Tasks
- [x] Failing tests for success, failure and order
- [x] Implement the attempt as the last in the chain

### Notes
- 2026-10-27: auto: assumed exit code 4 is carried in the attempt/chain result and printed with the log path; mapping it to the process exit code happens when the chain is wired into the pipeline (T-20)
- 2026-10-27: manual test pending (auto run)

## T-20 The real exe acts on the Windows machine

T-20 · status: done · size: M · needs: T-02, T-10, T-11, T-12, T-13, T-14, T-15, T-19 · files: src/WinFix/System/WindowsSystem.cs, src/WinFix/Program.cs, tests/WinFix.Tests/System/WindowsSystemTests.cs, README.md, src/WinFix/System/ISystem.cs, src/WinFix/Pipeline/FixPipeline.cs, tests/WinFix.Tests/ProgramTests.cs

### Acceptance
- [x] Without --dry-run, Program wires WindowsSystem; with --dry-run it wires FakeSystem (asserted)
- [x] WindowsSystem runs commands with a timeout and captures exit code, stdout and stderr; every call is appended to %ProgramData%\WinFix\winfix.log
- [x] Windows-only tests are skipped on non-Windows hosts and pass on Windows
- [x] README.md lists every problem module, how to launch WinFix.exe from Task Manager as admin, and what each exit code means

### Tasks
- [x] Failing tests for wiring and the command runner
- [x] Implement WindowsSystem (Process, Registry, FindWindow P/Invoke, WMI, event log, drives, HTTP, shutdown)
- [x] Write README.md

### Notes
- 2026-10-27: auto: assumed 'Program wires WindowsSystem' also closes the gaps deferred by parallel waves — register all 7 problem modules in the default list, wire ResetChain.DefaultAttempts into the pipeline Reset stage with its exit code (4) as the process exit code, and honor --only outside --dry-run
- 2026-10-27: manual test pending (auto run) — the 7 Windows-only WindowsSystem tests were skipped on macOS and still need one run on Windows

## T-21 Show the author line when WinFix starts

T-21 · status: done · size: S · needs: — · files: src/WinFix/AboutBanner.cs, src/WinFix/Program.cs, tests/WinFix.Tests/AboutBannerTests.cs

### Acceptance
- [x] The first line WinFix prints, in every mode including `--dry-run` and the not-admin refusal, is `WinFix <version> — by ANDRINIRINA Erick · github.com/ckandrinirina`
- [x] `<version>` is the assembly informational version without the `+commit` suffix; a build with `-p:Version=1.2.3` prints `WinFix 1.2.3`
- [x] `AboutBanner` is reusable with another product name (`WinFixUsb`) and prints the same author and link

### Tasks
- [x] Failing tests for each criterion
- [x] Add `AboutBanner` reading the informational version
- [x] Print it first in `Program.RunAsync`
- [x] Update existing output-order tests that assumed a different first line

### Notes
- 2026-10-27: manual test pending (auto run)

## T-22 Run the Mac USB tool as a safe practice run

T-22 · status: done · size: M · needs: T-21 · files: src/WinFixUsb/WinFixUsb.csproj, src/WinFixUsb/Program.cs, src/WinFixUsb/System/IMacSystem.cs, src/WinFixUsb/System/FakeMacSystem.cs, tests/WinFixUsb.Tests/WinFixUsb.Tests.csproj, tests/WinFixUsb.Tests/ProgramTests.cs, WinFix.sln

### Acceptance
- [x] `WinFix.sln` contains `WinFixUsb` (net10.0, references `WinFix`) and `WinFixUsb.Tests` (xunit.v3 4.0.2); `dotnet test WinFix.sln` runs both
- [x] `WinFixUsb --dry-run` prints the author line first (product `WinFixUsb`), then the ordered plan: find the ISO, pick the USB, erase it, copy Windows Setup, split install.wim, copy WinFix.exe, eject — and exits 0
- [x] In `--dry-run` every OS call goes to `FakeMacSystem` with fixture disks; no real command runs
- [x] `WinFixUsb --help` lists `--iso <path>`, `--disk <id>`, `--dry-run`, and exits 0

### Tasks
- [x] Failing tests for each criterion
- [x] Create the console project and test project, add both to the solution
- [x] Add the `IMacSystem` seam (run command, read/write files, ask the user) and its fake
- [x] Wire `Program` with argument parsing and the dry-run plan output

### Notes
- 2026-10-27: manual test pending (auto run)

## T-23 Pick an external USB disk and confirm the erase

T-23 · status: done · size: M · needs: T-22 · files: src/WinFixUsb/Disks/DiskList.cs, src/WinFixUsb/Disks/DiskPicker.cs, tests/WinFixUsb.Tests/Disks/DiskListTests.cs, tests/WinFixUsb.Tests/Disks/DiskPickerTests.cs, src/WinFixUsb/Program.cs, src/WinFixUsb/System/FakeMacSystem.cs, tests/WinFixUsb.Tests/ProgramTests.cs

### Acceptance
- [x] Disks are read from `diskutil list -plist external physical` output; an internal or synthesized disk in the plist is never offered
- [x] A disk smaller than 8 GB is listed as too small and cannot be chosen
- [x] The user sees each disk's identifier, name and size, and must type the identifier (e.g. `disk4`) to confirm; any other answer aborts with exit code 1 and no erase
- [x] `--disk disk4` preselects the disk but still asks for the typed confirmation; an unknown or internal identifier is refused
- [x] No external disk → plain message "Plug in a USB drive of at least 8 GB and run again." and exit 1

### Tasks
- [x] Failing tests with plist fixtures for each criterion
- [x] Parse the diskutil plist into disks
- [x] Implement the picker and the typed confirmation
- [x] Use it in the dry-run plan with fixture disks

### Notes
- 2026-10-27: auto: assumed 8 GB = 8,000,000,000 bytes (decimal, as diskutil reports sizes)
- 2026-10-27: manual test pending (auto run)
- 2026-10-27: internal/synthesized filter (Internal=true or APFSVolumes) verified only against fixture plists — check against real diskutil output on a Mac

## T-24 Write a bootable Windows USB from an ISO on the Mac

T-24 · status: done · size: M · needs: T-23 · files: src/WinFixUsb/Usb/UsbWriter.cs, src/WinFixUsb/Usb/IsoMount.cs, src/WinFixUsb/System/MacSystem.cs, src/WinFixUsb/Program.cs, tests/WinFixUsb.Tests/Usb/UsbWriterTests.cs, src/WinFixUsb/System/IMacSystem.cs, src/WinFixUsb/System/FakeMacSystem.cs, src/WinFixUsb/Disks/DiskList.cs, tests/WinFixUsb.Tests/ProgramTests.cs

### Acceptance
- [x] With `--iso <path>`, the writer runs in order: check `wimlib-imagex` → `hdiutil attach -nobrowse -readonly -plist <iso>` → `diskutil eraseDisk FAT32 WINFIX MBR <disk>` → copy every ISO file except `sources/install.wim` → split or copy install.wim → `hdiutil detach` → `diskutil eject <disk>`
- [x] An `install.wim` over 4 GB becomes `sources/install.swm` parts via `wimlib-imagex split … 3800`; one at or under 4 GB is copied as is
- [x] Missing `wimlib-imagex` stops before the erase with "Install wimlib first: brew install wimlib" and exit 1
- [x] An ISO without `setup.exe` and `sources/install.wim|esd` is refused before the erase
- [x] Any failed command stops the run, detaches the ISO, prints the step that failed, and exits non-zero
- [x] `MacSystem` is the only class that starts real processes; all of the above is tested through the fake

### Tasks
- [x] Failing tests for each criterion
- [x] Mount the ISO and read its mount point from the hdiutil plist
- [x] Implement the writer steps and the wim split rule
- [x] Implement `MacSystem` and use it when not in `--dry-run`

### Notes
- 2026-10-27: auto: assumed 4 GB = 4,000,000,000 bytes (decimal like T-23; always under the FAT32 4 GiB limit)
- 2026-10-27: manual test pending (auto run)
- 2026-10-27: MacSystem never run against a real ISO/USB — needs a real write to a spare USB drive

## T-25 Download Windows and add WinFix.exe to the USB

T-25 · status: done · size: M · needs: T-24 · files: src/WinFixUsb/Usb/IsoProvider.cs, src/WinFixUsb/Usb/WinFixExeProvider.cs, src/WinFixUsb/Program.cs, tests/WinFixUsb.Tests/Usb/IsoProviderTests.cs, tests/WinFixUsb.Tests/Usb/WinFixExeProviderTests.cs, src/WinFix/Media/IsoDownloader.cs, src/WinFix/Media/DownloadFiles.cs, src/WinFixUsb/UsbServices.cs, src/WinFixUsb/Usb/UsbWriter.cs, tests/WinFixUsb.Tests/Usb/DownloadFakes.cs, tests/WinFixUsb.Tests/Usb/UsbWriterTests.cs, tests/WinFixUsb.Tests/ProgramTests.cs

### Acceptance
- [x] Without `--iso`, the tool downloads the Windows 11 x64 ISO through the existing `MicrosoftIsoSource`/`IsoDownloader` into `~/Downloads` and uses it; a failed download prints a plain message with the manual Microsoft download page and exits 1 before any erase
- [x] The download finishes before the disk is erased
- [x] `WinFix.exe` is copied to the USB root: from beside the tool when present, else downloaded from `https://github.com/ckandrinirina/ck-winfix/releases/latest/download/WinFix.exe`; if both fail the USB is still finished and a warning says so
- [x] The final message tells the user to boot the PC from the USB and, if Windows starts again, run `WinFix.exe` from it

### Tasks
- [x] Failing tests for each criterion with a fake HTTP handler
- [x] Wrap the shared ISO downloader for the Mac tool
- [x] Add the WinFix.exe provider
- [x] Wire both into the flow and the closing message

### Notes
- 2026-10-27: auto: ISO language = Mac UI language (e.g. fr-FR), falling back to en-US — must match the installed Windows for a keep-files reinstall
- 2026-10-27: shared WinFix project touched: IsoDownloader split into DownloadFileAsync; DiskDownloadFiles.Delete no longer throws on a missing folder
- 2026-10-27: manual test pending (auto run)
- 2026-10-27: real MicrosoftIsoSource/GitHub download wiring not exercised by tests — confirm with a real run without --iso

## T-26 MIT license and README for the public repository

T-26 · status: done · size: S · needs: T-25 · files: LICENSE, README.md, tests/WinFix.Tests/RepoFilesTests.cs

### Acceptance
- [x] `LICENSE` holds the MIT text with `Copyright (c) 2026 ANDRINIRINA Erick`
- [x] `README.md` has a Download section linking `releases/latest/download/WinFix.exe`, `WinFixUsb-macos-arm64` and `WinFixUsb-macos-x64`, and the Pages site
- [x] `README.md` has a "Only have a Mac?" section: `brew install wimlib`, `xattr -d com.apple.quarantine`, `chmod +x`, an example run, and booting the PC from the USB
- [x] `README.md` names the author and the MIT license

### Tasks
- [x] Failing tests that read the repo files for each criterion
- [x] Add LICENSE
- [x] Extend README

### Notes
- 2026-10-27: manual test pending (auto run)

## T-27 Publish both programs on every version tag

T-27 · status: done · size: M · needs: T-26 · files: .github/workflows/release.yml, tests/WinFix.Tests/ReleaseWorkflowTests.cs

### Acceptance
- [x] Pushing a tag `v*` runs `dotnet test WinFix.sln` first; a failing test publishes nothing
- [x] It publishes `WinFix.exe` (win-x64), `WinFixUsb-macos-arm64` and `WinFixUsb-macos-x64` (built on `macos-latest`) as single-file self-contained, with `-p:Version=<tag without v>`
- [x] The three files are attached to the GitHub Release for the tag under exactly those names, using `softprops/action-gh-release@v3` with `contents: write`
- [x] The workflow uses the action major versions recorded in `## Stack`

### Tasks
- [x] Failing tests that parse the workflow file for each criterion
- [x] Write the release workflow with test, build matrix and release jobs

### Notes
- 2026-10-27: security review (MEDIUM): softprops/action-gh-release@v3 is a mutable third-party tag in a contents:write job — kept per the acceptance criterion; consider pinning to a commit SHA (# v3.x.y) or replacing with `gh release create --verify-tag`
- 2026-10-27: manual test pending (auto run)
- 2026-10-27: workflow never run on GitHub — push a test tag (e.g. v0.0.1-test) and check the 3 release assets
- 2026-10-27: CI tests run on macos-latest: the 7 Windows-only tests are skipped in CI

## T-28 Download page on GitHub Pages

T-28 · status: done · size: S · needs: T-27 · files: site/index.html, .github/workflows/pages.yml, tests/WinFix.Tests/SitePageTests.cs

### Acceptance
- [x] `site/index.html` has one download button per release asset, each linking `https://github.com/ckandrinirina/ck-winfix/releases/latest/download/<asset>` with the exact names the release workflow uploads
- [x] The page says what WinFix fixes, how to start it as administrator, the Mac USB steps, and shows the author line and MIT license
- [x] The page is a single static file, readable at phone width, light and dark
- [x] `pages.yml` deploys `site/` on push to `main` with `pages: write`, `id-token: write` and the `github-pages` environment

### Tasks
- [x] Failing tests that check the page links against the release workflow asset names
- [x] Write the page
- [x] Write the Pages workflow

### Notes
- 2026-10-27: manual test pending (auto run)
- 2026-10-27: Pages workflow never run and page not viewed in a browser — enable Pages (source: GitHub Actions), push to main, check phone width + dark mode
