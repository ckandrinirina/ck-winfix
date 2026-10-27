using WinFix.System;

namespace WinFix.Repairs;

/// <summary>Flushes the DNS cache and resets Winsock and the IP stack; the resets only take effect after a restart.</summary>
public sealed class ResetNetworkStackStep : IRepairStep
{
    private static readonly (string FileName, string Arguments)[] Commands =
    [
        ("ipconfig", "/flushdns"),
        ("netsh", "winsock reset"),
        ("netsh", "int ip reset"),
    ];

    public string Name => "Reset the network stack";

    public async Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
    {
        StepResult? failure = null;
        foreach (var (fileName, arguments) in Commands)
        {
            var result = await system.RunCommandAsync(fileName, arguments, ct);
            if (result.ExitCode != 0 && failure is null)
            {
                failure = new StepResult(result.ExitCode, $"{fileName} {arguments} failed: {result.Output.Trim()}", RebootRequired: true);
            }
        }

        return failure ?? new StepResult(0, "The network stack was reset. Windows must restart.", RebootRequired: true);
    }
}
