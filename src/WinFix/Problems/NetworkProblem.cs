using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Problems;

/// <summary>Broken network stack: an adapter is up but the name cannot be resolved and the host cannot be reached.</summary>
public sealed class NetworkProblem : IProblemModule
{
    private const string Host = "www.microsoft.com";

    private const string AdapterScript = "@(Get-NetAdapter | Where-Object Status -eq 'Up').Count";

    private const string DnsScript =
        $"try {{ [void](Resolve-DnsName {Host} -ErrorAction Stop); 'True' }} catch {{ 'False' }}";

    private const string TcpScript =
        $"Test-NetConnection {Host} -Port 443 -InformationLevel Quiet -WarningAction SilentlyContinue";

    public string Name => "network";

    public IReadOnlyList<IRepairStep> Repairs { get; } = [new ResetNetworkStackStep()];

    public async Task<Health> DetectAsync(ISystem system, CancellationToken ct)
    {
        var adapters = await RunScriptAsync(system, AdapterScript, ct);
        if (adapters.ExitCode == 0 && int.TryParse(adapters.Output.Trim(), out var up) && up == 0)
        {
            // Nothing to reset: reported as healthy so no repair runs.
            return new Health(true, "No network adapter connected");
        }

        var dns = await RunScriptAsync(system, DnsScript, ct);
        var tcp = await RunScriptAsync(system, TcpScript, ct);

        // Only a probe that ran and said False counts as a failure.
        return Failed(dns) && Failed(tcp)
            ? new Health(false, $"A network adapter is up but {Host} cannot be resolved or reached.")
            : new Health(true, "The network looks healthy.");
    }

    private static bool Failed(CommandResult probe) =>
        probe.ExitCode == 0 && probe.Output.Trim().Equals("False", StringComparison.OrdinalIgnoreCase);

    private static Task<CommandResult> RunScriptAsync(ISystem system, string script, CancellationToken ct) =>
        system.RunCommandAsync("powershell", $"-NoProfile -NonInteractive -Command \"{script}\"", ct);
}
