using WinFix.Diagnose;
using WinFix.System;

namespace WinFix.Pipeline;

/// <summary>Diagnose, then repairs, reset confirmation and reset; repairs and reset are placeholders for now.</summary>
public sealed class FixPipeline(ISystem system, TextWriter output, ShellHealthCheck? healthCheck = null)
{
    private readonly ShellHealthCheck _healthCheck = healthCheck ?? new ShellHealthCheck(system);

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
        await system.RunCommandAsync("sfc.exe", "/scannow", ct);

        var recheck = await _healthCheck.CheckAsync(ct);
        output.WriteLine(recheck.Reason);
        if (recheck.Healthy)
        {
            output.WriteLine("Shell is healthy after repair — nothing more to do");
            return 0;
        }

        output.WriteLine("[3/4] Confirm reset");

        output.WriteLine("[4/4] Reset");
        await system.RebootAsync(ct);
        return 0;
    }
}
