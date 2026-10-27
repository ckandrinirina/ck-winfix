using WinFix.Pipeline;
using WinFix.Reset;
using WinFix.System;

namespace WinFix.Tests.Reset;

public class BootToWinReAttemptTests
{
    private const string BootToRe = "RunCommand reagentc /boottore";
    private const string Shutdown = "RunCommand shutdown /r /o /t 30";

    private static List<string> Commands(FakeSystem fake) =>
        fake.Calls.Where(c => c.StartsWith("RunCommand", StringComparison.Ordinal)).ToList();

    [Fact]
    public async Task Prints_the_recovery_steps_before_it_runs_any_command()
    {
        var output = new StringWriter();
        var seenBeforeBootToRe = string.Empty;
        var fake = new FakeSystem
        {
            CommandResponder = (_, _) =>
            {
                seenBeforeBootToRe = output.ToString();
                return new CommandResult(0, string.Empty);
            },
        };

        await new BootToWinReAttempt(output).RunAsync(fake, CancellationToken.None);

        Assert.Contains("Troubleshoot", seenBeforeBootToRe);
        Assert.Contains("Reset this PC", seenBeforeBootToRe);
        Assert.Contains("Keep my files", seenBeforeBootToRe);
        Assert.True(
            seenBeforeBootToRe.IndexOf("Troubleshoot", StringComparison.Ordinal)
            < seenBeforeBootToRe.IndexOf("Reset this PC", StringComparison.Ordinal));
        Assert.True(
            seenBeforeBootToRe.IndexOf("Reset this PC", StringComparison.Ordinal)
            < seenBeforeBootToRe.IndexOf("Keep my files", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sets_the_next_boot_to_recovery_then_restarts_in_thirty_seconds()
    {
        var fake = new FakeSystem();

        var result = await new BootToWinReAttempt(new StringWriter()).RunAsync(fake, CancellationToken.None);

        Assert.True(result.Started);
        Assert.Equal([BootToRe, Shutdown], Commands(fake));
    }

    [Fact]
    public async Task A_failed_boottore_exits_with_code_4_names_the_log_and_never_reboots()
    {
        var output = new StringWriter();
        var fake = new FakeSystem { CommandExitCode = 2 };

        var result = await new BootToWinReAttempt(output).RunAsync(fake, CancellationToken.None);

        Assert.False(result.Started);
        Assert.Equal(4, result.ExitCode);
        Assert.Contains(ResetConfirmation.LogPath, output.ToString());
        Assert.Equal([BootToRe], Commands(fake));
        Assert.DoesNotContain(fake.Calls, c => c.Contains("shutdown", StringComparison.Ordinal));
        Assert.Contains(fake.Logs, l => l.Contains("reagentc"));
    }

    [Fact]
    public async Task A_failed_shutdown_also_exits_with_code_4_and_names_the_log()
    {
        var output = new StringWriter();
        var fake = new FakeSystem
        {
            CommandResponder = (file, _) => new CommandResult(file == "shutdown" ? 1 : 0, string.Empty),
        };

        var result = await new BootToWinReAttempt(output).RunAsync(fake, CancellationToken.None);

        Assert.False(result.Started);
        Assert.Equal(4, result.ExitCode);
        Assert.Contains(ResetConfirmation.LogPath, output.ToString());
    }

    [Fact]
    public async Task The_chain_carries_exit_code_4_when_every_attempt_failed()
    {
        var fake = new FakeSystem { CommandExitCode = 2 };

        var result = await new ResetChain([new BootToWinReAttempt(new StringWriter())]).RunAsync(fake, CancellationToken.None);

        Assert.False(result.Started);
        Assert.Equal(4, result.ExitCode);
    }

    [Fact]
    public async Task The_chain_reports_the_boot_to_winre_attempt_when_it_starts()
    {
        var result = await new ResetChain([new BootToWinReAttempt(new StringWriter())]).RunAsync(new FakeSystem(), CancellationToken.None);

        Assert.True(result.Started);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(new BootToWinReAttempt(new StringWriter()).Name, result.AttemptName);
    }
}
