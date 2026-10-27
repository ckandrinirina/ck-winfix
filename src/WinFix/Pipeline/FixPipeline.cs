using WinFix.Diagnose;
using WinFix.Problems;
using WinFix.Repairs;
using WinFix.Reset;
using WinFix.System;

namespace WinFix.Pipeline;

/// <summary>
/// Detect every module, repair each unhealthy one cheapest-first (re-detect after each step), summarize, confirm reset
/// (exit 3 unless YES) only if a module is still broken, then run the reset chain. Without a chain the reset is simulated by a reboot.
/// </summary>
public sealed class FixPipeline(
    ISystem system,
    TextWriter output,
    ShellHealthCheck? healthCheck = null,
    IReadOnlyList<IRepairStep>? steps = null,
    Func<string?>? readLine = null,
    bool dryRun = false,
    IReadOnlyList<IProblemModule>? modules = null,
    string? only = null,
    ResetChain? resetChain = null)
{
    private const int UsageExitCode = 2;

    private readonly IReadOnlyList<IProblemModule> _modules = modules ?? [new ShellProblem(healthCheck, steps)];

    private const string StillBroken = "Still broken";

    private sealed record Outcome(string Summary, bool Rebooting = false);

    public async Task<int> RunAsync(CancellationToken ct, bool resume = false)
    {
        var selected = _modules.Where(m => only is null || m.Name.Equals(only, StringComparison.OrdinalIgnoreCase)).ToList();
        if (selected.Count == 0)
        {
            output.WriteLine($"Unknown module: {only}. Known modules: {string.Join(", ", _modules.Select(m => m.Name))}");
            return UsageExitCode;
        }

        var (resumeModule, resumeStep) = FindResumePoint(selected, resume ? RunState.Load(system) : null);

        output.WriteLine("[1/4] Diagnose");
        var health = new List<Health>();
        foreach (var module in selected)
        {
            var detected = await module.DetectAsync(system, ct);
            output.WriteLine(detected.Reason);
            if (detected.Healthy)
            {
                output.WriteLine($"{Display(module)} is healthy — nothing to do");
            }

            health.Add(detected);
        }

        var outcomes = new List<Outcome>();
        if (health.Any(h => !h.Healthy))
        {
            output.WriteLine("[2/4] Repairs");
        }

        for (var i = 0; i < selected.Count; i++)
        {
            if (health[i].Healthy)
            {
                outcomes.Add(new Outcome("Healthy"));
            }
            else if (i < resumeModule)
            {
                outcomes.Add(new Outcome(StillBroken));
            }
            else
            {
                var outcome = await RepairAsync(selected[i], i == resumeModule ? resumeStep : 0, ct);
                if (outcome.Rebooting)
                {
                    return 0;
                }

                outcomes.Add(outcome);
            }
        }

        output.WriteLine("Summary");
        for (var i = 0; i < selected.Count; i++)
        {
            output.WriteLine($"{Display(selected[i])}: {outcomes[i].Summary}");
        }

        if (outcomes.All(o => o.Summary != StillBroken))
        {
            RunState.Clear(system);
            return 0;
        }

        output.WriteLine("[3/4] Confirm reset");
        var confirmed = new ResetConfirmation(output, readLine ?? Console.In.ReadLine, dryRun).Confirm();
        if (!confirmed)
        {
            system.Log("Reset declined by the user");
            RunState.Clear(system);
            output.WriteLine("Reset cancelled. Nothing was changed.");
            return 3;
        }

        output.WriteLine("[4/4] Reset");
        RunState.Clear(system);
        if (resetChain is null)
        {
            await system.RebootAsync(ct);
            return 0;
        }

        var reset = await resetChain.RunAsync(system, ct);
        if (reset.Started)
        {
            system.Log($"Reset started: {reset.AttemptName}");
            output.WriteLine($"Reset started: {reset.AttemptName}.");
            return 0;
        }

        output.WriteLine($"Every reset attempt failed. Details are in {ResetConfirmation.LogPath}");
        return reset.ExitCode;
    }

    private static string Display(IProblemModule module) => char.ToUpperInvariant(module.Name[0]) + module.Name[1..];

    /// <summary>A state saved before modules existed has no module name and belongs to the first one.</summary>
    private static (int Module, int Step) FindResumePoint(List<IProblemModule> modules, RunState? state)
    {
        if (state is null)
        {
            return (-1, 0);
        }

        var index = state.Module is null
            ? 0
            : modules.FindIndex(m => m.Name.Equals(state.Module, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? (-1, 0) : (index, state.NextStage);
    }

    private async Task<Outcome> RepairAsync(IProblemModule module, int firstStep, CancellationToken ct)
    {
        for (var index = firstStep; index < module.Repairs.Count; index++)
        {
            var step = module.Repairs[index];
            if (step.NeedsReboot)
            {
                new RunState(index + 1, module.Name).Save(system);
            }

            var result = await RunStepAsync(step, ct);
            if (step.NeedsReboot || result?.RebootRequired == true)
            {
                // a NeedsReboot step saved before it ran; a reboot decided by the result saves now
                if (!step.NeedsReboot)
                {
                    new RunState(index + 1, module.Name).Save(system);
                }

                output.WriteLine("Restarting to finish this repair. WinFix will continue by itself.");
                await system.RebootAsync(ct);
                return new Outcome("Rebooting", Rebooting: true);
            }

            var health = await module.DetectAsync(system, ct);
            output.WriteLine(health.Reason);
            if (health.Healthy)
            {
                var fixedBy = $"Fixed by {step.Name}";
                system.Log(fixedBy);
                output.WriteLine(fixedBy);
                return new Outcome(fixedBy);
            }
        }

        return new Outcome(StillBroken);
    }

    private async Task<StepResult?> RunStepAsync(IRepairStep step, CancellationToken ct)
    {
        output.WriteLine($"Running: {step.Name}");
        try
        {
            var result = await step.RunAsync(system, ct);
            system.Log($"Step {step.Name}: exit {result.ExitCode} — {result.Message}");
            output.WriteLine(result.Succeeded ? result.Message : $"{step.Name} failed (exit {result.ExitCode}). {result.Message}");
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            system.Log($"Step {step.Name} threw: {ex.Message}");
            output.WriteLine($"{step.Name} failed. {ex.Message}");
            return null;
        }
    }
}
