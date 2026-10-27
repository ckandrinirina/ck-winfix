using WinFix;
using WinFixUsb.System;

namespace WinFixUsb;

public static class Program
{
    private const string Product = "WinFixUsb";
    private const string UnknownIso = "<iso>";
    private const string UnknownDisk = "<disk>";

    private const string Help = """
        Writes a bootable Windows install USB from a Mac and puts WinFix.exe on it.

        Usage: WinFixUsb [options]
          --iso <path>   Windows ISO to use (default: look for one, or download it)
          --disk <id>    external disk to erase, for example disk4 (default: ask)
          --dry-run      show every step and run nothing
          --help         show this text
        """;

    public static Task<int> Main(string[] args) => RunAsync(args, Console.Out);

    /// <summary>--dry-run never touches the Mac.</summary>
    public static IMacSystem CreateSystem(bool dryRun) =>
        dryRun ? new FakeMacSystem() : throw new NotSupportedException("The real Mac system is not built yet.");

    public static async Task<int> RunAsync(string[] args, TextWriter output, IMacSystem? system = null)
    {
        AboutBanner.Write(output, Product, typeof(Program).Assembly);
        if (args.Contains("--help"))
        {
            output.WriteLine(Help);
            return 0;
        }

        if (!args.Contains("--dry-run"))
        {
            output.WriteLine("Only --dry-run works so far. Run: WinFixUsb --dry-run");
            return 2;
        }

        var iso = ValueOf(args, "--iso") ?? UnknownIso;
        var disk = ValueOf(args, "--disk") ?? UnknownDisk;
        system ??= CreateSystem(dryRun: true);

        var plan = new (string Title, string Command, string Arguments)[]
        {
            ($"Find the ISO: {iso}", "hdiutil", $"attach -nobrowse -readonly -plist {iso}"),
            ($"Pick the USB: {disk}", "diskutil", "list -plist external physical"),
            ($"Erase the USB {disk}", "diskutil", $"eraseDisk FAT32 WINFIX MBR {disk}"),
            ("Copy Windows Setup", "rsync", "-a --exclude sources/install.wim /Volumes/WINDOWS_ISO/ /Volumes/WINFIX/"),
            ("Split install.wim", "wimlib-imagex", "split /Volumes/WINDOWS_ISO/sources/install.wim /Volumes/WINFIX/sources/install.swm 3800"),
            ("Copy WinFix.exe", "cp", "WinFix.exe /Volumes/WINFIX/"),
            ($"Eject the USB {disk}", "diskutil", $"eject {disk}"),
        };

        for (var i = 0; i < plan.Length; i++)
        {
            var (title, command, arguments) = plan[i];
            output.WriteLine($"{i + 1}. {title}");
            output.WriteLine($"   would run: {command} {arguments}");
            await system.RunCommandAsync(command, arguments, CancellationToken.None);
        }

        return 0;
    }

    private static string? ValueOf(string[] args, string option)
    {
        var index = Array.IndexOf(args, option);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
