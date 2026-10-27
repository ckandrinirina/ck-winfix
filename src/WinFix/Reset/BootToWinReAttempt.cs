using WinFix.Pipeline;
using WinFix.System;

namespace WinFix.Reset;

/// <summary>
/// Last fallback: the user finishes "Keep my files" by hand in Windows Recovery. Prints the steps, points the
/// next boot at WinRE and restarts in 30 s. A failure carries <see cref="FailureExitCode"/> and never reboots.
/// </summary>
public sealed class BootToWinReAttempt(TextWriter output) : IResetAttempt
{
    public const int FailureExitCode = 4;

    private const string BootToRe = "/boottore";
    private const string RestartToRecovery = "/r /o /t 30";

    public string Name => "Boot to Windows Recovery";

    public async Task<ResetAttemptResult> RunAsync(ISystem system, CancellationToken ct)
    {
        output.WriteLine("The automatic reset did not work. Windows will restart into Windows Recovery.");
        output.WriteLine("There, choose these in order:");
        output.WriteLine("1. Troubleshoot");
        output.WriteLine("2. Reset this PC");
        output.WriteLine("3. Keep my files");

        var boot = await system.RunCommandAsync("reagentc", BootToRe, ct);
        if (boot.ExitCode != 0)
        {
            return Failed(system, $"reagentc /boottore failed with exit code {boot.ExitCode}.");
        }

        var restart = await system.RunCommandAsync("shutdown", RestartToRecovery, ct);
        return restart.ExitCode == 0
            ? new ResetAttemptResult(true, "The PC restarts into Windows Recovery in 30 seconds.")
            : Failed(system, $"shutdown failed with exit code {restart.ExitCode}.");
    }

    private ResetAttemptResult Failed(ISystem system, string message)
    {
        system.Log(message);
        output.WriteLine("WinFix could not restart into Windows Recovery.");
        output.WriteLine($"Details are in {ResetConfirmation.LogPath}");
        return new ResetAttemptResult(false, message, FailureExitCode);
    }
}
