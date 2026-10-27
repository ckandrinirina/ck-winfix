using WinFix.System;

namespace WinFix.Repairs;

/// <summary>ExitCode 0 is success; anything else is logged and the next step still runs.</summary>
public sealed record StepResult(int ExitCode, string Message)
{
    public bool Succeeded => ExitCode == 0;
}

public interface IRepairStep
{
    string Name { get; }

    Task<StepResult> RunAsync(ISystem system, CancellationToken ct);
}
