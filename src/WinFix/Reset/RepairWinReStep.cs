using System.Text.RegularExpressions;
using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Reset;

public enum WinReStatus
{
    Unknown,
    Enabled,
    Disabled,
}

/// <summary>Makes sure the Windows recovery environment is on; a reset cannot run without it.</summary>
public sealed partial class RepairWinReStep : IRepairStep
{
    public string Name => "Repair recovery environment";

    public static WinReStatus ParseStatus(string reagentcInfo)
    {
        var match = StatusLine().Match(reagentcInfo);
        if (!match.Success)
        {
            return WinReStatus.Unknown;
        }

        return match.Groups[1].Value.ToLowerInvariant() switch
        {
            "enabled" => WinReStatus.Enabled,
            "disabled" => WinReStatus.Disabled,
            _ => WinReStatus.Unknown,
        };
    }

    public async Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
    {
        var status = await ReadStatusAsync(system, ct);
        if (status == WinReStatus.Enabled)
        {
            return new StepResult(0, "The recovery environment is on.");
        }

        if (status == WinReStatus.Disabled)
        {
            var enable = await system.RunCommandAsync("reagentc", "/enable", ct);
            if (enable.ExitCode != 0)
            {
                return Fail(system, enable.ExitCode, $"reagentc /enable failed (exit code {enable.ExitCode}).");
            }

            if (await ReadStatusAsync(system, ct) == WinReStatus.Enabled)
            {
                return new StepResult(0, "The recovery environment was turned on.");
            }
        }

        return Fail(system, 1, "The Windows recovery environment (WinRE) is still not enabled.");
    }

    private static async Task<WinReStatus> ReadStatusAsync(ISystem system, CancellationToken ct)
    {
        var info = await system.RunCommandAsync("reagentc", "/info", ct);
        return info.ExitCode == 0 ? ParseStatus(info.Output) : WinReStatus.Unknown;
    }

    private static StepResult Fail(ISystem system, int exitCode, string message)
    {
        system.Log(message);
        return new StepResult(exitCode, message);
    }

    [GeneratedRegex(@"Windows RE status:\s*(\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex StatusLine();
}
