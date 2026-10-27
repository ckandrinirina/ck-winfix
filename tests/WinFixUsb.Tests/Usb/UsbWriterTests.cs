using SecurityElement = System.Security.SecurityElement;
using WinFixUsb.System;
using WinFixUsb.Usb;

namespace WinFixUsb.Tests.Usb;

public class UsbWriterTests
{
    private const string Iso = "/tmp/Win.iso";
    private const string Mount = "/Volumes/WINDOWS_ISO";
    private const string Wim = Mount + "/sources/install.wim";
    private const long FourGB = 4_000_000_000;

    private static readonly string[] HappyCommands =
    [
        "RunCommand wimlib-imagex --version",
        "RunCommand hdiutil attach -nobrowse -readonly -plist /tmp/Win.iso",
        "RunCommand diskutil list -plist external physical",
        "RunCommand diskutil eraseDisk FAT32 WINFIX MBR disk4",
        "RunCommand rsync -r --exclude /sources/install.wim -- /Volumes/WINDOWS_ISO/ /Volumes/WINFIX/",
        "RunCommand cp -- /Volumes/WINDOWS_ISO/sources/install.wim /Volumes/WINFIX/sources/install.wim",
        "RunCommand hdiutil detach /Volumes/WINDOWS_ISO",
        "RunCommand diskutil eject disk4",
    ];

    private static FakeMacSystem IsoFake(long wimSize = 1_000_000_000, params string[] answers)
    {
        var fake = new FakeMacSystem { Answers = answers.Length == 0 ? ["disk4"] : answers };
        fake.FileSizes[Mount + "/setup.exe"] = 100;
        fake.FileSizes[Wim] = wimSize;
        return fake;
    }

