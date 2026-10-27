using WinFix.System;

namespace WinFix.Repairs;

public sealed class ClearIrisServiceStep : IRepairStep
{
    private const string IrisServiceKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\IrisService";

    public string Name => "Clear IrisService";

    public Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
    {
        var existed = system.DeleteRegistryKey(IrisServiceKey);
        return Task.FromResult(new StepResult(
            0, existed ? "The IrisService key was deleted." : "The IrisService key was not there."));
    }
}
