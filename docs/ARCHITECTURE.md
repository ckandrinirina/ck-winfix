# ARCHITECTURE — WinFix

WinFix is a single Windows x64 `.exe` that repairs the most common Windows breakdowns on its
own: a broken shell (no taskbar, a splash screen flashing every ~5 s from an `explorer.exe`
crash loop), blue screens, broken Windows Update, disk and system-file corruption, a broken
Start menu/Search/Settings, a temporary user profile, and a broken network stack. On launch it
runs every problem module's detection, repairs what is broken cheapest-first, re-checks after
each repair, and — only if a problem is still there and the user types `YES` — reinstalls or
resets Windows keeping personal files. Because the built-in "Reset this PC" errors on the
target machine, that last step is a chain that needs no outside help: an in-place reinstall
from Windows install media that WinFix finds itself (ISO on any drive, USB, DVD) or downloads
from Microsoft, then a WinRE-repaired "keep my files" reset, then a reboot into Windows
Recovery. It runs only while Windows still starts; an unbootable PC is out of scope. The user launches it from Task Manager
(Ctrl+Shift+Esc → Run new task → tick "Create this task with administrative privileges").

A companion macOS console tool, `WinFixUsb`, serves the user who only has a Mac: it writes a
bootable Windows install USB from an ISO (its own or downloaded from Microsoft) and puts
`WinFix.exe` on it, so a PC that no longer boots can be repaired or reinstalled from Windows
Setup. Both programs show the author line on launch and are published as GitHub Release assets
behind a GitHub Pages download page; the repository is public under MIT.

## Stack

- **Language:** C# on .NET 10 (`net10.0`, SDK 10.0.401 installed on the dev Mac) — verified 2026-10-27
  - Publish per-RID: `dotnet publish src/WinFix -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` — without the last flag the native runtime DLLs stay beside the exe
  - Building for Windows on macOS may need `EnableWindowsTargeting=true` — unverified
  - No trimming (`PublishTrimmed` off) — WMI/`System.Management` and reflection are not trim-safe · idioms unverified
- **Testing:** xUnit v3 — `xunit.v3` 4.0.2, `xunit.runner.visualstudio` 4.0.1, `Microsoft.NET.Test.Sdk` 18.11.0 — verified 2026-10-27
  - Package is `xunit.v3`; the plain `xunit` package is the old v2 line
- **Windows APIs:** `Microsoft.Win32.Registry` (in-box), `System.Management` for WMI — version unverified
- **macOS tool:** .NET 10 console app, RIDs `osx-arm64` + `osx-x64`, single-file self-contained — idioms unverified
  - Single-file is RID-specific: one `dotnet publish -r <rid>` per Mac architecture — verified 2026-10-27
  - Built on a `macos-latest` runner so the apphost gets its ad-hoc signature — cross-building osx from Linux is unverified
  - Shells out to `diskutil` (`list -plist external physical`, `eraseDisk FAT32 <NAME> MBR <disk>`) and `hdiutil attach -nobrowse -readonly -plist` — syntax unverified
- **wimlib:** Homebrew formula `wimlib` 1.14.5 — verified 2026-10-27
  - `wimlib-imagex split <install.wim> <install.swm> <MiB>` with 3800 MiB parts — usage unverified; Setup reading `sources\install.swm` unverified
- **CI:** GitHub Actions — `actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/upload-artifact@v7`, `actions/download-artifact@v8`, `softprops/action-gh-release@v3`, `actions/configure-pages@v6`, `actions/upload-pages-artifact@v5`, `actions/deploy-pages@v5` — verified 2026-10-27
  - Pages deploy needs `pages: write` + `id-token: write` and environment `github-pages`; a release needs `contents: write`
  - Stable link: `https://github.com/ckandrinirina/ck-winfix/releases/latest/download/<asset>` — asset names must never change

## Commands

- test: dotnet test WinFix.sln
- build: dotnet build WinFix.sln
- lint: dotnet format WinFix.sln --verify-no-changes

`dotnet test` filters by test name, not by path — no `test-one`. Valid once T-01 creates `WinFix.sln`.

## Folder structure

