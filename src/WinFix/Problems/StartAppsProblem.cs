using System.Text.Json;
using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Problems;

/// <summary>A system app package that is not installed, or whose status is not Ok. InstallLocation is null when nothing is left to register from.</summary>
public sealed record BrokenPackage(string Name, string? InstallLocation);

/// <summary>Start menu, Search or Settings will not open: one of their app packages is missing or damaged.</summary>
public sealed class StartAppsProblem : IProblemModule
{
    // StartMenuExperienceHost; SearchHost (lives in the Windows 11 CBS package); SystemSettings.
    private static readonly string[] PackageNames =
    [
        "Microsoft.Windows.StartMenuExperienceHost",
        "MicrosoftWindows.Client.CBS",
        "windows.immersivecontrolpanel",
    ];

    public string Name => "startapps";

    public IReadOnlyList<IRepairStep> Repairs { get; } = [new ReRegisterSystemAppsStep()];

    public async Task<Health> DetectAsync(ISystem system, CancellationToken ct)
    {
        var broken = await FindBrokenAsync(system, ct);
        return broken.Count == 0
            ? new Health(true, "Start menu, Search and Settings packages are fine.")
            : new Health(false, "Broken or missing packages: " + string.Join(", ", broken.Select(b => b.Name)) + ".");
    }

    /// <summary>A package is broken when it has no copy in the Ok status.</summary>
    internal static async Task<IReadOnlyList<BrokenPackage>> FindBrokenAsync(ISystem system, CancellationToken ct)
    {
        var names = string.Join(",", PackageNames.Select(n => $"'{n}'"));
        var script = $"Get-AppxPackage -AllUsers | Where-Object {{ @({names}) -contains $_.Name }} "
            + "| ForEach-Object { [pscustomobject]@{ Name = $_.Name; Status = $_.Status.ToString(); InstallLocation = $_.InstallLocation } } "
            + "| ConvertTo-Json -Compress";
        var result = await system.RunCommandAsync(
            "powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"", ct);

        var found = result.ExitCode == 0 ? Parse(result.Output) : [];
        return PackageNames
            .Select(name => (Name: name, Copies: found.Where(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList()))
            .Where(p => !p.Copies.Any(c => c.Ok))
            .Select(p => new BrokenPackage(p.Name, p.Copies.Select(c => c.InstallLocation).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))))
            .ToList();
    }

    private sealed record Found(string Name, bool Ok, string? InstallLocation);

    // PowerShell prints one package as an object, several as an array.
    private static List<Found> Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        try
        {
            using var doc = JsonDocument.Parse(output);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().ToList()
                : [doc.RootElement];
            return items
                .Where(i => i.ValueKind == JsonValueKind.Object && i.TryGetProperty("Name", out var n) && n.ValueKind == JsonValueKind.String)
                .Select(i => new Found(
                    i.GetProperty("Name").GetString()!,
                    IsOk(i),
                    i.TryGetProperty("InstallLocation", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() : null))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsOk(JsonElement item)
    {
        if (!item.TryGetProperty("Status", out var status))
        {
            return false;
        }

        return status.ValueKind switch
        {
            JsonValueKind.String => string.Equals(status.GetString(), "Ok", StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => status.TryGetInt32(out var code) && code == 0,
            _ => false,
        };
    }
}
