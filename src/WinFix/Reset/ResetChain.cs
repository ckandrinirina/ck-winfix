using WinFix.Media;
using WinFix.System;

namespace WinFix.Reset;

/// <summary>AttemptName is the attempt that started; null when every attempt failed. ExitCode is 0 when one started, else the code the caller exits with.</summary>
public sealed record ResetChainResult(bool Started, string? AttemptName, int ExitCode = 0);

/// <summary>Runs attempts in order and stops at the first that starts a reset; a failure is logged and the next attempt runs.</summary>
public sealed class ResetChain(IReadOnlyList<IResetAttempt> attempts)
{
    /// <summary>The attempts in order, least destructive first: in-place reinstall, then the WinRE repair and keep-files reset, then a reboot into Windows Recovery.</summary>
    public static IReadOnlyList<IResetAttempt> DefaultAttempts(
        Func<ISystem, CancellationToken, Task<InstallMedia?>> findMedia,
        Func<ISystem, CancellationToken, Task<InstallMedia?>> downloadMedia,
        TextWriter output) =>
        [new InPlaceReinstallAttempt(findMedia, downloadMedia), new MdmKeepFilesReset(), new BootToWinReAttempt(output)];

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

        return new ResetChainResult(false, null, BootToWinReAttempt.FailureExitCode);
    }
}
