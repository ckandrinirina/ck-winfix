using WinFix.Pipeline;
using WinFix.Problems;
using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Tests.Pipeline;

public class ProblemLoopTests
{
    private const string StatePath = @"%ProgramData%\WinFix\state.json";

    private sealed class FakeStep(string name, Action run, bool rebootRequired = false) : IRepairStep
    {
        public string Name => name;

        public Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
        {
            run();
            return Task.FromResult(new StepResult(0, $"{name} done.", rebootRequired));
        }
    }

    /// <summary>Broken until the step named fixedBy has run; a null fixedBy never heals.</summary>
    private sealed class FakeModule : IProblemModule
    {
        private readonly List<string> _events;
        private readonly bool _brokenAtStart;
        private bool _repaired;

        public FakeModule(List<string> events, string name, bool brokenAtStart, string? fixedBy, params string[] stepNames)
        {
            _events = events;
            Name = name;
            _brokenAtStart = brokenAtStart;
            Repairs = stepNames
                .Select(s => (IRepairStep)new FakeStep(s, () =>
                {
                    events.Add($"run {s}");
                    _repaired |= s == fixedBy;
                }))
                .ToList();
        }

        public string Name { get; }

        public IReadOnlyList<IRepairStep> Repairs { get; }

        public Task<Health> DetectAsync(ISystem system, CancellationToken ct)
        {
            _events.Add($"detect {Name}");
            var healthy = !_brokenAtStart || _repaired;
            return Task.FromResult(new Health(healthy, healthy ? $"{Name} looks fine." : $"{Name} is broken."));
        }
    }

    /// <summary>Never heals; repairs are whatever steps the test hands in.</summary>
    private sealed class StubbornModule(List<string> events, string name, params IRepairStep[] steps) : IProblemModule
    {
        public string Name => name;

        public IReadOnlyList<IRepairStep> Repairs { get; } = steps;

        public Task<Health> DetectAsync(ISystem system, CancellationToken ct)
        {
            events.Add($"detect {name}");
            return Task.FromResult(new Health(false, $"{name} is broken."));
        }
    }

    private static async Task<(int Exit, string Output, FakeSystem Fake)> RunAsync(
        IReadOnlyList<IProblemModule> modules, string? only = null, string? answer = "YES", bool resume = false, FakeSystem? fake = null)
    {
        fake ??= new FakeSystem();
        var output = new StringWriter();
        var exit = await new FixPipeline(fake, output, readLine: () => answer, modules: modules, only: only)
            .RunAsync(CancellationToken.None, resume);
        return (exit, output.ToString(), fake);
    }

    [Fact]
    public async Task Every_module_is_detected_then_each_unhealthy_one_is_repaired_and_re_detected_after_each_step()
    {
        var events = new List<string>();
        var alpha = new FakeModule(events, "alpha", brokenAtStart: true, fixedBy: "a2", "a1", "a2", "a3");
        var beta = new FakeModule(events, "beta", brokenAtStart: true, fixedBy: null, "b1", "b2");

        await RunAsync([alpha, beta], answer: "no");

        string[] expected =
        [
            "detect alpha", "detect beta",
            "run a1", "detect alpha", "run a2", "detect alpha",
            "run b1", "detect beta", "run b2", "detect beta",
        ];
        Assert.Equal(expected, events);
    }

    [Fact]
    public async Task A_healthy_module_runs_no_repairs()
    {
        var events = new List<string>();
        var alpha = new FakeModule(events, "alpha", brokenAtStart: false, fixedBy: null, "a1");

        var (exit, output, _) = await RunAsync([alpha]);

        Assert.Equal(0, exit);
        Assert.Equal(["detect alpha"], events);
        Assert.DoesNotContain("Repairs", output);
    }

    [Fact]
    public async Task Summary_prints_one_line_per_module_healthy_fixed_or_still_broken()
    {
        var events = new List<string>();
        var alpha = new FakeModule(events, "alpha", brokenAtStart: false, fixedBy: null, "a1");
        var beta = new FakeModule(events, "beta", brokenAtStart: true, fixedBy: "b2", "b1", "b2");
        var gamma = new FakeModule(events, "gamma", brokenAtStart: true, fixedBy: null, "g1");

        var (_, output, _) = await RunAsync([alpha, beta, gamma], answer: "no");

        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        Assert.Contains("Alpha: Healthy", lines);
        Assert.Contains("Beta: Fixed by b2", lines);
        Assert.Contains("Gamma: Still broken", lines);
    }

    [Fact]
    public async Task Reset_confirmation_is_not_shown_when_every_module_is_healthy_or_fixed()
    {
        var events = new List<string>();
        var alpha = new FakeModule(events, "alpha", brokenAtStart: false, fixedBy: null, "a1");
        var beta = new FakeModule(events, "beta", brokenAtStart: true, fixedBy: "b1", "b1");

        var (exit, output, fake) = await RunAsync([alpha, beta], answer: "no");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Confirm reset", output);
        Assert.DoesNotContain(fake.Calls, c => c == "Reboot");
    }

