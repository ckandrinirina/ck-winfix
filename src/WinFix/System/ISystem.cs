namespace WinFix.System;

/// <summary>Output is stdout, Error is stderr. TimedOut: the command ran past its limit and was killed.</summary>
public sealed record CommandResult(int ExitCode, string Output, string Error = "", bool TimedOut = false);

/// <summary>Every OS side effect goes through here; only WindowsSystem touches the real machine.</summary>
public interface ISystem
{
    Task<CommandResult> RunCommandAsync(string fileName, string arguments, CancellationToken ct);

    /// <summary>An empty <paramref name="valueName"/> is the key's default value; null when missing.</summary>
    string? ReadRegistryValue(string keyPath, string valueName);

    void WriteRegistryValue(string keyPath, string valueName, string value);

    IReadOnlyList<int> FindProcesses(string name);

    void KillProcess(int processId);

    void StartProcess(string fileName);

    /// <summary>Returns whether the key existed; a missing key is not an error.</summary>
    bool DeleteRegistryKey(string keyPath);

    bool FindWindow(string className);

    /// <summary>Names of the direct subkeys; empty when the key is missing.</summary>
    IReadOnlyList<string> GetRegistrySubKeyNames(string keyPath);

    /// <summary>The Windows folder, e.g. C:\Windows.</summary>
    string WindowsDirectory { get; }

    /// <summary>Full path of the running exe, used for the RunOnce relaunch.</summary>
    string ExecutablePath { get; }

    /// <summary>Returns null when the file does not exist. Paths may hold %ENV% variables.</summary>
    string? ReadFile(string path);

    /// <summary>Creates missing folders; replaces an existing file.</summary>
    void WriteFile(string path, string content);

    /// <summary>A missing file is not an error.</summary>
    void DeleteFile(string path);

    /// <summary>A missing value is not an error.</summary>
    void DeleteRegistryValue(string keyPath, string valueName);

    /// <summary>Waits without blocking a thread; the fake returns at once so tests never sleep.</summary>
    Task DelayAsync(TimeSpan duration, CancellationToken ct);

    /// <summary>Appends one line to the WinFix log.</summary>
    void Log(string message);

    Task RebootAsync(CancellationToken ct);
}
