namespace WinFix.System;

public sealed record CommandResult(int ExitCode, string Output);

/// <summary>Every OS side effect goes through here; only WindowsSystem touches the real machine.</summary>
public interface ISystem
{
    Task<CommandResult> RunCommandAsync(string fileName, string arguments, CancellationToken ct);

    string? ReadRegistryValue(string keyPath, string valueName);

    void WriteRegistryValue(string keyPath, string valueName, string value);

    IReadOnlyList<int> FindProcesses(string name);

    void KillProcess(int processId);

    bool FindWindow(string className);

    /// <summary>Waits without blocking a thread; the fake returns at once so tests never sleep.</summary>
    Task DelayAsync(TimeSpan duration, CancellationToken ct);

    /// <summary>Appends one line to the WinFix log.</summary>
    void Log(string message);

    Task RebootAsync(CancellationToken ct);
}
