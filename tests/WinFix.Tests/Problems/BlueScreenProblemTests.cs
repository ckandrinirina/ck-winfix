using WinFix.Problems;
using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Tests.Problems;

public class BlueScreenProblemTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstCrash = Now.AddDays(-2);
    private static readonly DateTimeOffset SecondCrash = Now.AddDays(-1);

    private const string BugCheckMessage =
        "The computer has rebooted from a bugcheck. The bugcheck was: 0x0000009f (0x0000000000000003, 0xffffd18a, 0x0000000000000000, 0x0000000000000000). A dump was saved in: C:\\Windows\\MEMORY.DMP.";

    private static string Event(DateTimeOffset time, int id, string message) => $"{time:o}|{id}|{message}";

    private static string BugCheck(DateTimeOffset time) => Event(time, 1001, BugCheckMessage);

    private static string KernelPower(DateTimeOffset time) => Event(time, 41, "The system has rebooted without cleanly shutting down first.");

    private static string DriverBlock(string published, string provider) =>
        $"Published Name:     {published}\nOriginal Name:      {published[..^4]}x.inf\nProvider Name:      {provider}\nClass Name:         Display adapters\nDriver Version:     03/15/2026 1.2.3.4\nSigner Name:        Some Publisher\n";

    private static string HotFix(string kb, DateTimeOffset installed, string description = "Security Update") =>
        $"{kb}|{installed:yyyy-MM-dd}|{description}";

    private sealed class Machine
    {
        public string Events { get; set; } = string.Empty;

        public string Minidumps { get; set; } = string.Empty;

        public string Drivers { get; set; } = string.Empty;

        public Dictionary<string, string> InstallTimes { get; } = [];

        public string HotFixes { get; set; } = string.Empty;

        public int DeleteDriverExit { get; set; }

        public int WusaExit { get; set; }

        public FakeSystem Build() => new() { CommandResponder = Respond };

        private static CommandResult Ok(string output) => new(0, output);

        public CommandResult Respond(string file, string args)
        {
            if (file == "powershell" && args.Contains("Get-WinEvent"))
            {
                return Ok(Events);
            }

            if (file == "cmd" && args.Contains("Minidump"))
            {
                return Minidumps.Length == 0 ? new CommandResult(1, "File Not Found") : Ok(Minidumps);
            }

            if (file == "pnputil" && args.StartsWith("/enum-drivers", StringComparison.Ordinal))
            {
                return Ok("Microsoft PnP Utility\n\n" + Drivers);
            }

            if (file == "powershell" && args.Contains("CreationTime"))
            {
                var hit = InstallTimes.FirstOrDefault(p => args.Contains($@"\INF\{p.Key}'"));
                return hit.Key is null ? new CommandResult(1, "not found") : Ok(hit.Value + "\n");
            }

            if (file == "powershell" && args.Contains("Get-HotFix"))
            {
                return Ok(HotFixes);
            }

            if (file == "pnputil" && args.StartsWith("/delete-driver", StringComparison.Ordinal))
            {
                return new CommandResult(DeleteDriverExit, string.Empty);
            }

            if (file == "wusa")
            {
                return new CommandResult(WusaExit, string.Empty);
            }

            return new CommandResult(1, "unexpected command");
        }
    }

    private static Machine TwoCrashes() => new()
    {
        Events = string.Join('\n', KernelPower(FirstCrash), BugCheck(SecondCrash)),
    };

    private static IEnumerable<string> DeleteCalls(FakeSystem fake) =>
        fake.Calls.Where(c => c.StartsWith("RunCommand pnputil /delete-driver", StringComparison.Ordinal));

    // Detection

    [Fact]
    public async Task Two_crashes_in_seven_days_are_broken_and_name_the_count()
    {
        var fake = TwoCrashes().Build();

        var health = await new BlueScreenProblem(() => Now).DetectAsync(fake, CancellationToken.None);

        Assert.False(health.Healthy);
        Assert.Contains("2", health.Reason);
        Assert.Contains("0x0000009f", health.Reason);
    }

    [Fact]
    public async Task Detection_reads_the_system_log_for_events_1001_and_41()
    {
        var fake = TwoCrashes().Build();

        await new BlueScreenProblem(() => Now).DetectAsync(fake, CancellationToken.None);

        var query = Assert.Single(fake.Calls, c => c.Contains("Get-WinEvent"));
        Assert.Contains("System", query);
        Assert.Contains("1001", query);
        Assert.Contains("41", query);
    }

    [Fact]
    public async Task One_crash_is_healthy()
    {
        var machine = new Machine { Events = BugCheck(SecondCrash) };

        var health = await new BlueScreenProblem(() => Now).DetectAsync(machine.Build(), CancellationToken.None);

        Assert.True(health.Healthy);
    }

    [Fact]
    public async Task Crashes_older_than_seven_days_are_not_counted()
    {
        var machine = new Machine { Events = string.Join('\n', KernelPower(Now.AddDays(-8)), BugCheck(SecondCrash)) };

        var health = await new BlueScreenProblem(() => Now).DetectAsync(machine.Build(), CancellationToken.None);

        Assert.True(health.Healthy);
    }

    [Fact]
    public async Task Exactly_seven_days_old_still_counts()
    {
        var machine = new Machine { Events = string.Join('\n', KernelPower(Now.AddDays(-7)), BugCheck(SecondCrash)) };

        var health = await new BlueScreenProblem(() => Now).DetectAsync(machine.Build(), CancellationToken.None);

        Assert.False(health.Healthy);
    }

    [Fact]
    public async Task No_events_and_a_failed_query_are_healthy()
    {
        var empty = await new BlueScreenProblem(() => Now).DetectAsync(new Machine().Build(), CancellationToken.None);
        var failed = await new BlueScreenProblem(() => Now).DetectAsync(new FakeSystem { CommandExitCode = 1, CommandOutput = "access denied" }, CancellationToken.None);

        Assert.True(empty.Healthy);
        Assert.True(failed.Healthy);
    }

    [Fact]
    public async Task Lines_that_are_not_crash_events_are_ignored()
    {
        var machine = new Machine { Events = string.Join('\n', "garbage", Event(Now.AddDays(-1), 6008, "unexpected shutdown"), "", KernelPower(FirstCrash)) };

        var health = await new BlueScreenProblem(() => Now).DetectAsync(machine.Build(), CancellationToken.None);

        Assert.True(health.Healthy);
    }

    [Fact]
    public async Task Broken_detection_logs_every_stop_code_and_minidump_file()
    {
        var machine = TwoCrashes();
        machine.Events += "\n" + Event(SecondCrash.AddHours(3), 1001, "The bugcheck was: 0x0000000a (0x1, 0x2, 0x3, 0x4).");
        machine.Minidumps = "102526-1.dmp\r\n102626-2.dmp\r\n";
        var fake = machine.Build();

        await new BlueScreenProblem(() => Now).DetectAsync(fake, CancellationToken.None);

        Assert.Contains(fake.Logs, l => l.Contains("0x0000009f"));
        Assert.Contains(fake.Logs, l => l.Contains("0x0000000a"));
        Assert.Contains(fake.Logs, l => l.Contains(@"C:\Windows\Minidump\102526-1.dmp"));
        Assert.Contains(fake.Logs, l => l.Contains(@"C:\Windows\Minidump\102626-2.dmp"));
        Assert.Contains(fake.Calls, c => c.Contains(@"C:\Windows\Minidump"));
    }

    [Fact]
    public async Task A_missing_minidump_folder_does_not_break_detection()
    {
        var fake = TwoCrashes().Build();

        var health = await new BlueScreenProblem(() => Now).DetectAsync(fake, CancellationToken.None);

        Assert.False(health.Healthy);
    }

    [Fact]
    public void Module_is_named_bluescreen_and_rolls_back_drivers_then_updates()
    {
        var module = new BlueScreenProblem();

        Assert.Equal("bluescreen", module.Name);
        Assert.Collection(module.Repairs, r => Assert.IsType<RollbackDriverStep>(r), r => Assert.IsType<RollbackUpdateStep>(r));
    }

    // Driver rollback

    private static Machine DriverMachine()
    {
        var machine = TwoCrashes();
        machine.Drivers = string.Join(
            "\n",
            DriverBlock("oem10.inf", "Contoso Graphics"),
            DriverBlock("oem11.inf", "Contoso Graphics"),
            DriverBlock("oem12.inf", "Contoso Graphics"),
            DriverBlock("oem13.inf", "Fabrikam"),
            DriverBlock("oem20.inf", "Microsoft"),
            DriverBlock("oem21.inf", "microsoft corporation"));
        machine.InstallTimes["oem10.inf"] = FirstCrash.AddDays(-1).ToString("o");
        machine.InstallTimes["oem11.inf"] = FirstCrash.AddDays(-5).ToString("o");
        machine.InstallTimes["oem12.inf"] = FirstCrash.AddHours(2).ToString("o");
        machine.InstallTimes["oem13.inf"] = FirstCrash.AddDays(-3).ToString("o");
        machine.InstallTimes["oem20.inf"] = FirstCrash.AddDays(-1).ToString("o");
        machine.InstallTimes["oem21.inf"] = FirstCrash.AddDays(-1).ToString("o");
        return machine;
    }

    [Fact]
    public async Task Drivers_installed_in_the_three_days_before_the_first_crash_are_removed()
    {
        var fake = DriverMachine().Build();

        var result = await new RollbackDriverStep(() => Now).RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("RunCommand pnputil /delete-driver oem10.inf /uninstall", fake.Calls);
        Assert.Contains("RunCommand pnputil /delete-driver oem13.inf /uninstall", fake.Calls);
    }

    [Fact]
    public async Task Drivers_outside_the_window_are_kept()
    {
        var fake = DriverMachine().Build();

        await new RollbackDriverStep(() => Now).RunAsync(fake, CancellationToken.None);

        Assert.DoesNotContain(DeleteCalls(fake), c => c.Contains("oem11.inf"));
        Assert.DoesNotContain(DeleteCalls(fake), c => c.Contains("oem12.inf"));
        Assert.Equal(2, DeleteCalls(fake).Count());
    }

    [Fact]
    public async Task Microsoft_drivers_are_never_removed_even_inside_the_window()
    {
        var fake = DriverMachine().Build();

        await new RollbackDriverStep(() => Now).RunAsync(fake, CancellationToken.None);

        var thirdParty = new[] { "oem10.inf", "oem13.inf" };
        Assert.All(DeleteCalls(fake), c => Assert.Contains(thirdParty, name => c.Contains(name)));
        Assert.DoesNotContain(fake.Calls, c => c.Contains("delete-driver") && (c.Contains("oem20.inf") || c.Contains("oem21.inf")));
    }

    [Fact]
    public async Task Only_microsoft_drivers_installed_means_nothing_is_removed()
    {
        var machine = TwoCrashes();
        machine.Drivers = DriverBlock("oem20.inf", "Microsoft");
        machine.InstallTimes["oem20.inf"] = FirstCrash.AddDays(-1).ToString("o");
        var fake = machine.Build();

        var result = await new RollbackDriverStep(() => Now).RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(DeleteCalls(fake));
        Assert.False(result.RebootRequired);
    }

    [Fact]
    public async Task Removing_a_driver_asks_for_a_restart()
    {
        var result = await new RollbackDriverStep(() => Now).RunAsync(DriverMachine().Build(), CancellationToken.None);

        Assert.True(result.RebootRequired);
    }

    [Fact]
    public async Task Driver_removal_that_needs_a_restart_is_still_a_success()
    {
        var machine = DriverMachine();
        machine.DeleteDriverExit = 3010;

        var result = await new RollbackDriverStep(() => Now).RunAsync(machine.Build(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.RebootRequired);
    }

    [Fact]
    public async Task A_failed_driver_removal_is_a_failure_with_its_exit_code()
    {
        var machine = DriverMachine();
        machine.DeleteDriverExit = 5;

        var result = await new RollbackDriverStep(() => Now).RunAsync(machine.Build(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(5, result.ExitCode);
    }

    [Fact]
    public async Task A_driver_with_an_unknown_install_time_is_left_alone()
    {
        var machine = DriverMachine();
        machine.InstallTimes.Remove("oem10.inf");
        var fake = machine.Build();

        await new RollbackDriverStep(() => Now).RunAsync(fake, CancellationToken.None);

        Assert.DoesNotContain(DeleteCalls(fake), c => c.Contains("oem10.inf"));
    }

    [Fact]
    public async Task Without_a_crash_no_driver_is_touched()
    {
        var machine = DriverMachine();
        machine.Events = string.Empty;
        var fake = machine.Build();

        var result = await new RollbackDriverStep(() => Now).RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(DeleteCalls(fake));
        Assert.DoesNotContain(fake.Calls, c => c.Contains("/enum-drivers"));
    }

    [Fact]
    public async Task A_failed_driver_listing_is_a_failure_and_removes_nothing()
    {
        var machine = TwoCrashes();
        var fake = new FakeSystem
        {
            CommandResponder = (file, args) => file == "pnputil" ? new CommandResult(1, "failed") : machine.Respond(file, args),
        };

        var result = await new RollbackDriverStep(() => Now).RunAsync(fake, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(DeleteCalls(fake));
    }

    // Update rollback

    private static Machine UpdateMachine()
    {
        var machine = TwoCrashes();
        machine.HotFixes = string.Join(
            "\n",
            HotFix("KB5001111", FirstCrash.AddDays(-4)),
            HotFix("KB5002222", FirstCrash.AddDays(-2)),
            HotFix("KB5003333", FirstCrash.AddDays(-1), "Update"),
            HotFix("KB5004444", FirstCrash.AddDays(1)));
        return machine;
    }

    [Fact]
    public async Task The_newest_update_in_the_three_days_before_the_first_crash_is_uninstalled()
    {
        var fake = UpdateMachine().Build();

        var result = await new RollbackUpdateStep(() => Now).RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Single(fake.Calls, c => c.StartsWith("RunCommand wusa", StringComparison.Ordinal));
        Assert.Contains("RunCommand wusa /uninstall /kb:5003333 /quiet /norestart", fake.Calls);
    }

    [Fact]
    public async Task Update_rollback_requests_a_restart()
    {
        var result = await new RollbackUpdateStep(() => Now).RunAsync(UpdateMachine().Build(), CancellationToken.None);

        Assert.True(result.RebootRequired);
    }

    [Fact]
    public async Task An_update_installed_after_the_first_crash_or_long_before_it_is_kept()
    {
        var machine = TwoCrashes();
        machine.HotFixes = string.Join('\n', HotFix("KB5001111", FirstCrash.AddDays(-4)), HotFix("KB5004444", FirstCrash.AddDays(1)));
        var fake = machine.Build();

        var result = await new RollbackUpdateStep(() => Now).RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.RebootRequired);
        Assert.DoesNotContain(fake.Calls, c => c.Contains("wusa"));
    }

    [Fact]
    public async Task An_update_installed_exactly_three_days_before_still_counts()
    {
        var machine = TwoCrashes();
        machine.HotFixes = HotFix("KB5009999", FirstCrash.AddDays(-3));
        var fake = machine.Build();

        await new RollbackUpdateStep(() => Now).RunAsync(fake, CancellationToken.None);

        Assert.Contains("RunCommand wusa /uninstall /kb:5009999 /quiet /norestart", fake.Calls);
    }

    [Fact]
    public async Task A_failed_uninstall_is_a_failure_with_its_exit_code()
    {
        var machine = UpdateMachine();
        machine.WusaExit = 2359302;

        var result = await new RollbackUpdateStep(() => Now).RunAsync(machine.Build(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(2359302, result.ExitCode);
    }

    [Fact]
    public async Task An_uninstall_that_reports_a_pending_restart_is_a_success()
    {
        var machine = UpdateMachine();
        machine.WusaExit = 3010;

        var result = await new RollbackUpdateStep(() => Now).RunAsync(machine.Build(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.RebootRequired);
    }

    [Fact]
    public async Task Without_a_crash_no_update_is_touched()
    {
        var machine = UpdateMachine();
        machine.Events = string.Empty;
        var fake = machine.Build();

        var result = await new RollbackUpdateStep(() => Now).RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(fake.Calls, c => c.Contains("wusa") || c.Contains("Get-HotFix"));
    }

    [Fact]
    public async Task Update_history_lines_that_are_not_updates_are_ignored()
    {
        var machine = TwoCrashes();
        machine.HotFixes = string.Join('\n', "junk", $"NotAKb|{FirstCrash.AddDays(-1):yyyy-MM-dd}|Update", HotFix("KB5002222", FirstCrash.AddDays(-2)));
        var fake = machine.Build();

        await new RollbackUpdateStep(() => Now).RunAsync(fake, CancellationToken.None);

        Assert.Contains("RunCommand wusa /uninstall /kb:5002222 /quiet /norestart", fake.Calls);
    }

    [Fact]
    public async Task A_driver_name_with_non_ascii_digits_is_never_queried_or_removed()
    {
        const string crafted = "oem\u0661\u0662.inf";
        var machine = TwoCrashes();
        machine.Drivers = DriverBlock(crafted, "Contoso");
        machine.InstallTimes[crafted] = FirstCrash.AddDays(-1).ToString("o");
        var fake = machine.Build();

        await new RollbackDriverStep(() => Now).RunAsync(fake, CancellationToken.None);

        Assert.DoesNotContain(fake.Calls, c => c.Contains(crafted, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_hotfix_id_with_non_ascii_digits_is_never_passed_to_wusa()
    {
        var machine = TwoCrashes();
        machine.HotFixes = HotFix("KB\uFF15\uFF10\uFF10\uFF13", FirstCrash.AddDays(-1));
        var fake = machine.Build();

        var result = await new RollbackUpdateStep(() => Now).RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(fake.Calls, c => c.Contains("wusa", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(@"C:\Win""&calc&""dows")]
    [InlineData(@"C:\Win%COMSPEC%dows")]
    public async Task A_crafted_windows_folder_never_reaches_the_minidump_command(string windows)
    {
        var fake = new FakeSystem { CommandResponder = TwoCrashes().Respond, WindowsDirectory = windows };

        var health = await new BlueScreenProblem(() => Now).DetectAsync(fake, CancellationToken.None);

        Assert.False(health.Healthy);
        Assert.DoesNotContain(fake.Calls, c => c.Contains("calc", StringComparison.Ordinal) || c.Contains("%COMSPEC%", StringComparison.Ordinal));
    }
}
