using WinFix.Diagnose;
using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Problems;

/// <summary>Broken shell: no taskbar, or explorer.exe in a crash loop.</summary>
public sealed class ShellProblem(ShellHealthCheck? healthCheck = null, IReadOnlyList<IRepairStep>? repairs = null) : IProblemModule
{
    public string Name => "shell";

    public IReadOnlyList<IRepairStep> Repairs { get; } =
        repairs ??
        [
            new RestartExplorerStep(),
            new ClearIrisServiceStep(),
            new ReRegisterShellAppsStep(),
            new DismRestoreHealthStep(),
            new SfcScanStep(),
            new ShellExtensionsStep(),
        ];

    public async Task<Health> DetectAsync(ISystem system, CancellationToken ct)
    {
        var shell = await (healthCheck ?? new ShellHealthCheck(system)).CheckAsync(ct);
        return new Health(shell.Healthy, shell.Reason);
    }
}
