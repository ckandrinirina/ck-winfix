using WinFix.System;

namespace WinFixUsb.System;

/// <summary>Every macOS side effect goes through here; only the real implementation touches the Mac.</summary>
public interface IMacSystem
{
    /// <summary>Runs the program with each argument passed as one argv entry; no shell, no splitting.</summary>
    Task<CommandResult> RunCommandAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct);

    /// <summary>Returns null when the file does not exist.</summary>
    string? ReadFile(string path);

    /// <summary>Size in bytes; null when the file does not exist.</summary>
    long? FileSize(string path);

    /// <summary>Creates missing folders; replaces an existing file.</summary>
    void WriteFile(string path, string content);

    /// <summary>Shows <paramref name="prompt"/> and returns the typed line; null when input ended.</summary>
    Task<string?> AskAsync(string prompt, CancellationToken ct);
}
