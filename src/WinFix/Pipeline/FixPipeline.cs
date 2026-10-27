using WinFix.Diagnose;
using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Pipeline;

/// <summary>Diagnose, repairs cheapest-first (health check after each), reset confirmation, reset; reset is a placeholder.</summary>
public sealed class FixPipeline(
    ISystem system,
    TextWriter output,
    ShellHealthCheck? healthCheck = null,
    IReadOnlyList<IRepairStep>? steps = null)
{
    private readonly ShellHealthCheck _healthCheck = healthCheck ?? new ShellHealthCheck(system);

    private readonly IReadOnlyList<IRepairStep> _steps =
        steps ?? [new RestartExplorerStep(), new ClearIrisServiceStep(), new ReRegisterShellAppsStep()];

    public async Task<int> RunAsync(CancellationToken ct, bool resume = false)
    {
        var firstStage = resume ? RunState.Load(system)?.NextStage ?? 0 : 0;
        output.WriteLine("[1/4] Diagnose");
        var diagnosis = await _healthCheck.CheckAsync(ct);
        output.WriteLine(diagnosis.Reason);
        if (diagnosis.Healthy)
        {
            output.WriteLine("Shell is healthy — nothing to do");
            RunState.Clear(system);
            return 0;
        }

        output.WriteLine("[2/4] Repairs");
        for (var index = firstStage; index < _steps.Count; index++)
        {
            var step = _steps[index];
            if (step.NeedsReboot)
            {
                new RunState(index + 1).Save(system);
            }

            await RunStepAsync(step, ct);
            if (step.NeedsReboot)
            {
                output.WriteLine("Restarting to finish this repair. WinFix will continue by itself.");
                await system.RebootAsync(ct);
                return 0;
            }

            var health = await _healthCheck.CheckAsync(ct);
            output.WriteLine(health.Reason);
            if (health.Healthy)
            {
                var fixedBy = $"Fixed by {step.Name}";
                system.Log(fixedBy);
                output.WriteLine(fixedBy);
                RunState.Clear(system);
                return 0;
            }
        }

        output.WriteLine("[3/4] Confirm reset");

        output.WriteLine("[4/4] Reset");
        RunState.Clear(system);
        await system.RebootAsync(ct);
        return 0;
    }

    private async Task RunStepAsync(IRepairStep step, CancellationToken ct)
    {
        output.WriteLine($"Running: {step.Name}");
        try
        {
            var result = await step.RunAsync(system, ct);
            system.Log($"Step {step.Name}: exit {result.ExitCode} — {result.Message}");
            output.WriteLine(result.Succeeded ? result.Message : $"{step.Name} failed (exit {result.ExitCode}). {result.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            system.Log($"Step {step.Name} threw: {ex.Message}");
            output.WriteLine($"{step.Name} failed. {ex.Message}");
        }
    }
}
