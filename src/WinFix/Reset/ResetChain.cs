using WinFix.System;

namespace WinFix.Reset;

/// <summary>AttemptName is the attempt that started; null when every attempt failed.</summary>
public sealed record ResetChainResult(bool Started, string? AttemptName);

/// <summary>Runs attempts in order and stops at the first that starts a reset; a failure is logged and the next attempt runs.</summary>
public sealed class ResetChain(IReadOnlyList<IResetAttempt> attempts)
{
    public async Task<ResetChainResult> RunAsync(ISystem system, CancellationToken ct)
    {
        foreach (var attempt in attempts)
        {
            ResetAttemptResult result;
            try
            {
                result = await attempt.RunAsync(system, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = new ResetAttemptResult(false, ex.Message);
            }

            if (result.Started)
            {
                return new ResetChainResult(true, attempt.Name);
            }

            system.Log($"Reset attempt \"{attempt.Name}\" failed: {result.Message}");
        }

        return new ResetChainResult(false, null);
    }
}
