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

    public async Task<int> RunAsync(CancellationToken ct)
    {
        output.WriteLine("[1/4] Diagnose");
        var diagnosis = await _healthCheck.CheckAsync(ct);
        output.WriteLine(diagnosis.Reason);
        if (diagnosis.Healthy)
        {
            output.WriteLine("Shell is healthy — nothing to do");
            return 0;
        }

        output.WriteLine("[2/4] Repairs");
        foreach (var step in _steps)
        {
            await RunStepAsync(step, ct);
            var health = await _healthCheck.CheckAsync(ct);
            output.WriteLine(health.Reason);
            if (health.Healthy)
            {
                var fixedBy = $"Fixed by {step.Name}";
                system.Log(fixedBy);
                output.WriteLine(fixedBy);
                return 0;
            }
        }

        output.WriteLine("[3/4] Confirm reset");

        output.WriteLine("[4/4] Reset");
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
