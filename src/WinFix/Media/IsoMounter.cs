using System.Text.RegularExpressions;
using WinFix.System;

namespace WinFix.Media;

/// <summary>Mounts and dismounts ISO files with the Storage cmdlets.</summary>
public static partial class IsoMounter
{
    /// <summary>The mounted volume's root such as H:\, or null when the mount failed.</summary>
    public static async Task<string?> MountAsync(ISystem system, string isoPath, CancellationToken ct)
    {
        var script = $"(Mount-DiskImage -ImagePath {CommandArgs.QuotePowerShell(isoPath)} -PassThru | Get-Volume).DriveLetter";
        var result = await system.RunCommandAsync("powershell", $"-NoProfile -NonInteractive -Command \"{script}\"", ct);
        var letter = DriveLetter().Match(result.Output);
        return result.ExitCode == 0 && letter.Success ? $@"{letter.Value.ToUpperInvariant()}:\" : null;
    }

    public static async Task DismountAsync(ISystem system, string isoPath, CancellationToken ct)
    {
        var script = $"Dismount-DiskImage -ImagePath {CommandArgs.QuotePowerShell(isoPath)}";
        var result = await system.RunCommandAsync("powershell", $"-NoProfile -NonInteractive -Command \"{script}\"", ct);
        if (result.ExitCode != 0)
        {
            system.Log($"Could not dismount {isoPath} (exit code {result.ExitCode}).");
        }
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z](?![A-Za-z0-9])")]
    private static partial Regex DriveLetter();
}
