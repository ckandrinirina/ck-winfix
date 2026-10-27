using System.Text.RegularExpressions;
using WinFix.System;

namespace WinFix.Media;

public sealed record InstalledWindows(string Architecture, string EditionId, string Language);

/// <summary>One image inside install.wim or install.esd, as DISM reports it.</summary>
public sealed record WimImage(string Architecture, string Edition, int Build, IReadOnlyList<string> Languages);

/// <summary>Decides whether an install image can reinstall this PC while keeping files.</summary>
public static partial class MediaMatcher
{
    private const string EnvironmentKey = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    private const string CurrentVersionKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const int MaxImages = 32;

    /// <summary>Null when the registry or the UI language cannot be read.</summary>
    public static async Task<InstalledWindows?> ReadInstalledAsync(ISystem system, CancellationToken ct)
    {
        var architecture = system.ReadRegistryValue(EnvironmentKey, "PROCESSOR_ARCHITECTURE");
        var edition = system.ReadRegistryValue(CurrentVersionKey, "EditionID");
        var language = await system.RunCommandAsync(
            "powershell", "-NoProfile -NonInteractive -Command \"[System.Globalization.CultureInfo]::InstalledUICulture.Name\"", ct);
        var languageName = language.Output.Trim();
        if (architecture is null || edition is null || language.ExitCode != 0 || languageName.Length == 0)
        {
            return null;
        }

        return new InstalledWindows(NormalizeArchitecture(architecture), edition, languageName);
    }

    /// <summary>Null when the text holds no image details.</summary>
    public static WimImage? ParseWimInfo(string dismOutput)
    {
        var start = IndexLine().Match(dismOutput);
        if (!start.Success)
        {
            return null;
        }

        var details = dismOutput[start.Index..];
        var architecture = Field(details, "Architecture");
        var edition = Field(details, "Edition");
        var version = BuildNumber().Match(details);
        if (architecture is null || edition is null || !version.Success)
        {
            return null;
        }

        return new WimImage(NormalizeArchitecture(architecture), edition, int.Parse(version.Groups[1].Value), Languages(details));
    }

    /// <summary>Every index of the image; multi-edition media holds one image per edition.</summary>
    public static async Task<IReadOnlyList<WimImage>> ReadImagesAsync(ISystem system, string imagePath, CancellationToken ct)
    {
        var images = new List<WimImage>();
        for (var index = 1; index <= MaxImages; index++)
        {
            var result = await system.RunCommandAsync("dism", $"/Get-WimInfo /WimFile:\"{imagePath}\" /Index:{index}", ct);
            var image = result.ExitCode == 0 ? ParseWimInfo(result.Output) : null;
            if (image is null)
            {
                break;
            }

            images.Add(image);
        }

        return images;
    }

    /// <summary>The newest matching image, else null with every mismatch reason in <paramref name="reason"/>.</summary>
    public static WimImage? Match(IReadOnlyList<WimImage> images, InstalledWindows installed, out string reason)
    {
        var reasons = new List<string>();
        WimImage? best = null;
        foreach (var image in images)
        {
            var mismatch = Mismatch(image, installed);
            if (mismatch is not null)
            {
                reasons.Add(mismatch);
            }
            else if (best is null || image.Build > best.Build)
            {
                best = image;
            }
        }

        reason = images.Count == 0 ? "the image could not be read" : string.Join("; ", reasons.Distinct());
        return best;
    }

    private static string? Mismatch(WimImage image, InstalledWindows installed)
    {
        var problems = new List<string>();
        if (!image.Architecture.Equals(installed.Architecture, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"architecture {image.Architecture} but this PC is {installed.Architecture}");
        }

        if (!image.Edition.Equals(installed.EditionId, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"edition {image.Edition} but this PC is {installed.EditionId}");
        }

        if (!image.Languages.Contains(installed.Language, StringComparer.OrdinalIgnoreCase))
        {
            problems.Add($"language {string.Join(", ", image.Languages)} but this PC is {installed.Language}");
        }

        return problems.Count == 0 ? null : string.Join(", ", problems);
    }

    private static string NormalizeArchitecture(string value) =>
        value.Trim().ToLowerInvariant() == "amd64" ? "x64" : value.Trim().ToLowerInvariant();

    private static string? Field(string text, string name)
    {
        var match = Regex.Match(text, $@"^\s*{name}\s*:\s*(\S.*?)\s*$", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static List<string> Languages(string details)
    {
        var block = LanguagesBlock().Match(details);
        return block.Success
            ? block.Groups[1].Value
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Replace("(Default)", "", StringComparison.OrdinalIgnoreCase).Trim())
                .Where(language => language.Length > 0)
                .ToList()
            : [];
    }

    [GeneratedRegex(@"^\s*Index\s*:", RegexOptions.Multiline)]
    private static partial Regex IndexLine();

    [GeneratedRegex(@"^\s*Version\s*:\s*\d+\.\d+\.(\d+)", RegexOptions.Multiline)]
    private static partial Regex BuildNumber();

    [GeneratedRegex(@"^\s*Languages\s*:\s*\r?\n((?:[ \t]+\S[^\r\n]*\r?\n?)+)", RegexOptions.Multiline)]
    private static partial Regex LanguagesBlock();
}