    [Fact]
    public async Task Reset_confirmation_is_shown_when_one_module_is_still_broken_even_if_another_was_fixed()
    {
        var events = new List<string>();
        var alpha = new FakeModule(events, "alpha", brokenAtStart: true, fixedBy: "a1", "a1");
        var beta = new FakeModule(events, "beta", brokenAtStart: true, fixedBy: null, "b1");

        var (exit, output, fake) = await RunAsync([alpha, beta], answer: "no");

        Assert.Equal(3, exit);
        Assert.Contains("Confirm reset", output);
        Assert.DoesNotContain(fake.Calls, c => c == "Reboot");
    }

    [Fact]
    public async Task Only_runs_the_named_module_matched_case_insensitively()
    {
        var events = new List<string>();
        var alpha = new FakeModule(events, "alpha", brokenAtStart: true, fixedBy: "a1", "a1");
        var beta = new FakeModule(events, "beta", brokenAtStart: true, fixedBy: "b1", "b1");

        var (exit, output, _) = await RunAsync([alpha, beta], only: "BETA");

        Assert.Equal(0, exit);
        Assert.Equal(["detect beta", "run b1", "detect beta"], events);
        Assert.DoesNotContain("Alpha", output);
    }

    [Fact]
    public async Task Unknown_module_name_lists_the_known_names_and_exits_nonzero_without_detecting()
    {
        var events = new List<string>();
        var alpha = new FakeModule(events, "alpha", brokenAtStart: true, fixedBy: null, "a1");
        var beta = new FakeModule(events, "beta", brokenAtStart: true, fixedBy: null, "b1");

        var (exit, output, _) = await RunAsync([alpha, beta], only: "nope");

        Assert.NotEqual(0, exit);
        Assert.Contains("nope", output);
        Assert.Contains("alpha", output);
        Assert.Contains("beta", output);
        Assert.Empty(events);
    }

    [Fact]
    public async Task Reboot_in_a_later_module_saves_that_module_and_step_and_resume_skips_earlier_repairs()
    {
        var events = new List<string>();
        var alpha = new StubbornModule(events, "alpha", new FakeStep("a1", () => events.Add("run a1")));
        var beta = new StubbornModule(
            events, "beta",
            new FakeStep("b1", () => events.Add("run b1"), rebootRequired: true),
            new FakeStep("b2", () => events.Add("run b2")));
        var fake = new FakeSystem();

        await RunAsync([alpha, beta], fake: fake);

        var saved = RunState.Parse(fake.Files[StatePath])!;
        Assert.Equal(1, saved.NextStage);
        Assert.Equal("beta", saved.Module);
        Assert.Contains(fake.Calls, c => c == "Reboot");

        events.Clear();
        var (exit, _, _) = await RunAsync([alpha, beta], answer: "no", resume: true, fake: fake);

        Assert.Equal(3, exit);
        Assert.DoesNotContain("run a1", events);
        Assert.DoesNotContain("run b1", events);
        Assert.Contains("run b2", events);
    }

    [Fact]
    public async Task Program_only_shell_runs_just_the_shell_module()
    {
        var output = new StringWriter();

        var exit = await Program.RunAsync(["--dry-run", "--only", "shell"], output);

        Assert.Equal(0, exit);
        Assert.Contains("Shell: Still broken", output.ToString());
    }

    [Fact]
    public async Task Program_only_with_an_unknown_module_prints_the_known_names_and_exits_nonzero()
    {
        var output = new StringWriter();

        var exit = await Program.RunAsync(["--dry-run", "--only", "nope"], output);

        Assert.NotEqual(0, exit);
        Assert.Contains("shell", output.ToString());
        Assert.DoesNotContain("Diagnose", output.ToString());
    }

    [Fact]
    public async Task Program_only_without_a_name_exits_nonzero()
    {
        var output = new StringWriter();

        var exit = await Program.RunAsync(["--dry-run", "--only"], output);

        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("Diagnose", output.ToString());
    }

    [Fact]
    public async Task Shell_problem_is_named_shell_and_detects_through_the_health_check()
    {
        var module = new ShellProblem();

        var broken = await module.DetectAsync(new FakeSystem(), CancellationToken.None);
        var healthy = await module.DetectAsync(new FakeSystem { ShellHealthy = true }, CancellationToken.None);

        Assert.Equal("shell", module.Name);
        Assert.False(broken.Healthy);
        Assert.Contains("Shell_TrayWnd", broken.Reason);
        Assert.True(healthy.Healthy);
    }

    [Fact]
    public void Shell_problem_repairs_run_cheapest_first_ending_with_the_shell_extensions()
    {
        var types = new ShellProblem().Repairs.Select(r => r.GetType()).ToArray();

        Assert.Equal(
            [typeof(RestartExplorerStep), typeof(ClearIrisServiceStep), typeof(ReRegisterShellAppsStep),
             typeof(DismRestoreHealthStep), typeof(SfcScanStep), typeof(ShellExtensionsStep)],
            types);
    }
}
