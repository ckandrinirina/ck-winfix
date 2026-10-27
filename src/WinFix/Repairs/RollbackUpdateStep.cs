using System.Globalization;
using System.Text.RegularExpressions;
using WinFix.Problems;
using WinFix.System;

namespace WinFix.Repairs;

/// <summary>Uninstalls the newest quality update installed in the 3 days before the first crash.</summary>
public sealed partial class RollbackUpdateStep(Func<DateTimeOffset>? clock = null) : IRepairStep
{
    private const int RebootRequiredExit = 3010;

    // Get-HotFix reports a date, not a time, so the window is compared by calendar day.
    private const string HistoryScript =
        "Get-HotFix | Where-Object { $_.InstalledOn } | ForEach-Object { "
        + "'{0}|{1}|{2}' -f $_.HotFixID, $_.InstalledOn.ToString('yyyy-MM-dd',[cultureinfo]::InvariantCulture), $_.Description }";

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);

    public string Name => "Roll back the latest update";

    public async Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
    {
        var crashes = await CrashHistory.ReadAsync(system, _clock(), ct);
        if (crashes.Count == 0)
        {
            return new StepResult(0, "No recent crash was found, so no update was removed.");
        }

        var history = await system.RunCommandAsync("powershell", $"-NoProfile -NonInteractive -Command \"{HistoryScript}\"", ct);
        if (history.ExitCode != 0)
        {
            return new StepResult(history.ExitCode, "The list of installed updates could not be read.");
        }

        var crashDay = crashes[0].Time.Date;
        var cause = ParseUpdates(history.Output)
            .Where(u => u.InstalledOn >= crashDay - CrashHistory.CauseWindow && u.InstalledOn <= crashDay)
            .OrderBy(u => u.InstalledOn)
            .ThenBy(u => u.Number)
            .LastOrDefault();
        if (cause is null)
        {
            return new StepResult(0, "No update was installed shortly before the first crash.");
        }

        var result = await system.RunCommandAsync("wusa", $"/uninstall /kb:{cause.Number} /quiet /norestart", ct);
        if (result.ExitCode is not (0 or RebootRequiredExit))
        {
            return new StepResult(result.ExitCode, $"Update KB{cause.Number} could not be removed.");
        }

        system.Log($"Removed update KB{cause.Number}, installed {cause.InstalledOn:yyyy-MM-dd}");
        return new StepResult(0, $"Removed update KB{cause.Number}. Windows must restart.", RebootRequired: true);
    }

    internal sealed record Update(string Number, DateTime InstalledOn);

    internal static IReadOnlyList<Update> ParseUpdates(string output)
    {
        var updates = new List<Update>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|', 3);
            var kb = parts.Length == 3 ? KbId().Match(parts[0].Trim()) : Match.Empty;
            if (kb.Success && DateTime.TryParseExact(parts[1].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var installed))
            {
                updates.Add(new Update(kb.Groups[1].Value, installed));
            }
        }

        return updates;
    }

    [GeneratedRegex(@"^KB(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KbId();
}
