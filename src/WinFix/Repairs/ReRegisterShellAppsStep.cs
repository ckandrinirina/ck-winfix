using WinFix.System;

namespace WinFix.Repairs;

public sealed class ReRegisterShellAppsStep : IRepairStep
{
    private static readonly string[] Packages =
        ["ShellExperienceHost", "StartMenuExperienceHost", "Windows.Client.CBS"];

    public string Name => "Re-register shell apps";

    public async Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
    {
        var failure = 0;
        foreach (var package in Packages)
        {
            var script = "$ErrorActionPreference='Stop'; "
                + $"Get-AppxPackage -AllUsers {CommandArgs.QuotePowerShell($"*{package}*")} | ForEach-Object "
                + "{ Add-AppxPackage -DisableDevelopmentMode -Register ($_.InstallLocation + '\\AppXManifest.xml') }";
            var result = await system.RunCommandAsync(
                "powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"", ct);
            if (result.ExitCode != 0 && failure == 0)
            {
                failure = result.ExitCode;
            }
        }

        return failure == 0
            ? new StepResult(0, "The shell apps were registered again.")
            : new StepResult(failure, "At least one shell app could not be registered again.");
    }
}
