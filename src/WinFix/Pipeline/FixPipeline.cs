using WinFix.System;

namespace WinFix.Pipeline;

/// <summary>Diagnose, then repairs, reset confirmation and reset; stages are placeholders for now.</summary>
public sealed class FixPipeline(ISystem system, TextWriter output)
{
    private const string TaskbarWindowClass = "Shell_TrayWnd";

    public async Task<int> RunAsync(CancellationToken ct)
    {
        output.WriteLine("[1/4] Diagnose");
        if (system.FindWindow(TaskbarWindowClass))
        {
            output.WriteLine("Shell is healthy — nothing to do");
            return 0;
        }

        output.WriteLine("[2/4] Repairs");
        await system.RunCommandAsync("sfc.exe", "/scannow", ct);

        output.WriteLine("[3/4] Confirm reset");

        output.WriteLine("[4/4] Reset");
        await system.RebootAsync(ct);
        return 0;
    }
}