```
src/WinFix/
  Program.cs       entry point: parse args, run the pipeline, exit code
  app.manifest     requireAdministrator
  Pipeline/        the ordered flow: diagnose → repairs → confirm → reset chain
  Diagnose/        shell health check (crash loop, taskbar window present)
  Repairs/         one class per repair step
  Problems/        one IProblemModule per problem family (detect + its ordered repairs)
  Media/           find, download, verify and mount Windows install media
  Reset/           reset attempts and fallbacks
  System/          ISystem seam: commands, registry, processes, windows, reboot
src/WinFixUsb/
  Program.cs       entry point: args, banner, the USB-writing flow
  Disks/           list and pick an external disk, confirm the erase
  Usb/             mount ISO, format, copy, split install.wim, eject
  System/          IMacSystem seam: commands, files, prompts (fake for tests)
tests/WinFix.Tests/  mirrors src/, one *Tests.cs per class
tests/WinFixUsb.Tests/  mirrors src/WinFixUsb/
site/              GitHub Pages download page (static index.html)
.github/workflows/ release.yml (tag v* → build, attach assets) · pages.yml (deploy site/)
LICENSE            MIT
```

## Decisions

- Every OS side effect goes through one `ISystem` interface — the logic is unit-tested on macOS with a fake; only `WindowsSystem` touches the real machine
- `--dry-run` uses the fake `ISystem` and prints what would run — the demo surface and a safe rehearsal on a real PC
- Repairs escalate cheapest-first and the shell health check runs after each — stop at the first repair that makes the shell healthy
- Reset keeps personal files and never starts without the user typing `YES` — chosen at start; an accidental wipe is unrecoverable
- Reset chain: in-place reinstall from install media (`setup.exe /auto upgrade /eula accept /dynamicupdate disable`, keeps files and apps) → repair WinRE (`reagentc`, DISM) and run the keep-files reset → boot to WinRE (`reagentc /boottore` + `shutdown /r /o`) — least destructive and fully automatic first, the manual WinRE step last; replaced "WinRE repair → WinRE boot → ISO", chosen before the tool found its own media
- Install media is found without asking: setup.exe + sources\install.wim/esd on any removable, optical or fixed drive root, then *.iso in drive roots, Downloads and Desktop; only media matching the installed architecture, edition and language is used — a mismatched image cannot keep files
- No media found → download the Windows ISO from Microsoft through the software-download-connector API Fido uses — undocumented and may break, so the download sits behind `IIsoSource` and its failure falls through to the next reset attempt
- Every problem family is an `IProblemModule` (Detect → ordered repairs → re-detect); the reset chain runs only when a module is still unhealthy — one tool covers the major Windows breakdowns, each testable alone
- `WinFix.exe` still needs Windows booted; an unbootable PC is served by `WinFixUsb` writing a stock Windows install USB from a Mac — no custom WinPE; replaced "no USB build at all", chosen so a Mac-only user can recover
- `WinFixUsb` only ever erases an external physical disk, and only after the user types its identifier (e.g. `disk4`) — erasing the Mac's own disk is unrecoverable
- USB is FAT32/MBR (UEFI boots it everywhere); an `install.wim` over 4 GB is split into `.swm` parts with wimlib, and a missing `wimlib-imagex` stops the tool before anything is erased with `brew install wimlib`
- `WinFixUsb` references the `WinFix` project to reuse `MicrosoftIsoSource`/`IsoDownloader` — no shared library until a third consumer
- Author line (`WinFix <version> — by ANDRINIRINA Erick · github.com/ckandrinirina`) is the first output of both programs, every run; version comes from the assembly informational version, set from the release tag
- Releases: a `v*` tag builds `WinFix.exe` (win-x64), `WinFixUsb-macos-arm64` and `WinFixUsb-macos-x64`, unsigned; the Pages site links to `releases/latest/download/<asset>` — no signing cost, the README gives the Gatekeeper workaround
- Reset via WMI `MDM_RemoteWipe.doWipePersistUserDataMethod` (Root\CIMv2\MDM\DMMap) runs as SYSTEM through a one-shot scheduled task — community reports say admin alone is refused
- Progress survives reboots through a state file in `%ProgramData%\WinFix\` plus a `RunOnce` entry that relaunches with `--resume` — DISM/SFC and WinRE fixes may need a restart
- Self-contained single-file exe — the broken PC may not have a .NET runtime and has no working shell to install one

## Conventions

- Each problem module implements `IProblemModule { string Name; Task<Health> DetectAsync(ISystem, CancellationToken); IReadOnlyList<IRepairStep> Repairs }`
- Each repair step implements `IRepairStep { string Name; Task<StepResult> RunAsync(ISystem, CancellationToken) }`
- Every external command and its exit code is written to `%ProgramData%\WinFix\winfix.log`; the console shows a one-line status per step
- User-facing text is plain English, short sentences — the user may be reading it on a half-broken screen
