using WinFix;
using WinFixUsb.Disks;
using WinFixUsb.System;
using WinFixUsb.Usb;

namespace WinFixUsb;

public static class Program
{
    private const string Product = "WinFixUsb";
    private const string UnknownIso = "<iso>";

    private const string Help = """
        Writes a bootable Windows install USB from a Mac and puts WinFix.exe on it.

        Usage: WinFixUsb [options]
          --iso <path>   Windows ISO to use (default: look for one, or download it)
          --disk <id>    external disk to erase, for example disk4 (default: ask)
          --dry-run      show every step and run nothing
          --help         show this text
        """;

    public static Task<int> Main(string[] args) => RunAsync(args, Console.Out);

    /// <summary>--dry-run never touches the Mac; its fake confirms the disk it was given (disk4 by default).</summary>
    public static IMacSystem CreateSystem(bool dryRun, string? disk = null) =>
        dryRun
            ? new FakeMacSystem { Answers = [disk ?? "disk4"] }
            : new MacSystem();

    public static async Task<int> RunAsync(string[] args, TextWriter output, IMacSystem? system = null)
    {
        AboutBanner.Write(output, Product, typeof(Program).Assembly);
        if (args.Contains("--help"))
        {
            output.WriteLine(Help);
            return 0;
        }

        var dryRun = args.Contains("--dry-run");
        var iso = ValueOf(args, "--iso");
        var requestedDisk = ValueOf(args, "--disk");
        if (!dryRun)
        {
            if (iso is null)
            {
                output.WriteLine("Pass the Windows ISO: WinFixUsb --iso <path>. To rehearse, run: WinFixUsb --dry-run");
                return 2;
            }

            return await UsbWriter.WriteAsync(system ?? CreateSystem(dryRun: false), iso, requestedDisk, output, CancellationToken.None);
        }

        iso ??= UnknownIso;
        system ??= CreateSystem(dryRun: true, requestedDisk);

        await Step(1, $"Find the ISO: {iso}", "hdiutil", $"attach -nobrowse -readonly -plist {iso}");

        output.WriteLine("2. Pick the USB");
        output.WriteLine("   would run: diskutil list -plist external physical");
        var picked = await DiskPicker.PickAsync(system, requestedDisk, output, CancellationToken.None);
        if (picked is null)
        {
            return 1;
        }

        var disk = picked.Identifier;
        output.WriteLine($"   picked: {disk} ({picked.Name}, {picked.SizeText})");

        await Step(3, $"Erase the USB {disk}", "diskutil", $"eraseDisk FAT32 WINFIX MBR {disk}");
        await Step(4, "Copy Windows Setup", "rsync", "-a --exclude sources/install.wim /Volumes/WINDOWS_ISO/ /Volumes/WINFIX/");
        await Step(5, "Split install.wim", "wimlib-imagex", "split /Volumes/WINDOWS_ISO/sources/install.wim /Volumes/WINFIX/sources/install.swm 3800");
        await Step(6, "Copy WinFix.exe", "cp", "WinFix.exe /Volumes/WINFIX/");
        await Step(7, $"Eject the USB {disk}", "diskutil", $"eject {disk}");
        return 0;

        async Task Step(int number, string title, string command, string arguments)
        {
            output.WriteLine($"{number}. {title}");
            output.WriteLine($"   would run: {command} {arguments}");
            await system.RunCommandAsync(command, arguments, CancellationToken.None);
        }
    }

    private static string? ValueOf(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
