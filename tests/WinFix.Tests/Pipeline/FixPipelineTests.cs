using WinFix.Pipeline;
using WinFix.System;

namespace WinFix.Tests.Pipeline;

public class FixPipelineTests
{
    private static readonly string[] Stages = ["Diagnose", "Repairs", "Confirm reset", "Reset"];

    private static async Task<(int Exit, string Output, FakeSystem Fake)> RunAsync(bool shellHealthy)
    {
        var fake = new FakeSystem { ShellHealthy = shellHealthy };
        var output = new StringWriter();
        var exit = await new FixPipeline(fake, output, readLine: () => "YES").RunAsync(CancellationToken.None);
        return (exit, output.ToString(), fake);
    }

    [Fact]
    public async Task Broken_shell_prints_every_stage_in_order_and_exits_zero()
    {
        var (exit, output, _) = await RunAsync(shellHealthy: false);

        Assert.Equal(0, exit);
        var positions = Stages.Select(s => output.IndexOf(s, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, p => Assert.True(p >= 0, $"stage missing from output:\n{output}"));
        Assert.Equal(positions.OrderBy(p => p), positions);
    }

    [Fact]
    public async Task Healthy_shell_stops_after_diagnose()
    {
        var (exit, output, fake) = await RunAsync(shellHealthy: true);

        Assert.Equal(0, exit);
        Assert.Contains("Diagnose", output);
        Assert.Contains("Shell is healthy — nothing to do", output);
        Assert.DoesNotContain("Repairs", output);
        Assert.DoesNotContain("Reset", output);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("RunCommand") || c.StartsWith("Reboot"));
    }

    [Fact]
    public async Task Diagnose_prints_the_reason_and_writes_it_to_the_log()
    {
        var (_, output, fake) = await RunAsync(shellHealthy: false);

        Assert.Contains("Shell_TrayWnd", output);
        Assert.Contains(fake.Logs, l => l.Contains("Shell_TrayWnd"));
    }

    [Fact]
    public async Task Healthy_diagnosis_reason_is_printed_and_logged()
    {
        var (_, output, fake) = await RunAsync(shellHealthy: true);

        Assert.Contains("taskbar", output, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(fake.Logs);
    }

    [Fact]
    public async Task Shell_that_is_healthy_after_repair_skips_the_reset()
    {
        var fake = new FakeSystem { ShellHealthy = false, RepairFixesShell = true };
        var output = new StringWriter();

        var exit = await new FixPipeline(fake, output, readLine: () => "YES").RunAsync(CancellationToken.None);

        var text = output.ToString();
        Assert.Equal(0, exit);
        Assert.Contains("Repairs", text);
        Assert.Contains("Fixed by", text);
        Assert.DoesNotContain("Confirm reset", text);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("Reboot"));
    }

    [Fact]
    public async Task Shell_still_broken_after_repair_goes_on_to_the_reset()
    {
        var (_, output, fake) = await RunAsync(shellHealthy: false);

        Assert.Contains("Confirm reset", output);
        Assert.Equal(7, fake.Calls.Count(c => c == "FindWindow Shell_TrayWnd"));
    }

    [Fact]
    public async Task Every_action_is_recorded_by_the_fake()
    {
        var (_, _, fake) = await RunAsync(shellHealthy: false);

        Assert.Contains(fake.Calls, c => c.StartsWith("FindWindow"));
        Assert.Contains(fake.Calls, c => c.StartsWith("RunCommand"));
        Assert.Contains(fake.Calls, c => c.StartsWith("Reboot"));
    }

    [Fact]
    public async Task Dry_run_switch_runs_the_whole_flow_with_the_fake_and_exits_zero()
    {
        var output = new StringWriter();

        var exit = await Program.RunAsync(["--dry-run"], output);

        Assert.Equal(0, exit);
        var text = output.ToString();
        var positions = Stages.Select(s => text.IndexOf(s, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, p => Assert.True(p >= 0, $"stage missing from output:\n{text}"));
        Assert.Equal(positions.OrderBy(p => p), positions);
    }

    [Fact]
    public async Task Without_dry_run_nothing_runs_and_exit_is_nonzero()
    {
        var output = new StringWriter();

        var exit = await Program.RunAsync([], output);

        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("Diagnose", output.ToString());
    }
}
