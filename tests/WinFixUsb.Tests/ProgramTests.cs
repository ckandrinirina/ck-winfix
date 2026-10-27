using System.Text.RegularExpressions;
using WinFixUsb.System;

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

    private static async Task<(int Exit, string[] Lines, FakeMacSystem Fake)> RunAsync(params string[] args)
    {
        var output = new StringWriter();
        var fake = new FakeMacSystem();

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
        Assert.All(fake.Calls, c => Assert.StartsWith("RunCommand ", c));
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

        var result = await fake.RunCommandAsync("diskutil", "list -plist external physical", CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("disk4", result.Output);
    }

    [Fact]
    public async Task Dry_run_uses_the_disk_and_iso_given_on_the_command_line()
    {
        var (exit, lines, fake) = await RunAsync("--dry-run", "--disk", "disk9", "--iso", "/tmp/Win.iso");

        Assert.Equal(0, exit);
        Assert.Contains(fake.Calls, c => c.Contains("disk9"));
        Assert.Contains(fake.Calls, c => c.Contains("/tmp/Win.iso"));
        Assert.Contains(lines, l => l.Contains("disk9"));
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
    public async Task Without_dry_run_or_help_nothing_runs_and_the_exit_code_is_2()
    {
        var (exit, _, fake) = await RunAsync();

        Assert.Equal(2, exit);
        Assert.Empty(fake.Calls);
    }
}
