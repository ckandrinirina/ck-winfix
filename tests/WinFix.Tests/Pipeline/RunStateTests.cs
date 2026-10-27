using WinFix.Pipeline;
using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Tests.Pipeline;

public class RunStateTests
{
    private const string StatePath = @"%ProgramData%\WinFix\state.json";
    private const string RunOnceKey = @"HKLM\Software\Microsoft\Windows\CurrentVersion\RunOnce";

    private sealed class MarkerStep(string name, bool needsReboot = false) : IRepairStep
    {
        public string Name => name;

        public bool NeedsReboot => needsReboot;

        public Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
        {
            system.StartProcess($"step-{name}");
            return Task.FromResult(new StepResult(0, $"{name} done."));
        }
    }

    private static async Task<(int Exit, string Output)> RunAsync(
        FakeSystem fake, IReadOnlyList<IRepairStep> steps, bool resume = false)
    {
        var output = new StringWriter();
        var exit = await new FixPipeline(fake, output, steps: steps, readLine: () => "YES").RunAsync(CancellationToken.None, resume);
        return (exit, output.ToString());
    }

    private static int IndexOf(FakeSystem fake, string prefix) =>
        fake.Calls.ToList().FindIndex(c => c.StartsWith(prefix, StringComparison.Ordinal));

    [Fact]
    public void Existing_steps_do_not_need_a_reboot()
    {
        IRepairStep[] steps = [new RestartExplorerStep(), new ClearIrisServiceStep(), new ReRegisterShellAppsStep()];

        Assert.All(steps, step => Assert.False(step.NeedsReboot));
    }

    [Fact]
    public async Task Step_that_needs_a_reboot_saves_the_next_stage_and_a_RunOnce_value_before_it_runs()
    {
        var fake = new FakeSystem { ExecutablePath = @"C:\Tools\WinFix.exe" };
        IRepairStep[] steps = [new MarkerStep("A"), new MarkerStep("B", needsReboot: true), new MarkerStep("C")];

        var (exit, _) = await RunAsync(fake, steps);

        Assert.Equal(0, exit);
        Assert.Equal(2, RunState.Parse(fake.Files[StatePath])!.NextStage);
        Assert.Equal("\"C:\\Tools\\WinFix.exe\" --resume", fake.RegistryValues[(RunOnceKey, "WinFix")]);
        var save = IndexOf(fake, $"WriteFile {StatePath}");
        var runOnce = IndexOf(fake, $"WriteRegistry {RunOnceKey} WinFix=");
        var stepB = IndexOf(fake, "StartProcess step-B");
        Assert.True(save >= 0 && runOnce >= 0 && stepB >= 0);
        Assert.True(save < stepB && runOnce < stepB, "state must be saved before the reboot step runs");
    }

    [Fact]
    public async Task Step_that_needs_a_reboot_restarts_the_machine_and_stops_the_run()
    {
        var fake = new FakeSystem();
        IRepairStep[] steps = [new MarkerStep("B", needsReboot: true), new MarkerStep("C")];

        await RunAsync(fake, steps);

        Assert.Contains("Reboot", fake.Calls);
        Assert.DoesNotContain("StartProcess step-C", fake.Calls);
        Assert.Contains(StatePath, fake.Files.Keys);
    }

    [Fact]
    public async Task Resume_continues_from_the_saved_stage_and_skips_steps_already_done()
    {
        var fake = new FakeSystem();
        fake.Files[StatePath] = new RunState(2).ToJson();
        IRepairStep[] steps = [new MarkerStep("A"), new MarkerStep("B", needsReboot: true), new MarkerStep("C")];

        await RunAsync(fake, steps, resume: true);

        Assert.DoesNotContain("StartProcess step-A", fake.Calls);
        Assert.DoesNotContain("StartProcess step-B", fake.Calls);
        Assert.Contains("StartProcess step-C", fake.Calls);
    }

    [Fact]
    public async Task Resume_without_a_state_file_runs_every_step()
    {
        var fake = new FakeSystem();
        IRepairStep[] steps = [new MarkerStep("A"), new MarkerStep("C")];

        await RunAsync(fake, steps, resume: true);

        Assert.Contains("StartProcess step-A", fake.Calls);
        Assert.Contains("StartProcess step-C", fake.Calls);
    }

    [Fact]
    public async Task Resume_with_an_unreadable_state_file_runs_every_step()
    {
        var fake = new FakeSystem();
        fake.Files[StatePath] = "{ not json";
        IRepairStep[] steps = [new MarkerStep("A")];

        await RunAsync(fake, steps, resume: true);

        Assert.Contains("StartProcess step-A", fake.Calls);
    }

    [Fact]
    public async Task Without_the_resume_switch_a_leftover_state_file_is_ignored()
    {
        var fake = new FakeSystem();
        fake.Files[StatePath] = new RunState(2).ToJson();
        IRepairStep[] steps = [new MarkerStep("A"), new MarkerStep("B"), new MarkerStep("C")];

        await RunAsync(fake, steps);

        Assert.Contains("StartProcess step-A", fake.Calls);
    }

    [Fact]
    public async Task Finished_run_fixed_by_a_step_deletes_the_state_file_and_the_RunOnce_value()
    {
        var fake = new FakeSystem { FixedByCall = "StartProcess step-C" };
        fake.Files[StatePath] = new RunState(2).ToJson();
        fake.RegistryValues[(RunOnceKey, "WinFix")] = "\"x.exe\" --resume";
        IRepairStep[] steps = [new MarkerStep("A"), new MarkerStep("B", needsReboot: true), new MarkerStep("C")];

        var (exit, output) = await RunAsync(fake, steps, resume: true);

        Assert.Equal(0, exit);
        Assert.Contains("Fixed by C", output);
        Assert.DoesNotContain(StatePath, fake.Files.Keys);
        Assert.DoesNotContain((RunOnceKey, "WinFix"), fake.RegistryValues.Keys);
    }

    [Fact]
    public async Task Finished_run_where_reset_starts_deletes_the_state_before_the_reboot()
    {
        var fake = new FakeSystem();
        fake.Files[StatePath] = new RunState(1).ToJson();
        fake.RegistryValues[(RunOnceKey, "WinFix")] = "\"x.exe\" --resume";

        await RunAsync(fake, [new MarkerStep("A")], resume: true);

        Assert.DoesNotContain(StatePath, fake.Files.Keys);
        Assert.DoesNotContain((RunOnceKey, "WinFix"), fake.RegistryValues.Keys);
        var cleanup = IndexOf(fake, $"DeleteFile {StatePath}");
        var reboot = IndexOf(fake, "Reboot");
        Assert.True(cleanup >= 0 && reboot > cleanup, "cleanup must happen before the reset reboot");
    }

    [Fact]
    public async Task Healthy_shell_run_also_cleans_up()
    {
        var fake = new FakeSystem { ShellHealthy = true };
        fake.Files[StatePath] = new RunState(1).ToJson();

        await RunAsync(fake, [new MarkerStep("A")], resume: true);

        Assert.DoesNotContain(StatePath, fake.Files.Keys);
        Assert.Contains(fake.Calls, c => c.StartsWith($"DeleteRegistryValue {RunOnceKey} WinFix"));
    }

    [Fact]
    public async Task Program_resume_switch_skips_the_steps_already_done()
    {
        var fake = new FakeSystem();
        fake.Files[StatePath] = new RunState(1).ToJson();
        var output = new StringWriter();

        var exit = await Program.RunAsync(["--dry-run", "--resume"], output, system: fake);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Running: Restart Explorer", output.ToString());
        Assert.Contains("Running: Clear IrisService", output.ToString());
    }
}