    private static async Task<(int Exit, string[] Lines, FakeMacSystem Fake)> WriteAsync(
        FakeMacSystem fake, string? disk = null)
    {
        var output = new StringWriter();
        var exit = await UsbWriter.WriteAsync(fake, Iso, disk, output, CancellationToken.None);
        return (exit, output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), fake);
    }

    private static string[] Commands(FakeMacSystem fake) =>
        [.. fake.Calls.Where(c => c.StartsWith("RunCommand "))];

    [Fact]
    public async Task Runs_the_steps_in_order_for_a_small_wim()
    {
        var (exit, _, fake) = await WriteAsync(IsoFake());

        Assert.Equal(0, exit);
        Assert.Equal(HappyCommands, Commands(fake));
    }

    [Fact]
    public async Task The_erase_only_runs_after_the_typed_confirmation()
    {
        var (_, _, fake) = await WriteAsync(IsoFake());

        var ask = fake.Calls.ToList().FindIndex(c => c.StartsWith("Ask "));
        var erase = fake.Calls.ToList().FindIndex(c => c.Contains("eraseDisk"));
        Assert.InRange(ask, 0, erase - 1);
    }

    [Fact]
    public async Task A_wim_over_4_GB_is_split_into_swm_parts_of_3800()
    {
        var (exit, _, fake) = await WriteAsync(IsoFake(FourGB + 1));

        Assert.Equal(0, exit);
        var commands = Commands(fake);
        Assert.Contains(
            "RunCommand wimlib-imagex split /Volumes/WINDOWS_ISO/sources/install.wim /Volumes/WINFIX/sources/install.swm 3800",
            commands);
        Assert.DoesNotContain(commands, c => c.StartsWith("RunCommand cp "));
    }

    [Fact]
    public async Task A_wim_of_exactly_4_GB_is_copied_as_is()
    {
        var (_, _, fake) = await WriteAsync(IsoFake(FourGB));

        var commands = Commands(fake);
        Assert.Contains("RunCommand cp -- /Volumes/WINDOWS_ISO/sources/install.wim /Volumes/WINFIX/sources/install.wim", commands);
        Assert.DoesNotContain(commands, c => c.Contains("split"));
    }

    [Fact]
    public async Task An_install_esd_is_copied_with_the_rest_and_needs_no_wim_step()
    {
        var fake = new FakeMacSystem { Answers = ["disk4"] };
        fake.FileSizes[Mount + "/setup.exe"] = 100;
        fake.FileSizes[Mount + "/sources/install.esd"] = 5_000_000_000;

        var (exit, _, _) = await WriteAsync(fake);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(Commands(fake), c => c.StartsWith("RunCommand cp ") || c.Contains("split"));
        Assert.Contains(Commands(fake), c => c.StartsWith("RunCommand rsync "));
    }

    [Fact]
    public async Task Missing_wimlib_stops_before_anything_else_with_the_brew_hint()
    {
        var fake = IsoFake();
        fake.FailingCommands.Add("wimlib-imagex");

        var (exit, lines, _) = await WriteAsync(fake);

        Assert.Equal(1, exit);
        Assert.Contains("Install wimlib first: brew install wimlib", lines);
        Assert.Equal(["RunCommand wimlib-imagex --version"], Commands(fake));
    }

    [Theory]
    [InlineData("/setup.exe")]
    [InlineData("/sources/install.wim")]
    public async Task An_iso_missing_setup_or_the_install_image_is_refused_before_the_erase(string missing)
    {
        var fake = IsoFake();
        fake.FileSizes.Remove(Mount + missing);

        var (exit, lines, _) = await WriteAsync(fake);

        Assert.Equal(1, exit);
        Assert.Contains(lines, l => l.Contains("not Windows install media"));
        var commands = Commands(fake);
        Assert.DoesNotContain(commands, c => c.Contains("eraseDisk"));
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("Ask "));
        Assert.Equal("RunCommand hdiutil detach /Volumes/WINDOWS_ISO", commands[^1]);
    }

    [Fact]
    public async Task A_wrong_confirmation_erases_nothing_and_detaches_the_iso()
    {
        var (exit, _, fake) = await WriteAsync(IsoFake(answers: "nope"));

        Assert.Equal(1, exit);
        var commands = Commands(fake);
        Assert.DoesNotContain(commands, c => c.Contains("eraseDisk"));
        Assert.Equal("RunCommand hdiutil detach /Volumes/WINDOWS_ISO", commands[^1]);
    }

    [Theory]
    [InlineData("hdiutil attach", "Mount the ISO", false)]
    [InlineData("diskutil eraseDisk", "Erase the USB", true)]
    [InlineData("rsync", "Copy Windows Setup", true)]
    [InlineData("cp ", "Copy install.wim", true)]
    [InlineData("diskutil eject", "Eject the USB", false)]
    public async Task A_failed_command_stops_the_run_names_the_step_and_exits_non_zero(
        string failing, string title, bool detached)
    {
        var fake = IsoFake();
        fake.FailingCommands.Add(failing);

        var (exit, lines, _) = await WriteAsync(fake);

        Assert.NotEqual(0, exit);
        Assert.Contains(lines, l => l.StartsWith("Step failed:") && l.Contains(title));
        var commands = Commands(fake);
        var failedAt = Array.FindIndex(commands, c => c.Contains(failing));
        var laterWork = commands[(failedAt + 1)..].Where(c => !c.Contains("hdiutil detach")).ToArray();
        Assert.Empty(laterWork);
        Assert.Equal(detached, commands[(failedAt + 1)..].Any(c => c.Contains("hdiutil detach")));
    }

    [Fact]
    public async Task A_failed_split_detaches_the_iso_and_never_ejects()
    {
        var fake = IsoFake(FourGB + 1);
        fake.FailingCommands.Add("wimlib-imagex split");

        var (exit, lines, _) = await WriteAsync(fake);

        Assert.NotEqual(0, exit);
        Assert.Contains(lines, l => l.StartsWith("Step failed:") && l.Contains("Split install.wim"));
        var commands = Commands(fake);
        Assert.Equal("RunCommand hdiutil detach /Volumes/WINDOWS_ISO", commands[^1]);
        Assert.DoesNotContain(commands, c => c.Contains("eject"));
    }

    [Fact]
    public async Task A_failed_detach_is_reported_and_the_usb_is_not_ejected()
    {
        var fake = IsoFake();
        fake.FailingCommands.Add("hdiutil detach");

        var (exit, lines, _) = await WriteAsync(fake);

        Assert.NotEqual(0, exit);
        Assert.Contains(lines, l => l.StartsWith("Step failed:") && l.Contains("Unmount the ISO"));
        Assert.Single(Commands(fake), c => c.Contains("hdiutil detach"));
        Assert.DoesNotContain(Commands(fake), c => c.Contains("eject"));
    }

    [Fact]
    public async Task The_requested_disk_is_the_one_erased()
    {
        var (exit, _, fake) = await WriteAsync(IsoFake(answers: "disk5"), "disk5");

        Assert.Equal(0, exit);
        Assert.Contains("RunCommand diskutil eraseDisk FAT32 WINFIX MBR disk5", Commands(fake));
        Assert.Contains("RunCommand diskutil eject disk5", Commands(fake));
    }

    [Fact]
    public async Task An_iso_path_with_spaces_is_one_argument()
    {
        var fake = IsoFake();
        var output = new StringWriter();

        await UsbWriter.WriteAsync(fake, "/tmp/My Win.iso", null, output, CancellationToken.None);

        Assert.Contains(fake.Invocations, i => i.File == "hdiutil" && i.Args.SequenceEqual(["attach", "-nobrowse", "-readonly", "-plist", "/tmp/My Win.iso"]));
    }

    [Fact]
    public async Task An_iso_path_starting_with_a_dash_becomes_absolute_so_it_is_never_an_option()
    {
        var fake = IsoFake();
        var output = new StringWriter();

        await UsbWriter.WriteAsync(fake, "-evil.iso", null, output, CancellationToken.None);

        var attach = fake.Invocations.Single(i => i.File == "hdiutil" && i.Args[0] == "attach");
        Assert.Equal(Path.GetFullPath("-evil.iso"), attach.Args[^1]);
        Assert.StartsWith("/", attach.Args[^1]);
    }

    [Fact]
    public async Task A_volume_name_with_spaces_and_a_leading_dash_is_one_argument_after_double_dash()
    {
        const string mount = "/Volumes/-e touch x";
        var fake = new FakeMacSystem { Answers = ["disk4"], IsoMountPlist = MountPlist(mount) };
        fake.FileSizes[mount + "/setup.exe"] = 100;
        fake.FileSizes[mount + "/sources/install.wim"] = 1_000;

        var (exit, _, _) = await WriteAsync(fake);

        Assert.Equal(0, exit);
        var rsync = fake.Invocations.Single(i => i.File == "rsync");
        Assert.Equal(["-r", "--exclude", "/sources/install.wim", "--", mount + "/", "/Volumes/WINFIX/"], rsync.Args);
        var cp = fake.Invocations.Single(i => i.File == "cp");
        Assert.Equal(["--", mount + "/sources/install.wim", "/Volumes/WINFIX/sources/install.wim"], cp.Args);
        Assert.Contains(fake.Invocations, i => i.File == "hdiutil" && i.Args.SequenceEqual(["detach", mount]));
    }

    [Theory]
    [InlineData("/tmp/evil")]
    [InlineData("/Volumesx/a")]
    [InlineData("/Volumes/")]
    [InlineData("/Volumes/a\"b")]
    [InlineData("/Volumes/a\\b")]
    [InlineData("/Volumes/a\nb")]
    [InlineData("/Volumes/a\" -e \"touch x")]
    [InlineData("/Volumes/../Users/x")]
    [InlineData("/Volumes/CCCOMA/..")]
    [InlineData("/Volumes/CCCOMA/../../etc")]
    public async Task An_unsafe_mount_point_is_refused_before_the_erase_and_the_iso_is_detached(string mount)
    {
        var fake = new FakeMacSystem { Answers = ["disk4"], IsoMountPlist = MountPlist(mount) };
        fake.FileSizes[mount + "/setup.exe"] = 100;
        fake.FileSizes[mount + "/sources/install.wim"] = 1_000;

        var (exit, lines, _) = await WriteAsync(fake);

        Assert.NotEqual(0, exit);
        Assert.Contains(lines, l => l.StartsWith("Step failed:") && l.Contains("Mount the ISO"));
        Assert.DoesNotContain(fake.Invocations, i => i.File is "diskutil" && i.Args[0] == "eraseDisk");
        Assert.DoesNotContain(fake.Invocations, i => i.File is "rsync" or "cp");
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("Ask "));
        Assert.Equal(["detach", "/dev/disk9"], fake.Invocations[^1].Args);
        Assert.DoesNotContain(fake.Invocations, i => i.Args.Contains(mount));
    }

    private static string MountPlist(string mount) =>
        "<?xml version=\"1.0\"?><plist version=\"1.0\"><dict><key>system-entities</key><array>"
        + "<dict><key>dev-entry</key><string>/dev/disk9</string></dict>"
        + $"<dict><key>dev-entry</key><string>/dev/disk9s1</string><key>mount-point</key><string>{SecurityElement.Escape(mount)}</string></dict>"
        + "</array></dict></plist>";

    [Fact]
    public async Task A_path_with_a_double_quote_is_refused_before_any_command()
    {
        var fake = IsoFake();
        var output = new StringWriter();

        var exit = await UsbWriter.WriteAsync(fake, "/tmp/a\"b.iso", null, output, CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(Commands(fake));
    }

    [Fact]
    public async Task The_mount_point_is_read_from_the_hdiutil_plist()
    {
        var fake = new FakeMacSystem
        {
            IsoMountPlist = """
                <?xml version="1.0"?><plist version="1.0"><dict><key>system-entities</key><array>
                <dict><key>content-hint</key><string>GUID_partition_scheme</string><key>dev-entry</key><string>/dev/disk9</string></dict>
                <dict><key>dev-entry</key><string>/dev/disk9s1</string><key>mount-point</key><string>/Volumes/CCCOMA_X64FRE</string></dict>
                </array></dict></plist>
                """,
        };

        var (mount, result) = await IsoMount.AttachAsync(fake, Iso, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("/Volumes/CCCOMA_X64FRE", mount?.MountPoint);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not xml")]
    [InlineData("<plist><dict><key>system-entities</key><array><dict><key>dev-entry</key><string>/dev/disk9</string></dict></array></dict></plist>")]
    public async Task A_plist_without_a_mount_point_gives_no_mount(string plist)
    {
        var fake = new FakeMacSystem { IsoMountPlist = plist };

        var (mount, _) = await IsoMount.AttachAsync(fake, Iso, CancellationToken.None);

        Assert.Null(mount);
    }

    [Fact]
    public async Task An_attach_without_a_mount_point_stops_the_run_before_the_erase()
    {
        var withoutMount = new FakeMacSystem { IsoMountPlist = "<plist/>", Answers = ["disk4"] };

        var (exit, lines, _) = await WriteAsync(withoutMount);

        Assert.Equal(1, exit);
        Assert.Contains(lines, l => l.StartsWith("Step failed:") && l.Contains("Mount the ISO"));
        Assert.DoesNotContain(Commands(withoutMount), c => c.Contains("eraseDisk"));
    }

    [Fact]
    public async Task Program_with_an_iso_and_no_dry_run_writes_through_the_injected_system()
    {
        var fake = IsoFake();
        var output = new StringWriter();

        var exit = await Program.RunAsync(["--iso", Iso], output, fake);

        Assert.Equal(0, exit);
        Assert.Equal(HappyCommands, Commands(fake));
    }

    [Fact]
    public void Without_dry_run_Program_builds_the_real_mac_system()
    {
        Assert.IsType<MacSystem>(Program.CreateSystem(dryRun: false));
    }

    private static Dictionary<string, string> UsbSources()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WinFix.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Directory.EnumerateFiles(Path.Combine(dir.FullName, "src", "WinFixUsb"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToDictionary(f => Path.GetFileName(f), File.ReadAllText);
    }

    [Fact]
    public void Only_MacSystem_starts_processes()
    {
        var offenders = UsbSources()
            .Where(f => f.Value.Contains("Process.Start") || f.Value.Contains("new Process"))
            .Select(f => f.Key)
            .ToArray();

        Assert.Equal(["MacSystem.cs"], offenders);
    }

    [Fact]
    public void MacSystem_passes_arguments_through_ArgumentList_and_nothing_sets_Arguments()
    {
        var sources = UsbSources();

        Assert.Contains("ArgumentList.Add", sources["MacSystem.cs"]);
        var stringArguments = new global::System.Text.RegularExpressions.Regex(@"\.Arguments\s*=[^=]|new\s+ProcessStartInfo\s*\([^()]*,");
        Assert.DoesNotContain(sources, f => stringArguments.IsMatch(f.Value));
    }

    private const string ExeSource = "/opt/winfixusb/WinFix.exe";
    private const string ExeTarget = "RunCommand cp -- /opt/winfixusb/WinFix.exe /Volumes/WINFIX/WinFix.exe";

    private static async Task<(int Exit, string Text, FakeMacSystem Fake)> WriteWithExeAsync(FakeMacSystem fake, string? exe = ExeSource)
    {
        var output = new StringWriter();
        var exit = await UsbWriter.WriteAsync(fake, Iso, null, output, CancellationToken.None, exe);
        return (exit, output.ToString(), fake);
    }

    [Fact]
    public async Task WinFix_exe_is_copied_to_the_usb_root_after_the_windows_files_and_before_the_unmount()
    {
        var (exit, _, fake) = await WriteWithExeAsync(IsoFake());

        Assert.Equal(0, exit);
        var commands = Commands(fake).ToList();
        var copy = commands.IndexOf(ExeTarget);
        Assert.True(copy > commands.IndexOf(HappyCommands[5]), "after install.wim");
        Assert.True(copy < commands.IndexOf("RunCommand hdiutil detach /Volumes/WINDOWS_ISO"), "before the unmount");
        Assert.Contains(fake.Invocations, i => i.File == "cp" && i.Args.SequenceEqual(["--", ExeSource, "/Volumes/WINFIX/WinFix.exe"]));
    }

    [Fact]
    public async Task A_failed_WinFix_exe_copy_warns_and_still_finishes_the_usb()
    {
        var fake = IsoFake();
        fake.FailingCommands.Add("cp -- " + ExeSource);

        var (exit, text, _) = await WriteWithExeAsync(fake);

        Assert.Equal(0, exit);
        Assert.Contains("Warning", text);
        Assert.Contains("WinFix.exe", text);
        Assert.Contains("RunCommand diskutil eject disk4", Commands(fake));
    }

    [Fact]
    public async Task Without_a_WinFix_exe_nothing_is_copied_and_the_usb_is_still_finished()
    {
        var (exit, text, fake) = await WriteWithExeAsync(IsoFake(), exe: null);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(Commands(fake), c => c.Contains("WinFix.exe"));
        Assert.Contains("not on the USB", text);
        Assert.Contains("RunCommand diskutil eject disk4", Commands(fake));
    }

    [Fact]
    public async Task The_final_message_says_to_boot_from_the_usb_and_run_WinFix_exe_if_windows_starts_again()
    {
        var (_, text, _) = await WriteWithExeAsync(IsoFake());

        var last = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[^1];
        Assert.Contains("boot the PC from the USB", last, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("If Windows starts again, run WinFix.exe from the USB", last);
    }

    [Fact]
    public async Task A_failed_run_prints_no_final_message()
    {
        var fake = IsoFake();
        fake.FailingCommands.Add("diskutil eraseDisk");

        var (exit, text, _) = await WriteWithExeAsync(fake);

        Assert.Equal(1, exit);
        Assert.DoesNotContain("boot the PC", text, StringComparison.OrdinalIgnoreCase);
    }
}
