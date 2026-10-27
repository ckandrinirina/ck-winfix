using WinFix.System;

namespace WinFix.Repairs;

public sealed class RestartExplorerStep : IRepairStep
{
    private const string ExplorerName = "explorer.exe";

    public string Name => "Restart Explorer";

    public Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
    {
        foreach (var processId in system.FindProcesses(ExplorerName))
        {
            system.KillProcess(processId);
        }

        system.StartProcess(ExplorerName);
        return Task.FromResult(new StepResult(0, "Explorer was stopped and started again."));
    }
}
