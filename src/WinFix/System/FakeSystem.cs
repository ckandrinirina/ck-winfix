namespace WinFix.System;

/// <summary>Records every call and touches nothing; backs --dry-run and the unit tests.</summary>
public sealed class FakeSystem : ISystem
{
    private const string ExplorerName = "explorer.exe";

    private readonly List<string> _calls = [];
    private readonly List<string> _logs = [];
    private int _explorerSample;
    private bool _repaired;

    public IReadOnlyList<string> Calls => _calls;

    public IReadOnlyList<string> Logs => _logs;

    public TimeSpan TotalDelay { get; private set; }

    public bool ShellHealthy { get; init; }

    /// <summary>Once any command has run, the taskbar exists.</summary>
    public bool RepairFixesShell { get; init; }

    /// <summary>One entry per explorer.exe lookup; the last entry repeats once they run out.</summary>
    public IReadOnlyList<IReadOnlyList<int>> ExplorerProcessIdSamples { get; init; } = [];

    public Task<CommandResult> RunCommandAsync(string fileName, string arguments, CancellationToken ct)
    {
        _calls.Add($"RunCommand {fileName} {arguments}".TrimEnd());
        _repaired = true;
        return Task.FromResult(new CommandResult(0, string.Empty));
    }

    public string? ReadRegistryValue(string keyPath, string valueName)
    {
        _calls.Add($"ReadRegistry {keyPath} {valueName}");
        return null;
    }

    public void WriteRegistryValue(string keyPath, string valueName, string value) =>
        _calls.Add($"WriteRegistry {keyPath} {valueName}={value}");

    public IReadOnlyList<int> FindProcesses(string name)
    {
        _calls.Add($"FindProcesses {name}");
        if (!name.Equals(ExplorerName, StringComparison.OrdinalIgnoreCase) || ExplorerProcessIdSamples.Count == 0)
        {
            return [];
        }

        return ExplorerProcessIdSamples[Math.Min(_explorerSample++, ExplorerProcessIdSamples.Count - 1)];
    }

    public void KillProcess(int processId) => _calls.Add($"KillProcess {processId}");

    public bool FindWindow(string className)
    {
        _calls.Add($"FindWindow {className}");
        return ShellHealthy || (RepairFixesShell && _repaired);
    }

    public Task DelayAsync(TimeSpan duration, CancellationToken ct)
    {
        TotalDelay += duration;
        return Task.CompletedTask;
    }

    public void Log(string message) => _logs.Add(message);

    public Task RebootAsync(CancellationToken ct)
    {
        _calls.Add("Reboot");
        return Task.CompletedTask;
    }
}
