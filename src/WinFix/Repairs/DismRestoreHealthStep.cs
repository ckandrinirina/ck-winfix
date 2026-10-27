using WinFix.System;

namespace WinFix.Repairs;

public sealed class DismRestoreHealthStep : IRepairStep
{
    public string Name => "Repair Windows image";

    public async Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
    {
        var result = await system.RunCommandAsync("DISM", "/Online /Cleanup-Image /RestoreHealth", ct);
        if (result.ExitCode == 0)
        {
            return new StepResult(0, "The Windows image was checked and repaired.");
        }

        var message = $"DISM could not repair the Windows image (exit code {result.ExitCode}).";
        system.Log(message);
        return new StepResult(result.ExitCode, message);
    }
}
