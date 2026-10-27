using WinFix.Problems;
using WinFix.System;

namespace WinFix.Repairs;

/// <summary>Registers again each broken Start menu / Search / Settings package from its install location; healthy ones are left alone.</summary>
public sealed class ReRegisterSystemAppsStep : IRepairStep
{
    public string Name => "Re-register Start menu, Search and Settings apps";

    public async Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
    {
        var failure = 0;
        foreach (var package in await StartAppsProblem.FindBrokenAsync(system, ct))
        {
            if (package.InstallLocation is null)
            {
                system.Log($"{package.Name} is not installed and has no install location to register from.");
                failure = failure == 0 ? 1 : failure;
                continue;
            }

            var manifest = $@"{package.InstallLocation}\AppxManifest.xml";
            if (!CommandArgs.CanQuotePowerShell(manifest))
            {
                system.Log($"{package.Name} has an install location that cannot be passed to PowerShell safely: {manifest}");
                failure = failure == 0 ? 1 : failure;
                continue;
            }

            var script = $"$ErrorActionPreference='Stop'; Add-AppxPackage -DisableDevelopmentMode -Register {CommandArgs.QuotePowerShell(manifest)}";
            var result = await system.RunCommandAsync(
                "powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"", ct);
            failure = result.ExitCode != 0 && failure == 0 ? result.ExitCode : failure;
        }

        return failure == 0
            ? new StepResult(0, "The Start menu, Search and Settings apps were registered again.")
            : new StepResult(failure, "At least one of the Start menu, Search or Settings apps could not be registered again.");
    }
}
