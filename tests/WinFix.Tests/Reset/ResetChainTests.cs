using System.Text.RegularExpressions;
using WinFix.Reset;
using WinFix.System;

namespace WinFix.Tests.Reset;

public class ResetChainTests
{
    private const string InfoDisabled = "Windows RE status:         Disabled\r\nWindows RE location:\r\nBoot Configuration Data (BCD) identifier: 00000000-0000-0000-0000-000000000000";
    private const string InfoEnabled = "Windows RE status:         Enabled\r\nWindows RE location:       \\\\?\\GLOBALROOT\\device\\harddisk0\\partition4\\Recovery\\WindowsRE";

    private sealed class FakeAttempt(string name, bool started) : IResetAttempt
    {
        public string Name => name;

        public int Runs { get; private set; }

        public Task<ResetAttemptResult> RunAsync(ISystem system, CancellationToken ct)
        {
            Runs++;
            return Task.FromResult(new ResetAttemptResult(started, started ? "started" : "refused"));
        }
    }

    private sealed class ThrowingAttempt : IResetAttempt
    {
        public string Name => "Throwing";

        public Task<ResetAttemptResult> RunAsync(ISystem system, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }

    private static FakeSystem Responding(Func<string, string, CommandResult> responder) =>
        new() { CommandResponder = responder };

    private static CommandResult TaskQuery(string args, string lastResult) =>
        args.StartsWith("/Query", StringComparison.Ordinal)
            ? new CommandResult(0, $"TaskName:  \\WinFixReset\r\nLast Result:                          {lastResult}\r\n")
            : new CommandResult(0, "");

    private static FakeSystem ResetSystem(string lastResult, string info = InfoEnabled) =>
        Responding((file, args) => file == "schtasks" ? TaskQuery(args, lastResult) : new CommandResult(0, info));

    [Theory]
    [InlineData(InfoEnabled, WinReStatus.Enabled)]
    [InlineData(InfoDisabled, WinReStatus.Disabled)]
    [InlineData("Windows RE status:  disabled", WinReStatus.Disabled)]
    [InlineData("nothing useful here", WinReStatus.Unknown)]
    [InlineData("", WinReStatus.Unknown)]
    public void Reagentc_info_output_is_parsed(string output, WinReStatus expected)
    {
        Assert.Equal(expected, RepairWinReStep.ParseStatus(output));
    }

    [Fact]
    public async Task Enabled_winre_is_left_alone()
    {
        var fake = Responding((_, _) => new CommandResult(0, InfoEnabled));

        var result = await new RepairWinReStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(fake.Calls, c => c.Contains("reagentc /enable"));
    }

    [Fact]
    public async Task Disabled_winre_is_enabled_and_rechecked()
    {
        var enabled = false;
        var fake = Responding((_, args) =>
        {
            if (args == "/enable")
            {
                enabled = true;
                return new CommandResult(0, "REAGENTC.EXE: Operation Successful.");
            }

            return new CommandResult(0, enabled ? InfoEnabled : InfoDisabled);
        });

        var result = await new RepairWinReStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        var reagentc = fake.Calls.Where(c => c.StartsWith("RunCommand reagentc", StringComparison.Ordinal)).ToList();
        Assert.Equal(["RunCommand reagentc /info", "RunCommand reagentc /enable", "RunCommand reagentc /info"], reagentc);
    }

