using WinFix.System;

namespace WinFix.Diagnose;

public sealed record ShellHealth(bool Healthy, string Reason);

/// <summary>Taskbar window present and explorer.exe not crash-looping, judged over a watch window.</summary>
public sealed class ShellHealthCheck(
    ISystem system,
    TimeSpan? watchWindow = null,
    TimeSpan? sampleInterval = null)
{
    private const string TaskbarWindowClass = "Shell_TrayWnd";

    private const string ExplorerName = "explorer.exe";
    private const int CrashLoopStarts = 3;

    private readonly TimeSpan _window = watchWindow ?? TimeSpan.FromSeconds(20);
    private readonly TimeSpan _interval = sampleInterval ?? TimeSpan.FromSeconds(1);

    public async Task<ShellHealth> CheckAsync(CancellationToken ct)
    {
        var health = await JudgeAsync(ct);
        system.Log($"Shell diagnosis: {health.Reason}");
        return health;
    }

    private async Task<ShellHealth> JudgeAsync(CancellationToken ct)
    {
        if (!system.FindWindow(TaskbarWindowClass))
        {
            return new ShellHealth(false, $"The taskbar window ({TaskbarWindowClass}) is missing.");
        }

        var seen = new HashSet<int>(system.FindProcesses(ExplorerName));
        for (var waited = TimeSpan.Zero; waited < _window; waited += _interval)
        {
            await system.DelayAsync(_interval, ct);
            seen.UnionWith(system.FindProcesses(ExplorerName));
        }

        return seen.Count >= CrashLoopStarts
            ? new ShellHealth(false, $"{ExplorerName} started {seen.Count} times in {_window.TotalSeconds:0} seconds.")
            : new ShellHealth(true, $"The taskbar is present and {ExplorerName} is stable"
                + (seen.Count == 1 ? $" (process {seen.Single()})." : "."));
    }
}
