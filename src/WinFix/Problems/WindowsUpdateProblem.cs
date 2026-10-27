using System.Text.RegularExpressions;
using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Problems;

/// <summary>Broken Windows Update: a disabled update service, or repeated install failures.</summary>
public sealed partial class WindowsUpdateProblem : IProblemModule
{
    private const int FailureThreshold = 3;
    private const int DisabledStartType = 4;

    // 14 days in ms; event 20 of the WindowsUpdateClient provider is "Installation Failure".
    private const string FailureQuery =
        "*[System[Provider[@Name='Microsoft-Windows-WindowsUpdateClient'] and (EventID=20) and TimeCreated[timediff(@SystemTime) <= 1209600000]]]";

    private static readonly string[] Services = ["wuauserv", "bits", "cryptsvc"];

    public string Name => "windowsupdate";

    public IReadOnlyList<IRepairStep> Repairs { get; } = [new ResetUpdateComponentsStep()];

    public async Task<Health> DetectAsync(ISystem system, CancellationToken ct)
    {
        foreach (var service in Services)
        {
            var config = await system.RunCommandAsync("sc", $"qc {service}", ct);
            var startType = StartTypePattern().Match(config.Output);
            if (config.ExitCode == 0 && startType.Success && int.Parse(startType.Groups[1].Value) == DisabledStartType)
            {
                return new Health(false, $"The {service} service is disabled.");
            }
        }

        var events = await system.RunCommandAsync("wevtutil", $"qe System /q:\"{FailureQuery}\" /f:text", ct);
        var failures = events.ExitCode == 0 ? FailureEventPattern().Count(events.Output) : 0;
        return failures >= FailureThreshold
            ? new Health(false, $"Windows Update failed to install {failures} times in the last 14 days.")
            : new Health(true, "Windows Update looks healthy.");
    }

    [GeneratedRegex(@"START_TYPE\s*:\s*(\d+)")]
    private static partial Regex StartTypePattern();

    [GeneratedRegex(@"^\s*Event ID:\s*20\s*$", RegexOptions.Multiline)]
    private static partial Regex FailureEventPattern();
}