    [Fact]
    public async Task Winre_that_stays_disabled_after_enable_is_a_failure()
    {
        var fake = Responding((_, _) => new CommandResult(0, InfoDisabled));

        var result = await new RepairWinReStep().RunAsync(fake, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(fake.Logs, l => l.Contains("WinRE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_failing_enable_command_is_a_failure()
    {
        var fake = Responding((_, args) => args == "/enable"
            ? new CommandResult(1, "error")
            : new CommandResult(0, InfoDisabled));

        var result = await new RepairWinReStep().RunAsync(fake, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Reset_creates_a_one_shot_system_task_that_calls_the_keep_files_wmi_method()
    {
        var fake = ResetSystem("0");

        await new MdmKeepFilesReset().RunAsync(fake, CancellationToken.None);

        var create = Assert.Single(fake.Calls, c => c.StartsWith("RunCommand schtasks /Create", StringComparison.Ordinal));
        Assert.Contains("/RU SYSTEM", create);
        Assert.Contains("/SC ONCE", create);
        Assert.Contains("doWipePersistUserDataMethod", create);
        Assert.Contains(@"Root\CIMv2\MDM\DMMap", create);
        Assert.Contains("MDM_RemoteWipe", create);
        var calls = fake.Calls.ToList();
        Assert.True(calls.FindIndex(c => c.Contains("schtasks /Create")) < calls.FindIndex(c => c.Contains("schtasks /Run")));
    }

    [Fact]
    public async Task Remove_everything_wipe_is_never_called()
    {
        var fake = ResetSystem("0", InfoDisabled);

        await new MdmKeepFilesReset().RunAsync(fake, CancellationToken.None);
        await new ResetChain([new MdmKeepFilesReset()]).RunAsync(fake, CancellationToken.None);

        Assert.All(fake.Calls, c => Assert.DoesNotContain("doWipeMethod", c));
        Assert.All(fake.Files.Values, v => Assert.DoesNotContain("doWipeMethod", v));
        Assert.Contains(fake.Calls, c => c.Contains("doWipePersistUserDataMethod"));
        Assert.DoesNotMatch(new Regex(@"doWipe(?!PersistUserData)"), string.Join('\n', fake.Calls));
    }

    [Fact]
    public async Task A_non_zero_task_result_is_a_failure_that_is_logged()
    {
        var fake = ResetSystem("-2147024891");

        var result = await new MdmKeepFilesReset().RunAsync(fake, CancellationToken.None);

        Assert.False(result.Started);
        Assert.Contains(fake.Logs, l => l.Contains("-2147024891"));
    }

    [Fact]
    public async Task A_failing_task_create_is_a_failure()
    {
        var fake = Responding((file, args) => file == "schtasks" && args.StartsWith("/Create", StringComparison.Ordinal)
            ? new CommandResult(1, "ERROR: Access is denied.")
            : new CommandResult(0, InfoEnabled));

        var result = await new MdmKeepFilesReset().RunAsync(fake, CancellationToken.None);

        Assert.False(result.Started);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("RunCommand schtasks /Run", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_reboot_within_the_timeout_is_a_failure_after_waiting_the_full_timeout()
    {
        var timeout = TimeSpan.FromMinutes(3);
        var fake = ResetSystem("0");

        var result = await new MdmKeepFilesReset(timeout: timeout).RunAsync(fake, CancellationToken.None);

        Assert.False(result.Started);
        Assert.True(fake.TotalDelay >= timeout);
        Assert.Contains(fake.Logs, l => l.Contains("reboot", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fake.Calls, c => c.StartsWith("RunCommand schtasks /Delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Chain_stops_at_the_first_attempt_that_starts()
    {
        var first = new FakeAttempt("First", started: true);
        var second = new FakeAttempt("Second", started: true);
        var fake = new FakeSystem();

        var result = await new ResetChain([first, second]).RunAsync(fake, CancellationToken.None);

        Assert.True(result.Started);
        Assert.Equal("First", result.AttemptName);
        Assert.Equal(0, second.Runs);
    }

    [Fact]
    public async Task Chain_logs_a_failed_attempt_and_moves_to_the_next()
    {
        var first = new FakeAttempt("First", started: false);
        var second = new FakeAttempt("Second", started: true);
        var fake = new FakeSystem();

        var result = await new ResetChain([first, second]).RunAsync(fake, CancellationToken.None);

        Assert.True(result.Started);
        Assert.Equal("Second", result.AttemptName);
        Assert.Contains(fake.Logs, l => l.Contains("First") && l.Contains("refused"));
    }

    [Fact]
    public async Task Chain_treats_a_throwing_attempt_as_a_failure()
    {
        var next = new FakeAttempt("Next", started: true);
        var fake = new FakeSystem();

        var result = await new ResetChain([new ThrowingAttempt(), next]).RunAsync(fake, CancellationToken.None);

        Assert.True(result.Started);
        Assert.Contains(fake.Logs, l => l.Contains("Throwing") && l.Contains("boom"));
    }

    [Fact]
    public async Task Chain_reports_failure_when_every_attempt_fails()
    {
        var fake = new FakeSystem();

        var result = await new ResetChain([new FakeAttempt("A", false), new FakeAttempt("B", false)])
            .RunAsync(fake, CancellationToken.None);

        Assert.False(result.Started);
        Assert.Null(result.AttemptName);
    }
}
