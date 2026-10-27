using WinFix.System;
using WinFixUsb.Disks;
using WinFixUsb.System;

namespace WinFixUsb.Usb;

public static class UsbWriter
{
    /// <summary>Strictly more than this and install.wim cannot sit on FAT32 whole (its limit is 4 GiB).</summary>
    public const long MaxWimBytes = 4_000_000_000;

    private const string UsbMount = "/Volumes/WINFIX";
    private const string WimlibMissing = "Install wimlib first: brew install wimlib";

    /// <summary>Writes the ISO to a disk the user confirms. Returns the exit code; nothing is erased before the checks pass.</summary>
    public static async Task<int> WriteAsync(
        IMacSystem system, string iso, string? requestedDisk, TextWriter output, CancellationToken ct, string? winFixExe = null)
    {
        if (iso.Any(c => c == '"' || char.IsControl(c)))
        {
            output.WriteLine("The ISO path holds a double quote or a control character. Rename the file and run again.");
            return 1;
        }

        iso = Path.GetFullPath(iso);
        var step = 0;
        IsoMount? mounted = null;

        Announce("Check wimlib");
        if ((await system.RunCommandAsync("wimlib-imagex", ["--version"], ct)).ExitCode != 0)
        {
            output.WriteLine(WimlibMissing);
            return 1;
        }

        Announce($"Mount the ISO: {iso}");
        var (mount, attach) = await IsoMount.AttachAsync(system, iso, ct);
        if (mount is null)
        {
            return await FailAsync("Mount the ISO", attach);
        }

        mounted = mount;
        var wim = $"{mount.MountPoint}/sources/install.wim";
        var hasWim = system.FileSize(wim) is not null;
        var hasEsd = system.FileSize($"{mount.MountPoint}/sources/install.esd") is not null;
        if (system.FileSize($"{mount.MountPoint}/setup.exe") is null || !(hasWim || hasEsd))
        {
            output.WriteLine("This ISO is not Windows install media: setup.exe or sources/install.wim|esd is missing. Nothing was erased.");
            await mount.DetachAsync(system, ct);
            return 1;
        }

        Announce("Pick the USB");
        var disk = await DiskPicker.PickAsync(system, requestedDisk, output, ct);
        if (disk is null)
        {
            await mount.DetachAsync(system, ct);
            return 1;
        }

        int? failed = await RunStepAsync($"Erase the USB {disk.Identifier}", "diskutil", ["eraseDisk", "FAT32", "WINFIX", "MBR", disk.Identifier])
            ?? await RunStepAsync(
                "Copy Windows Setup", "rsync", ["-r", "--exclude", "/sources/install.wim", "--", mount.MountPoint + "/", UsbMount + "/"]);
        if (failed is null && hasWim)
        {
            var big = system.FileSize(wim) > MaxWimBytes;
            failed = big
                ? await RunStepAsync("Split install.wim", "wimlib-imagex", ["split", wim, $"{UsbMount}/sources/install.swm", "3800"])
                : await RunStepAsync("Copy install.wim", "cp", ["--", wim, $"{UsbMount}/sources/install.wim"]);
        }

        if (failed is not null)
        {
            return 1;
        }

        var exeCopied = false;
        if (winFixExe is not null)
        {
            Announce("Copy WinFix.exe");
            var copy = await system.RunCommandAsync("cp", ["--", winFixExe, $"{UsbMount}/WinFix.exe"], ct);
            exeCopied = copy.ExitCode == 0;
            if (!exeCopied)
            {
                output.WriteLine($"Warning: WinFix.exe could not be copied to the USB (exit code {copy.ExitCode}). Copy it there yourself: {WinFixExeProvider.DownloadUrl}");
            }
        }

        mounted = null;
        failed = await RunStepAsync("Unmount the ISO", "hdiutil", ["detach", mount.MountPoint])
            ?? await RunStepAsync($"Eject the USB {disk.Identifier}", "diskutil", ["eject", disk.Identifier]);
        if (failed is not null)
        {
            return 1;
        }

        output.WriteLine(exeCopied
            ? "Done. Boot the PC from the USB. If Windows starts again, run WinFix.exe from the USB."
            : "Done. WinFix.exe is not on the USB. Boot the PC from the USB to repair or reinstall Windows.");
        return 0;

        void Announce(string title) => output.WriteLine($"{++step}. {title}");

        // null when the command succeeded, else the exit code to return
        async Task<int?> RunStepAsync(string title, string command, string[] arguments)
        {
            Announce(title);
            var result = await system.RunCommandAsync(command, arguments, ct);
            return result.ExitCode == 0 ? null : await FailAsync(title, result);
        }

        async Task<int> FailAsync(string title, CommandResult result)
        {
            output.WriteLine($"Step failed: {title} (exit code {result.ExitCode})");
            var detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            if (!string.IsNullOrWhiteSpace(detail))
            {
                output.WriteLine(detail.Trim());
            }

            if (mounted is not null)
            {
                await mounted.DetachAsync(system, ct);
            }

            return 1;
        }
    }
}
