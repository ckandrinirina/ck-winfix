using System.Text.RegularExpressions;
using WinFix.Media;
using WinFixUsb.System;
using WinFixUsb.Tests.Disks;
using WinFixUsb.Tests.Usb;
using WinFixUsb.Usb;

namespace WinFixUsb.Tests;

public class ProgramTests
{
    private static readonly string[] PlanOrder =
    [
        "Find the ISO",
        "Pick the USB",
        "Erase the USB",
        "Copy Windows Setup",
        "Split install.wim",
        "Copy WinFix.exe",
        "Eject the USB",
    ];

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WinFix.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, relative);
    }

    private static Task<(int Exit, string[] Lines, FakeMacSystem Fake)> RunAsync(params string[] args) =>
        RunWithAsync(new FakeMacSystem { Answers = ["disk4"] }, args);

    private static async Task<(int Exit, string[] Lines, FakeMacSystem Fake)> RunWithAsync(
        FakeMacSystem fake, params string[] args)
    {
        var output = new StringWriter();

        var exit = await Program.RunAsync(args, output, fake);

        return (exit, output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), fake);
    }

    [Fact]
    public void Solution_lists_the_usb_tool_and_its_tests()
    {
        var sln = File.ReadAllText(RepoFile("WinFix.sln"));

        Assert.Contains(@"src\WinFixUsb\WinFixUsb.csproj", sln);
        Assert.Contains(@"tests\WinFixUsb.Tests\WinFixUsb.Tests.csproj", sln);
    }

    [Fact]
    public void Usb_tool_targets_net10_and_references_WinFix()
    {
        var csproj = File.ReadAllText(RepoFile(Path.Combine("src", "WinFixUsb", "WinFixUsb.csproj")));

        Assert.Contains("<TargetFramework>net10.0</TargetFramework>", csproj);
        Assert.Contains(@"..\WinFix\WinFix.csproj", csproj);
    }

    [Fact]
    public void Usb_tests_use_xunit_v3_4_0_2()
    {
        var csproj = File.ReadAllText(RepoFile(Path.Combine("tests", "WinFixUsb.Tests", "WinFixUsb.Tests.csproj")));

        Assert.Matches(new Regex(@"Include=""xunit\.v3""\s+Version=""4\.0\.2"""), csproj);
    }

    [Fact]
    public async Task Dry_run_prints_the_author_line_first_with_product_WinFixUsb()
    {
        var (exit, lines, _) = await RunAsync("--dry-run");

        Assert.Equal(0, exit);
        Assert.Matches(@"^WinFixUsb \S+ — by ANDRINIRINA Erick · github\.com/ckandrinirina$", lines[0]);
    }

    [Fact]
    public async Task Dry_run_prints_the_seven_steps_in_order()
    {
        var (_, lines, _) = await RunAsync("--dry-run");

        var positions = PlanOrder.Select(step => Array.FindIndex(lines, l => l.Contains(step))).ToArray();
        Assert.All(positions, p => Assert.True(p > 0, "every step is printed after the author line"));
        Assert.Equal(positions.Order(), positions);
    }

    [Fact]
    public async Task Dry_run_sends_every_os_call_to_the_fake_system()
    {
        var (_, _, fake) = await RunAsync("--dry-run");

        Assert.Contains(fake.Calls, c => c.StartsWith("RunCommand diskutil list"));
        Assert.Contains(fake.Calls, c => c.StartsWith("RunCommand diskutil eraseDisk"));
        Assert.Contains(fake.Calls, c => c.StartsWith("RunCommand diskutil eject"));
        Assert.All(fake.Calls, c => Assert.True(c.StartsWith("RunCommand ") || c.StartsWith("Ask "), c));
    }

    [Fact]
    public async Task Dry_run_without_an_injected_system_uses_the_fake()
    {
        Assert.IsType<FakeMacSystem>(Program.CreateSystem(dryRun: true));

        var output = new StringWriter();
        var exit = await Program.RunAsync(["--dry-run"], output);

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task Fake_lists_fixture_disks()
    {
        var fake = new FakeMacSystem();

        var result = await fake.RunCommandAsync("diskutil", ["list", "-plist", "external", "physical"], CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("disk4", result.Output);
    }

    [Fact]
    public async Task Dry_run_uses_the_disk_and_iso_given_on_the_command_line()
    {
        var (exit, lines, fake) = await RunWithAsync(
            new FakeMacSystem { Answers = ["disk5"] }, "--dry-run", "--disk", "disk5", "--iso", "/tmp/Win.iso");

        Assert.Equal(0, exit);
        Assert.Contains(fake.Calls, c => c.Contains("eraseDisk FAT32 WINFIX MBR disk5"));
        Assert.Contains(fake.Calls, c => c.Contains("/tmp/Win.iso"));
        Assert.Contains(lines, l => l.Contains("disk5"));
    }

    [Fact]
    public async Task Fake_stores_files_in_memory_and_answers_prompts_from_its_script()
    {
        var fake = new FakeMacSystem { Answers = ["disk4"] };

        fake.WriteFile("/tmp/a.txt", "hi");

        Assert.Equal("hi", fake.ReadFile("/tmp/a.txt"));
        Assert.Null(fake.ReadFile("/tmp/missing"));
        Assert.Equal("disk4", await fake.AskAsync("Which disk?", CancellationToken.None));
        Assert.Contains("Ask Which disk?", fake.Calls);
    }

    [Fact]
    public async Task Help_lists_the_three_options_and_exits_0()
    {
        var (exit, lines, _) = await RunAsync("--help");

        Assert.Equal(0, exit);
        var text = string.Join('\n', lines);
        Assert.Contains("--iso <path>", text);
        Assert.Contains("--disk <id>", text);
        Assert.Contains("--dry-run", text);
    }

    [Fact]
    public async Task Help_prints_the_author_line_first_and_runs_no_command()
    {
        var (_, lines, fake) = await RunAsync("--help");

        Assert.StartsWith("WinFixUsb ", lines[0]);
        Assert.Empty(fake.Calls);
    }

    [Fact]
    public async Task Dry_run_plan_uses_the_disk_the_user_confirmed()
    {
        var (exit, lines, fake) = await RunAsync("--dry-run");

        Assert.Equal(0, exit);
        Assert.Contains(fake.Calls, c => c == "RunCommand diskutil eraseDisk FAT32 WINFIX MBR disk4");
        Assert.Contains(lines, l => l.Contains("disk4") && l.Contains("SanDisk Ultra"));
        Assert.Contains(fake.Calls, c => c.StartsWith("Ask "));
    }

    [Fact]
    public async Task A_wrong_confirmation_exits_1_and_plans_no_erase()
    {
        var (exit, lines, fake) = await RunWithAsync(new FakeMacSystem { Answers = ["nope"] }, "--dry-run");

        Assert.Equal(1, exit);
        Assert.Contains(lines, l => l.Contains("Nothing was erased."));
        Assert.DoesNotContain(fake.Calls, c => c.Contains("eraseDisk"));
    }

    [Fact]
    public async Task An_unknown_disk_on_the_command_line_exits_1_and_plans_no_erase()
    {
        var (exit, _, fake) = await RunAsync("--dry-run", "--disk", "disk0");

        Assert.Equal(1, exit);
        Assert.DoesNotContain(fake.Calls, c => c.Contains("eraseDisk"));
    }

    [Fact]
    public async Task No_external_disk_exits_1_with_the_plug_in_message()
    {
        var (exit, lines, fake) = await RunWithAsync(
            new FakeMacSystem { ExternalDisks = DiskFixtures.Plist() }, "--dry-run");

        Assert.Equal(1, exit);
        Assert.Contains("Plug in a USB drive of at least 8 GB and run again.", lines);
        Assert.DoesNotContain(fake.Calls, c => c.Contains("eraseDisk"));
    }

    [Fact]
    public async Task Dry_run_without_an_injected_system_confirms_the_disk_it_was_given()
    {
        var output = new StringWriter();

        var exit = await Program.RunAsync(["--dry-run", "--disk", "disk5"], output);

        Assert.Equal(0, exit);
        Assert.Contains("disk5", output.ToString());
    }

    private const string Downloads = "/Users/me/Downloads";
    private const string ToolFolder = "/opt/winfixusb";
    private const string TempExe = "/tmp/WinFixUsb/WinFix.exe";
    private const string IsoFile = Downloads + "/Windows11_x64_en-US.iso";
    private static readonly Uri IsoUrl = new("https://software.download.prss.microsoft.com/dbazure/Win11_English_x64.iso");
    private static readonly byte[] Payload = [1, 2, 3, 4, 5, 6, 7, 8];

    private sealed class Rig
    {
        public FakeMacSystem Fake { get; } = new() { Answers = ["disk4"] };

        public MemoryFiles Files { get; } = new();

        public FakeSource Source { get; set; } = new(new IsoLink(IsoUrl, null));

        public bool ExeOnGitHub { get; set; } = true;

        public bool ExeBesideTool { get; set; }

        public List<string> Requests { get; } = [];

        public List<bool> ErasedBeforeRequest { get; } = [];

        public UsbServices Services()
        {
            Fake.FileSizes["/Volumes/WINDOWS_ISO/setup.exe"] = 100;
            Fake.FileSizes["/Volumes/WINDOWS_ISO/sources/install.wim"] = 1_000_000_000;
            if (ExeBesideTool)
            {
                Fake.FileSizes[ToolFolder + "/WinFix.exe"] = 100;
            }

            var handler = new FakeHandler(request =>
                request.RequestUri!.Host.EndsWith("microsoft.com", StringComparison.Ordinal) ? FakeHandler.Bytes(Payload)
                : ExeOnGitHub ? FakeHandler.Bytes(Payload)
                : FakeHandler.Status(global::System.Net.HttpStatusCode.NotFound))
            {
                OnRequest = request =>
                {
                    Requests.Add(request.RequestUri!.Host);
                    ErasedBeforeRequest.Add(Fake.Invocations.Any(i => i.Args.Contains("eraseDisk")));
                },
            };
            return new UsbServices
            {
                Http = new HttpClient(handler),
                IsoSource = Source,
                Files = Files,
                DownloadsFolder = Downloads,
                ToolFolder = ToolFolder,
                TempExe = TempExe,
                Language = "en-US",
                Delay = (_, _) => Task.CompletedTask,
            };
        }
    }

    private static async Task<(int Exit, string Text)> RunRealAsync(Rig rig, params string[] args)
    {
        var output = new StringWriter();
        var exit = await Program.RunAsync(args, output, rig.Fake, rig.Services());
        return (exit, output.ToString());
    }

    [Fact]
    public async Task Without_iso_the_Windows_11_x64_ISO_is_downloaded_into_downloads_and_used()
    {
        var rig = new Rig();

        var (exit, _) = await RunRealAsync(rig);

        Assert.Equal(0, exit);
        Assert.Equal(new IsoRequest("11", "x64", "en-US"), Assert.Single(rig.Source.Requests));
        Assert.Equal(Payload, rig.Files.Data[IsoFile].ToArray());
        Assert.Contains(rig.Fake.Invocations, i => i.File == "hdiutil" && i.Args.Contains(IsoFile));
    }

    [Fact]
    public async Task The_download_finishes_before_anything_runs_on_the_mac_and_before_the_erase()
    {
        var rig = new Rig();

        await RunRealAsync(rig);

        Assert.NotEmpty(rig.Requests);
        Assert.All(rig.ErasedBeforeRequest, erased => Assert.False(erased));
        Assert.Contains(rig.Fake.Invocations, i => i.Args.Contains("eraseDisk"));
    }

    [Fact]
    public async Task A_failed_download_exits_1_with_the_manual_page_and_runs_nothing()
    {
        var rig = new Rig { Source = new FakeSource(null) };

        var (exit, text) = await RunRealAsync(rig);

        Assert.Equal(1, exit);
        Assert.Contains(IsoProvider.ManualPage, text);
        Assert.Empty(rig.Fake.Calls);
    }

    [Fact]
    public async Task With_iso_nothing_is_downloaded_for_the_iso()
    {
        var rig = new Rig();

        var (exit, _) = await RunRealAsync(rig, "--iso", "/tmp/Win.iso");

        Assert.Equal(0, exit);
        Assert.Empty(rig.Source.Requests);
        Assert.DoesNotContain("software.download.prss.microsoft.com", rig.Requests);
    }

    [Fact]
    public async Task WinFix_exe_from_beside_the_tool_is_copied_to_the_usb_root()
    {
        var rig = new Rig { ExeBesideTool = true };

        var (exit, _) = await RunRealAsync(rig);

        Assert.Equal(0, exit);
        Assert.Contains(rig.Fake.Invocations, i => i.File == "cp" && i.Args.SequenceEqual(["--", ToolFolder + "/WinFix.exe", "/Volumes/WINFIX/WinFix.exe"]));
        Assert.DoesNotContain("github.com", rig.Requests);
    }

    [Fact]
    public async Task WinFix_exe_is_downloaded_before_the_erase_copied_and_the_temp_file_removed()
    {
        var rig = new Rig();

        var (exit, _) = await RunRealAsync(rig, "--iso", "/tmp/Win.iso");

        Assert.Equal(0, exit);
        Assert.Contains("github.com", rig.Requests);
        Assert.All(rig.ErasedBeforeRequest, erased => Assert.False(erased));
        Assert.Contains(rig.Fake.Invocations, i => i.File == "cp" && i.Args.SequenceEqual(["--", TempExe, "/Volumes/WINFIX/WinFix.exe"]));
        Assert.DoesNotContain(TempExe, rig.Files.Data.Keys);
    }

    [Fact]
    public async Task When_WinFix_exe_cannot_be_found_or_downloaded_the_usb_is_still_finished_with_a_warning()
    {
        var rig = new Rig { ExeOnGitHub = false };

        var (exit, text) = await RunRealAsync(rig, "--iso", "/tmp/Win.iso");

        Assert.Equal(0, exit);
        Assert.Contains("Warning", text);
        Assert.DoesNotContain(rig.Fake.Invocations, i => i.File == "cp" && i.Args.Any(a => a.EndsWith("WinFix.exe", StringComparison.Ordinal)));
        Assert.Contains(rig.Fake.Invocations, i => i.File == "diskutil" && i.Args.Contains("eject"));
    }

    [Fact]
    public async Task The_closing_message_tells_the_user_to_boot_from_the_usb_and_run_WinFix_exe_if_windows_starts_again()
    {
        var rig = new Rig { ExeBesideTool = true };

        var (_, text) = await RunRealAsync(rig, "--iso", "/tmp/Win.iso");

        Assert.Contains("boot the PC from the USB", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("If Windows starts again, run WinFix.exe from the USB", text);
    }
}
