using WinFix.System;

namespace WinFix.Reset;

/// <summary>Started: the attempt kicked off a reset that restarts the PC. Otherwise Message says why it failed. ExitCode is the process exit code the failure asks for; 0 when it asks for none.</summary>
public sealed record ResetAttemptResult(bool Started, string Message, int ExitCode = 0);

public interface IResetAttempt
{
    string Name { get; }

    Task<ResetAttemptResult> RunAsync(ISystem system, CancellationToken ct);
}
