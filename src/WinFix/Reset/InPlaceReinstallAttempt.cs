using WinFix.Media;
using WinFix.System;

namespace WinFix.Reset;

/// <summary>
/// In-place upgrade from install media: keeps files and apps. Media comes from the finder, else the downloader.
/// Setup exit 0 means the upgrade launched and the PC reboots on its own; any other code fails the attempt.
/// </summary>
public sealed class InPlaceReinstallAttempt(
    Func<ISystem, CancellationToken, Task<InstallMedia?>> findMedia,
    Func<ISystem, CancellationToken, Task<InstallMedia?>> downloadMedia) : IResetAttempt
{
    private const string SetupArguments = "/auto upgrade /eula accept /dynamicupdate disable";

    public string Name => "In-place reinstall (keeps files and apps)";

    public async Task<ResetAttemptResult> RunAsync(ISystem system, CancellationToken ct)
    {
        var media = await findMedia(system, ct) ?? await downloadMedia(system, ct);
        if (media is null)
        {
            return Failed(system, "No Windows install media found.");
        }

        // The media may be a drive or ISO the user does not control, so its path is checked before it is run.
        var setup = media.Root.TrimEnd('\\') + @"\setup.exe";
        if (!CommandArgs.CanQuoteCmd(setup))
        {
            return Failed(system, $"The install media path {setup} holds a character that cannot be passed to a command safely.");
        }

        var result = await system.RunCommandAsync(setup, SetupArguments, ct);
        return result.ExitCode == 0
            ? new ResetAttemptResult(true, "Windows setup started the in-place reinstall.")
            : Failed(system, $"Windows setup failed with exit code 0x{unchecked((uint)result.ExitCode):X8}.");
    }

    private static ResetAttemptResult Failed(ISystem system, string message)
    {
        system.Log(message);
        return new ResetAttemptResult(false, message);
    }
}
