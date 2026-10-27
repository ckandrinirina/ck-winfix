using System.Globalization;
using System.Text.RegularExpressions;
using WinFix.Problems;
using WinFix.System;

namespace WinFix.Repairs;

/// <summary>Removes third-party drivers installed in the 3 days before the first crash. Microsoft-provided drivers are never touched.</summary>
public sealed partial class RollbackDriverStep(Func<DateTimeOffset>? clock = null) : IRepairStep
{
    private const int RebootRequiredExit = 3010;

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);

    public string Name => "Roll back recent drivers";

    public async Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
    {
        var crashes = await CrashHistory.ReadAsync(system, _clock(), ct);
        if (crashes.Count == 0)
        {
            return new StepResult(0, "No recent crash was found, so no driver was removed.");
        }

        var listing = await system.RunCommandAsync("pnputil", "/enum-drivers", ct);
        if (listing.ExitCode != 0)
        {
            return new StepResult(listing.ExitCode, "The list of installed drivers could not be read.");
        }

        var firstCrash = crashes[0].Time;
        var removed = new List<string>();
        var failure = 0;
        foreach (var driver in ParseThirdParty(listing.Output))
        {
            var installed = await InstallTimeAsync(system, driver.PublishedName, ct);
            if (installed is null || installed < firstCrash - CrashHistory.CauseWindow || installed > firstCrash)
            {
                continue;
            }

            var result = await system.RunCommandAsync("pnputil", $"/delete-driver {driver.PublishedName} /uninstall", ct);
            if (result.ExitCode is 0 or RebootRequiredExit)
            {
                system.Log($"Removed driver {driver.PublishedName} from {driver.Provider}, installed {installed:yyyy-MM-dd HH:mm}");
                removed.Add(driver.PublishedName);
            }
            else if (failure == 0)
            {
                failure = result.ExitCode;
            }
        }

        var restart = removed.Count > 0;
        if (failure != 0)
        {
            return new StepResult(failure, "A recently installed driver could not be removed.", restart);
        }

        return removed.Count == 0
            ? new StepResult(0, "No third-party driver was installed shortly before the first crash.")
            : new StepResult(0, $"Removed {removed.Count} recently installed driver(s). Windows must restart.", restart);
    }

    internal sealed record Driver(string PublishedName, string Provider);

    /// <summary>oem*.inf entries whose provider is set and is not Microsoft; inbox drivers are never listed.</summary>
    internal static IReadOnlyList<Driver> ParseThirdParty(string output)
    {
        var drivers = new List<Driver>();
        string? published = null;
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries))
        {
            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (key.Equals("Published Name", StringComparison.OrdinalIgnoreCase))
            {
                published = value;
            }
            else if (key.Equals("Provider Name", StringComparison.OrdinalIgnoreCase) && published is not null)
            {
                if (OemInf().IsMatch(published) && value.Length > 0 && !value.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase))
                {
                    drivers.Add(new Driver(published, value));
                }

                published = null;
            }
        }

        return drivers;
    }

    /// <summary>pnputil shows no install date; the oem*.inf copy in the INF folder is created at install.</summary>
    private static async Task<DateTimeOffset?> InstallTimeAsync(ISystem system, string publishedName, CancellationToken ct)
    {
        var script = $@"(Get-Item '{system.WindowsDirectory}\INF\{publishedName}').CreationTime.ToString('o')";
        var result = await system.RunCommandAsync("powershell", $"-NoProfile -NonInteractive -Command \"{script}\"", ct);
        return result.ExitCode == 0 && DateTimeOffset.TryParse(result.Output.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;
    }

    [GeneratedRegex(@"^oem\d+\.inf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OemInf();
}
