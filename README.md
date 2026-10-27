# WinFix

WinFix is one Windows program, `WinFix.exe`, that repairs common Windows problems by itself.
It checks each problem, tries the cheapest fix first, and checks again after each fix.
If something is still broken, it can reinstall or reset Windows, keeping your personal files.

Windows must still start. WinFix cannot repair a PC that does not boot.

## What it fixes

WinFix checks these problems, in this order:

| Module | Problem |
| --- | --- |
| `shell` | No taskbar, or a screen that flashes every few seconds because Explorer keeps crashing. |
| `windowsupdate` | Windows Update is broken: its service is turned off, or updates keep failing to install. |
| `bluescreen` | Blue screens: two or more crashes in the last 7 days. |
| `disk` | Disk errors or damaged system files. |
| `startapps` | The Start menu, Search or Settings will not open. |
| `profile` | Windows signed you in with a temporary profile, so your files and settings seem gone. |
| `network` | The network adapter is on, but no website can be reached. |

## How to start it as administrator

WinFix needs administrator rights. This way works even when the taskbar is gone:

1. Press **Ctrl+Shift+Esc** to open Task Manager.
2. Click **Run new task**.
3. Type the full path to `WinFix.exe`, for example `C:\Users\you\Downloads\WinFix.exe`.
4. Tick **Create this task with administrative privileges**.
5. Click **OK**.

## Options

| Option | What it does |
| --- | --- |
| *(none)* | Check every module and fix what is broken. |
| `--dry-run` | Practice run. Shows what WinFix would do and changes nothing. No administrator rights needed. |
| `--only <module>` | Check and fix one module only, for example `--only network`. |
| `--resume` | Continue after a restart. WinFix adds this itself when a fix needs a restart. |

## Reset, the last step

WinFix resets Windows only if a problem is still there after every fix.
It explains what will happen, then waits. Type `YES` and press Enter to go on.
Anything else cancels the reset and changes nothing.

Every reset keeps your personal files. WinFix tries these, in order, and stops at the first that starts:

1. **Reinstall Windows in place.** Keeps your files and your apps. WinFix looks for Windows install media
   on its own: a USB stick, a DVD, or an ISO file at the top of any drive, in Downloads or on the Desktop.
   It only uses media that matches your Windows edition, language and processor type.
   If it finds none, it downloads the matching Windows ISO from Microsoft.
2. **Reset keeping files.** Repairs Windows Recovery, then starts "Reset this PC" with "Keep my files".
   Your apps and settings are removed.
3. **Restart into Windows Recovery.** WinFix shows the steps, then restarts the PC.
   There, choose **Troubleshoot**, then **Reset this PC**, then **Keep my files**.

## Log

Every command WinFix runs, with its result, is written to:

```
%ProgramData%\WinFix\winfix.log
```

That is usually `C:\ProgramData\WinFix\winfix.log`.

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Everything is healthy or was fixed, a reset started, or the PC is restarting to finish a fix. |
| `2` | WinFix is not running as administrator, or an option is wrong (`--only` without a name, or an unknown module). |
| `3` | You did not type `YES`, so the reset was cancelled. |
| `4` | Every reset attempt failed. See the log. |

Any other code means WinFix stopped on an unexpected error. See the log.
