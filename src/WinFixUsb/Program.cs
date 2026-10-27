using WinFix;
using WinFix.Media;
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
          --iso <path>   Windows ISO to use (default: download Windows 11 into Downloads)
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

    public static async Task<int> RunAsync(string[] args, TextWriter output, IMacSystem? system = null, UsbServices? services = null)
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
            return await WriteUsbAsync(system ?? CreateSystem(dryRun: false), services ?? new UsbServices(), iso, requestedDisk, output);
        }

        iso ??= UnknownIso;
        system ??= CreateSystem(dryRun: true, requestedDisk);

        await Step(1, $"Find the ISO: {iso}", "hdiutil", "attach", "-nobrowse", "-readonly", "-plist", iso);

        output.WriteLine("2. Pick the USB");
        output.WriteLine("   would run: diskutil list -plist external physical");
        var picked = await DiskPicker.PickAsync(system, requestedDisk, output, CancellationToken.None);
        if (picked is null)
        {
            return 1;
        }

        var disk = picked.Identifier;
        output.WriteLine($"   picked: {disk} ({picked.Name}, {picked.SizeText})");

        await Step(3, $"Erase the USB {disk}", "diskutil", "eraseDisk", "FAT32", "WINFIX", "MBR", disk);
        await Step(4, "Copy Windows Setup", "rsync", "-r", "--exclude", "/sources/install.wim", "--", "/Volumes/WINDOWS_ISO/", "/Volumes/WINFIX/");
        await Step(5, "Split install.wim", "wimlib-imagex", "split", "/Volumes/WINDOWS_ISO/sources/install.wim", "/Volumes/WINFIX/sources/install.swm", "3800");
        await Step(6, "Copy WinFix.exe", "cp", "--", "WinFix.exe", "/Volumes/WINFIX/");
        await Step(7, $"Eject the USB {disk}", "diskutil", "eject", disk);
        return 0;

        async Task Step(int number, string title, string command, params string[] arguments)
        {
            output.WriteLine($"{number}. {title}");
            output.WriteLine($"   would run: {command} {string.Join(' ', arguments)}");
            await system.RunCommandAsync(command, arguments, CancellationToken.None);
        }
    }

    /// <summary>Both downloads finish here, before the writer erases anything.</summary>
    private static async Task<int> WriteUsbAsync(
        IMacSystem system, UsbServices services, string? iso, string? requestedDisk, TextWriter output)
    {
        var ct = CancellationToken.None;
        if (iso is null)
        {
            output.WriteLine("No --iso given: downloading Windows 11 from Microsoft into your Downloads folder.");
            var source = services.IsoSource ?? new MicrosoftIsoSource(services.Http, output.WriteLine);
            var provider = new IsoProvider(source, services.Http, services.Files, services.DownloadsFolder, services.Language, services.Delay);
            iso = await provider.DownloadAsync(output, ct);
            if (iso is null)
            {
                return 1;
            }
        }

        var exeProvider = new WinFixExeProvider(system, services.Http, services.Files, services.ToolFolder, services.TempExe);
        try
        {
            var exe = await exeProvider.StageAsync(output, ct);
            return await UsbWriter.WriteAsync(system, iso, requestedDisk, output, ct, exe);
        }
        finally
        {
            exeProvider.Cleanup();
        }
    }

    private static string? ValueOf(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
