using WinFix.System;

namespace WinFix.Repairs;

/// <summary>ExitCode 0 is success; anything else is logged and the next step still runs. RebootRequired: the pipeline saves progress and restarts.</summary>
public sealed record StepResult(int ExitCode, string Message, bool RebootRequired = false)
{
    public bool Succeeded => ExitCode == 0;
}

public interface IRepairStep
{
    string Name { get; }

    /// <summary>True when the repair only takes effect after a restart; the pipeline saves progress first.</summary>
    bool NeedsReboot => false;

    Task<StepResult> RunAsync(ISystem system, CancellationToken ct);
}
