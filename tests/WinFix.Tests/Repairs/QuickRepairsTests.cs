using WinFix.Diagnose;
using WinFix.Pipeline;
using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Tests.Repairs;

public class QuickRepairsTests
{
    private const string IrisKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\IrisService";

    private static readonly string[] ShellPackages =
        ["ShellExperienceHost", "StartMenuExperienceHost", "Windows.Client.CBS"];

    private static ShellHealthCheck QuickCheck(ISystem system) =>
        new(system, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1));

    private static async Task<(int Exit, string Output, FakeSystem Fake)> RunPipelineAsync(
        FakeSystem fake, IReadOnlyList<IRepairStep>? steps = null)
    {
        var output = new StringWriter();
        var exit = await new FixPipeline(fake, output, QuickCheck(fake), steps, () => "YES").RunAsync(CancellationToken.None);
        return (exit, output.ToString(), fake);
    }

    [Fact]
    public async Task Restart_step_kills_every_explorer_then_starts_it_again()
    {
        var fake = new FakeSystem { ExplorerProcessIdSamples = [[100, 200]] };

        var result = await new RestartExplorerStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        var calls = fake.Calls.ToList();
        var kill100 = calls.IndexOf("KillProcess 100");
        var kill200 = calls.IndexOf("KillProcess 200");
        var start = calls.IndexOf("StartProcess explorer.exe");
        Assert.True(kill100 >= 0 && kill200 >= 0, "both explorer.exe processes are killed");
        Assert.True(start > kill100 && start > kill200, "explorer.exe starts after the kills");
    }

    [Fact]
    public async Task Restart_step_starts_explorer_even_when_none_is_running()
    {
        var fake = new FakeSystem();

        var result = await new RestartExplorerStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("StartProcess explorer.exe", fake.Calls);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("KillProcess"));
    }

    [Fact]
    public async Task IrisService_step_deletes_the_key()
    {
        var fake = new FakeSystem { ExistingRegistryKeys = [IrisKey] };

        var result = await new ClearIrisServiceStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains($"DeleteRegistryKey {IrisKey}", fake.Calls);
    }

    [Fact]
    public async Task IrisService_step_is_a_success_when_the_key_is_already_absent()
    {
        var fake = new FakeSystem();

        var result = await new ClearIrisServiceStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Reregister_step_registers_each_shell_package_through_powershell()
    {
        var fake = new FakeSystem();

        var result = await new ReRegisterShellAppsStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        var commands = fake.Calls.Where(c => c.StartsWith("RunCommand")).ToList();
        Assert.Equal(ShellPackages.Length, commands.Count);
        foreach (var package in ShellPackages)
        {
            var command = Assert.Single(commands, c => c.Contains(package));
            Assert.Contains("powershell.exe", command);
            Assert.Contains("Add-AppxPackage", command);
            Assert.Contains("-Register", command);
        }
    }

    [Fact]
    public async Task Reregister_step_tries_every_package_and_reports_a_failing_exit_code()
    {
        var fake = new FakeSystem { CommandExitCode = 5 };

        var result = await new ReRegisterShellAppsStep().RunAsync(fake, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(5, result.ExitCode);
        Assert.Equal(ShellPackages.Length, fake.Calls.Count(c => c.StartsWith("RunCommand")));
    }

    [Fact]
    public async Task Pipeline_runs_the_steps_cheapest_first()
    {
        var (_, _, fake) = await RunPipelineAsync(new FakeSystem());

        var calls = fake.Calls.ToList();
        var restart = calls.IndexOf("StartProcess explorer.exe");
        var iris = calls.IndexOf($"DeleteRegistryKey {IrisKey}");
        var reregister = calls.FindIndex(c => c.StartsWith("RunCommand powershell.exe"));
        Assert.True(restart >= 0 && iris > restart && reregister > iris, string.Join("\n", calls));
    }

    [Fact]
    public async Task Health_check_runs_after_each_step()
    {
        var (_, _, fake) = await RunPipelineAsync(new FakeSystem());

        // one diagnosis plus one check per default step (3 quick, 3 deep)
        Assert.Equal(7, fake.Calls.Count(c => c == "FindWindow Shell_TrayWnd"));
    }

    [Fact]
    public async Task First_healthy_result_ends_the_run_with_fixed_by_and_exit_zero()
    {
        var fake = new FakeSystem { FixedByCall = "DeleteRegistryKey" };

        var (exit, output, _) = await RunPipelineAsync(fake);

        Assert.Equal(0, exit);
        Assert.Contains("Fixed by Clear IrisService", output);
        Assert.Contains(fake.Logs, l => l.Contains("Fixed by Clear IrisService"));
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("RunCommand"));
        Assert.DoesNotContain("Confirm reset", output);
        Assert.DoesNotContain(fake.Calls, c => c == "Reboot");
    }

    [Fact]
    public async Task First_step_fixing_the_shell_skips_the_remaining_steps()
    {
        var fake = new FakeSystem { FixedByCall = "StartProcess" };

        var (exit, output, _) = await RunPipelineAsync(fake);

        Assert.Equal(0, exit);
        Assert.Contains("Fixed by Restart Explorer", output);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("DeleteRegistryKey"));
    }

    [Fact]
    public async Task Step_that_throws_is_logged_and_the_next_step_still_runs()
    {
        var fake = new FakeSystem { RepairFixesShell = true };

        var (exit, output, _) = await RunPipelineAsync(fake, [new ThrowingStep(), new CommandStep()]);

        Assert.Equal(0, exit);
        Assert.Contains(fake.Logs, l => l.Contains("Boom") && l.Contains("Throwing step"));
        Assert.Contains("RunCommand fix.exe", fake.Calls);
        Assert.Contains("Fixed by Command step", output);
    }

    [Fact]
    public async Task Step_with_a_nonzero_exit_code_is_logged_and_the_next_step_still_runs()
    {
        var fake = new FakeSystem { RepairFixesShell = true };

        var (_, output, _) = await RunPipelineAsync(fake, [new FailingStep(), new CommandStep()]);

        Assert.Contains(fake.Logs, l => l.Contains("Failing step") && l.Contains("7"));
        Assert.Contains("RunCommand fix.exe", fake.Calls);
        Assert.Contains("Fixed by Command step", output);
    }

    [Fact]
    public async Task Steps_that_all_fail_leave_the_shell_broken_and_go_on_to_the_reset()
    {
        var (_, output, fake) = await RunPipelineAsync(new FakeSystem(), [new ThrowingStep(), new FailingStep()]);

        Assert.DoesNotContain("Fixed by", output);
        Assert.Contains("Confirm reset", output);
        Assert.Contains("Reboot", fake.Calls);
    }

    private sealed class ThrowingStep : IRepairStep
    {
        public string Name => "Throwing step";

        public Task<StepResult> RunAsync(ISystem system, CancellationToken ct) =>
            throw new InvalidOperationException("Boom");
    }

    private sealed class FailingStep : IRepairStep
    {
        public string Name => "Failing step";

        public Task<StepResult> RunAsync(ISystem system, CancellationToken ct) =>
            Task.FromResult(new StepResult(7, "It failed."));
    }

    private sealed class CommandStep : IRepairStep
    {
        public string Name => "Command step";

        public async Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
        {
            var result = await system.RunCommandAsync("fix.exe", string.Empty, ct);
            return new StepResult(result.ExitCode, "Ran fix.exe.");
        }
    }
}
