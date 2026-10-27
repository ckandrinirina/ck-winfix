using System.Globalization;
using System.Text.RegularExpressions;
using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Reset;

/// <summary>
/// Reset keeping personal files: WMI MDM_RemoteWipe.doWipePersistUserDataMethod, run as SYSTEM through a
/// one-shot scheduled task (admin alone is refused). The remove-everything method is never used.
/// A started reset reboots the PC and ends this process, so the only result ever returned is a failure.
/// </summary>
public sealed partial class MdmKeepFilesReset(IRepairStep? repairWinRe = null, TimeSpan? timeout = null) : IResetAttempt
{
    private const string TaskName = "WinFixReset";
    private const int TaskRunning = 267009;
    private const int TaskNotYetRun = 267011;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private const string WmiScript =
        @"try { $i = Get-CimInstance -Namespace Root\CIMv2\MDM\DMMap -ClassName MDM_RemoteWipe | Select-Object -First 1; "
        + "$r = Invoke-CimMethod -InputObject $i -MethodName doWipePersistUserDataMethod -Arguments @{param=''} -ErrorAction Stop; "
        + "exit [int]$r.ReturnValue } catch { exit 1 }";

    private const string TaskCommand = $"powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \\\"{WmiScript}\\\"";

    private readonly IRepairStep _repairWinRe = repairWinRe ?? new RepairWinReStep();
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromMinutes(10);

    public string Name => "Reset keeping files (MDM)";

    public async Task<ResetAttemptResult> RunAsync(ISystem system, CancellationToken ct)
    {
        var winRe = await _repairWinRe.RunAsync(system, ct);
        if (!winRe.Succeeded)
        {
            system.Log($"Continuing the reset although the recovery environment is not ready: {winRe.Message}");
        }

        var create = await system.RunCommandAsync(
            "schtasks", $"/Create /TN {TaskName} /RU SYSTEM /SC ONCE /ST 00:00 /RL HIGHEST /F /TR \"{TaskCommand}\"", ct);
        if (create.ExitCode != 0)
        {
            return Failed(system, $"Could not create the reset task (exit code {create.ExitCode}).");
        }

        var run = await system.RunCommandAsync("schtasks", $"/Run /TN {TaskName}", ct);
        if (run.ExitCode != 0)
        {
            return await CleanUpAsync(system, $"Could not start the reset task (exit code {run.ExitCode}).", ct);
        }

        for (var waited = TimeSpan.Zero; waited < _timeout; waited += PollInterval)
        {
            await system.DelayAsync(PollInterval, ct);
            var query = await system.RunCommandAsync("schtasks", $"/Query /TN {TaskName} /FO LIST /V", ct);
            var lastResult = ParseLastResult(query.Output);
            if (lastResult is not (null or 0 or TaskRunning or TaskNotYetRun))
            {
                return await CleanUpAsync(system, $"The reset task failed (result {lastResult}).", ct);
            }
        }

        return await CleanUpAsync(system, "The PC did not reboot within the timeout.", ct);
    }

    private static int? ParseLastResult(string output)
    {
        var match = LastResultLine().Match(output);
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    private static async Task<ResetAttemptResult> CleanUpAsync(ISystem system, string message, CancellationToken ct)
    {
        await system.RunCommandAsync("schtasks", $"/Delete /TN {TaskName} /F", ct);
        return Failed(system, message);
    }

    private static ResetAttemptResult Failed(ISystem system, string message)
    {
        system.Log(message);
        return new ResetAttemptResult(false, message);
    }

    [GeneratedRegex(@"Last Result:\s*(-?\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex LastResultLine();
}
