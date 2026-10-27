using WinFix.System;

namespace WinFix.Reset;

/// <summary>Started: the attempt kicked off a reset that restarts the PC. Otherwise Message says why it failed.</summary>
public sealed record ResetAttemptResult(bool Started, string Message);

public interface IResetAttempt
{
    string Name { get; }

    Task<ResetAttemptResult> RunAsync(ISystem system, CancellationToken ct);
}
