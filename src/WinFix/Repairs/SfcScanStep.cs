using WinFix.System;

namespace WinFix.Repairs;

public enum SfcOutcome
{
    Unknown,
    NoViolations,
    Repaired,
    Unrepaired,
    CouldNotRun,
}

public sealed class SfcScanStep : IRepairStep
{
    public string Name => "Scan system files";

    /// <summary>sfc prints UTF-16, so redirected text can carry NUL characters; they are ignored.</summary>
    public static SfcOutcome Parse(string output)
    {
        var text = output.Replace("\0", string.Empty);
        if (text.Contains("did not find any integrity violations", StringComparison.OrdinalIgnoreCase))
        {
            return SfcOutcome.NoViolations;
        }

        if (text.Contains("unable to fix", StringComparison.OrdinalIgnoreCase))
        {
            return SfcOutcome.Unrepaired;
        }

        if (text.Contains("found corrupt files and", StringComparison.OrdinalIgnoreCase)
            && text.Contains("repaired them", StringComparison.OrdinalIgnoreCase))
        {
            return SfcOutcome.Repaired;
        }

        return text.Contains("could not perform the requested operation", StringComparison.OrdinalIgnoreCase)
            ? SfcOutcome.CouldNotRun
            : SfcOutcome.Unknown;
    }

    public async Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
    {
        var result = await system.RunCommandAsync("sfc", "/scannow", ct);
        var failureCode = result.ExitCode != 0 ? result.ExitCode : 1;
        return Parse(result.Output) switch
        {
            SfcOutcome.Repaired => new StepResult(0, "Damaged system files were repaired. Windows must restart.", RebootRequired: true),
            SfcOutcome.NoViolations => new StepResult(0, "No damaged system files were found."),
            SfcOutcome.Unrepaired => new StepResult(failureCode, "Damaged system files were found but some could not be repaired."),
            SfcOutcome.CouldNotRun => new StepResult(failureCode, "The system file scan could not run."),
            _ => new StepResult(result.ExitCode, result.ExitCode == 0 ? "The system file scan finished." : "The system file scan failed."),
        };
    }
}
