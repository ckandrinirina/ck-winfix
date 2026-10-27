namespace WinFix.System;

/// <summary>Records every call and touches nothing; backs --dry-run and the unit tests.</summary>
public sealed class FakeSystem : ISystem
{
    private const string ExplorerName = "explorer.exe";

    private readonly List<string> _calls = [];
    private readonly List<string> _logs = [];
    private int _explorerSample;
    private bool _repaired;
    private bool _fixedByCall;

    public IReadOnlyList<string> Calls => _calls;

    public IReadOnlyList<string> Logs => _logs;

    public TimeSpan TotalDelay { get; private set; }

    public bool ShellHealthy { get; init; }

    /// <summary>Exit code every command returns.</summary>
    public int CommandExitCode { get; init; }

    /// <summary>Registry keys that exist; DeleteRegistryKey reports whether it removed one.</summary>
    public IReadOnlyCollection<string> ExistingRegistryKeys { get; init; } = [];

    /// <summary>The taskbar exists once a recorded call starts with this text.</summary>
    public string? FixedByCall { get; init; }

    /// <summary>Once any command has run, the taskbar exists.</summary>
    public bool RepairFixesShell { get; init; }

    /// <summary>One entry per explorer.exe lookup; the last entry repeats once they run out.</summary>
    public IReadOnlyList<IReadOnlyList<int>> ExplorerProcessIdSamples { get; init; } = [];

    public Task<CommandResult> RunCommandAsync(string fileName, string arguments, CancellationToken ct)
    {
        Record($"RunCommand {fileName} {arguments}".TrimEnd());
        _repaired = true;
        return Task.FromResult(new CommandResult(CommandExitCode, string.Empty));
    }

    public string? ReadRegistryValue(string keyPath, string valueName)
    {
        Record($"ReadRegistry {keyPath} {valueName}");
        return null;
    }

    public void WriteRegistryValue(string keyPath, string valueName, string value) =>
        Record($"WriteRegistry {keyPath} {valueName}={value}");

    public IReadOnlyList<int> FindProcesses(string name)
    {
        Record($"FindProcesses {name}");
        if (!name.Equals(ExplorerName, StringComparison.OrdinalIgnoreCase) || ExplorerProcessIdSamples.Count == 0)
        {
            return [];
        }

        return ExplorerProcessIdSamples[Math.Min(_explorerSample++, ExplorerProcessIdSamples.Count - 1)];
    }

    public void KillProcess(int processId) => Record($"KillProcess {processId}");

    public void StartProcess(string fileName) => Record($"StartProcess {fileName}");

    public bool DeleteRegistryKey(string keyPath)
    {
        Record($"DeleteRegistryKey {keyPath}");
        return ExistingRegistryKeys.Contains(keyPath);
    }

    public bool FindWindow(string className)
    {
        Record($"FindWindow {className}");
        return ShellHealthy || _fixedByCall || (RepairFixesShell && _repaired);
    }

    private void Record(string call)
    {
        _calls.Add(call);
        if (FixedByCall is not null && call.StartsWith(FixedByCall, StringComparison.Ordinal))
        {
            _fixedByCall = true;
        }
    }

    public Task DelayAsync(TimeSpan duration, CancellationToken ct)
    {
        TotalDelay += duration;
        return Task.CompletedTask;
    }

    public void Log(string message) => _logs.Add(message);

    public Task RebootAsync(CancellationToken ct)
    {
        Record("Reboot");
        return Task.CompletedTask;
    }
}
