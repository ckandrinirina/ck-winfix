using WinFix.System;

namespace WinFix.Repairs;

public sealed class ChkdskStep : IRepairStep
{
    public string Name => "Schedule disk check";

    public async Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
    {
        // the volume is in use, so chkdsk asks to run at the next restart; the piped Y answers it
        var result = await system.RunCommandAsync("cmd", "/c echo Y| chkdsk C: /f", ct);
        if (result.ExitCode == 0)
        {
            return new StepResult(0, "The disk check is scheduled. Windows must restart.", RebootRequired: true);
        }

        var message = $"The disk check could not be scheduled (exit code {result.ExitCode}).";
        system.Log(message);
        return new StepResult(result.ExitCode, message);
    }
}
