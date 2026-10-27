using System.Text.RegularExpressions;
using WinFix.System;

namespace WinFix.Media;

/// <summary>Install media that matches this PC; <see cref="IsoPath"/> is set when WinFix mounted it.</summary>
public sealed record InstallMedia(string Root, string ImagePath, string? IsoPath, int Build);

/// <summary>Finds matching Windows install media on its own; nothing here ever asks the user.</summary>
public sealed partial class MediaFinder
{
    private const string DriveScript =
        "Get-CimInstance Win32_LogicalDisk | ForEach-Object { '{0}|{1}' -f $_.DeviceID, $_.DriveType }";

    private sealed record Candidate(string Root, string ImagePath, string? IsoPath);

    /// <summary>The matching media with the highest build, or null. Kept ISOs stay mounted.</summary>
    public async Task<InstallMedia?> FindAsync(ISystem system, CancellationToken ct)
    {
        var installed = await MediaMatcher.ReadInstalledAsync(system, ct);
        if (installed is null)
        {
            system.Log("Install media search skipped: the installed Windows edition or language could not be read.");
            return null;
        }

        var roots = await ListDriveRootsAsync(system, ct);
        var candidates = new List<Candidate>();
        foreach (var root in roots)
        {
            if (await FindImageAsync(system, root, ct) is { } image)
            {
                candidates.Add(new Candidate(root, image, null));
            }
        }

        foreach (var iso in await ListIsosAsync(system, roots, ct))
        {
            if (await MountAsync(system, iso, candidates, ct) is { } mounted)
            {
                candidates.Add(mounted);
            }
        }

        var matches = new List<InstallMedia>();
        foreach (var candidate in candidates)
        {
            var images = await MediaMatcher.ReadImagesAsync(system, candidate.ImagePath, ct);
            var best = MediaMatcher.Match(images, installed, out var reason);
            if (best is null)
            {
                system.Log($"Install media {candidate.IsoPath ?? candidate.Root} skipped: {reason}.");
                await DismountAsync(system, candidate.IsoPath, ct);
                continue;
            }

            matches.Add(new InstallMedia(candidate.Root, candidate.ImagePath, candidate.IsoPath, best.Build));
        }

        var chosen = matches.MaxBy(m => m.Build);
        foreach (var other in matches.Where(m => m != chosen))
        {
            system.Log($"Install media {other.IsoPath ?? other.Root} skipped: build {other.Build} is older than {chosen!.Build}.");
            await DismountAsync(system, other.IsoPath, ct);
        }

        return chosen;
    }

    private static async Task<Candidate?> MountAsync(ISystem system, string iso, List<Candidate> known, CancellationToken ct)
    {
        if (!CommandArgs.CanQuotePowerShell(iso))
        {
            system.Log($"Install media {iso} skipped: its name holds a character that cannot be passed to PowerShell safely.");
            return null;
        }

        var root = await IsoMounter.MountAsync(system, iso, ct);
        if (root is null)
        {
            system.Log($"Install media {iso} skipped: it could not be mounted.");
            return null;
        }

        if (known.Any(c => c.Root.Equals(root, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var image = await FindImageAsync(system, root, ct);
        if (image is null)
        {
            system.Log($"Install media {iso} skipped: it holds no setup.exe with install.wim or install.esd.");
            await IsoMounter.DismountAsync(system, iso, ct);
            return null;
        }

        return new Candidate(root, image, iso);
    }

    private static async Task DismountAsync(ISystem system, string? iso, CancellationToken ct)
    {
        if (iso is not null)
        {
            await IsoMounter.DismountAsync(system, iso, ct);
        }
    }

    private static async Task<List<string>> ListDriveRootsAsync(ISystem system, CancellationToken ct)
    {
        var result = await system.RunCommandAsync("powershell", $"-NoProfile -NonInteractive -Command \"{DriveScript}\"", ct);
        return DriveLine().Matches(result.Output)
            .Where(m => m.Groups[2].Value is "2" or "3" or "5")
            .Select(m => m.Groups[1].Value + @"\")
            .ToList();
    }

    private static async Task<List<string>> ListIsosAsync(ISystem system, List<string> roots, CancellationToken ct)
    {
        var folders = string.Join(",", roots.Select(CommandArgs.QuotePowerShell));
        var script = $"$d = @({folders}); " +
            "$d += Join-Path $env:USERPROFILE 'Downloads'; " +
            "$d += [Environment]::GetFolderPath('Desktop'); " +
            "Get-ChildItem -LiteralPath $d -Filter *.iso -File -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }";
        var result = await system.RunCommandAsync("powershell", $"-NoProfile -NonInteractive -Command \"{script}\"", ct);
        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<string?> FindImageAsync(ISystem system, string root, CancellationToken ct)
    {
        if (!await ExistsAsync(system, root + "setup.exe", ct))
        {
            return null;
        }

        foreach (var name in new[] { "install.wim", "install.esd" })
        {
            var path = root + @"sources\" + name;
            if (await ExistsAsync(system, path, ct))
            {
                return path;
            }
        }

        return null;
    }

    private static async Task<bool> ExistsAsync(ISystem system, string path, CancellationToken ct)
    {
        var result = await system.RunCommandAsync(
            "powershell", $"-NoProfile -NonInteractive -Command \"Test-Path -LiteralPath {CommandArgs.QuotePowerShell(path)}\"", ct);
        return result.ExitCode == 0 && result.Output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^\s*([A-Za-z]:)\|(\d+)", RegexOptions.Multiline)]
    private static partial Regex DriveLine();
}
