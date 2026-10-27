using System.Globalization;
using System.Text.RegularExpressions;
using WinFix.System;

namespace WinFix.Problems;

/// <summary>A BugCheck (1001) or Kernel-Power (41) event. Time keeps the machine's UTC offset.</summary>
internal sealed record CrashEvent(DateTimeOffset Time, int EventId, string Message);

/// <summary>Reads recent crashes from the System log through ISystem.RunCommand; the steps share it to find the first crash.</summary>
internal static partial class CrashHistory
{
    public const int MinCrashes = 2;

    public static readonly TimeSpan LookBack = TimeSpan.FromDays(7);

    /// <summary>How far before the first crash a driver or update can have been installed to count as the cause.</summary>
    public static readonly TimeSpan CauseWindow = TimeSpan.FromDays(3);

    private const string QueryScript =
        "Get-WinEvent -FilterHashtable @{LogName='System'; Id=1001,41; "
        + "ProviderName='Microsoft-Windows-WER-SystemErrorReporting','Microsoft-Windows-Kernel-Power'; "
        + "StartTime=(Get-Date).AddDays(-7)} -ErrorAction SilentlyContinue | ForEach-Object { "
        + "'{0}|{1}|{2}' -f $_.TimeCreated.ToString('o'), $_.Id, ($_.Message -replace '\\s+',' ') }";

    /// <summary>Crashes inside the look-back window, oldest first; empty when the query fails.</summary>
    public static async Task<IReadOnlyList<CrashEvent>> ReadAsync(ISystem system, DateTimeOffset now, CancellationToken ct)
    {
        var result = await system.RunCommandAsync("powershell", $"-NoProfile -NonInteractive -Command \"{QueryScript}\"", ct);
        if (result.ExitCode != 0)
        {
            return [];
        }

        return Parse(result.Output)
            .Where(e => e.Time >= now - LookBack)
            .OrderBy(e => e.Time)
            .ToList();
    }

    /// <summary>Full paths of the *.dmp files in %SystemRoot%\Minidump; empty when the folder is missing.</summary>
    public static async Task<IReadOnlyList<string>> ListMinidumpsAsync(ISystem system, CancellationToken ct)
    {
        var folder = $@"{system.WindowsDirectory}\Minidump";
        var result = await system.RunCommandAsync("cmd", $"/c dir /b \"{folder}\\*.dmp\"", ct);
        if (result.ExitCode != 0)
        {
            return [];
        }

        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => name.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase))
            .Select(name => $@"{folder}\{name}")
            .ToList();
    }

    /// <summary>Only a BugCheck message carries the stop code.</summary>
    public static string? StopCode(CrashEvent crash)
    {
        var match = StopCodePattern().Match(crash.Message);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static IEnumerable<CrashEvent> Parse(string output)
    {
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|', 3);
            if (parts.Length == 3
                && DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && id is 1001 or 41)
            {
                yield return new CrashEvent(time, id, parts[2]);
            }
        }
    }

    [GeneratedRegex(@"bugcheck was:\s*(0x[0-9a-f]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StopCodePattern();
}
