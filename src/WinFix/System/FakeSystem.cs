namespace WinFix.System;

/// <summary>Records every call and touches nothing; backs --dry-run and the unit tests.</summary>
public sealed class FakeSystem : ISystem
{
    private readonly List<string> _calls = [];

    public IReadOnlyList<string> Calls => _calls;

    public bool ShellHealthy { get; init; }

    public Task<CommandResult> RunCommandAsync(string fileName, string arguments, CancellationToken ct)
    {
        _calls.Add($"RunCommand {fileName} {arguments}".TrimEnd());
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
        return [];
    }

    public void KillProcess(int processId) => _calls.Add($"KillProcess {processId}");

    public bool FindWindow(string className)
    {
        _calls.Add($"FindWindow {className}");
        return ShellHealthy;
    }

    public Task RebootAsync(CancellationToken ct)
    {
        _calls.Add("Reboot");
        return Task.CompletedTask;
    }
}
