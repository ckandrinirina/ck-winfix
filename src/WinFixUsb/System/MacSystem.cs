using System.ComponentModel;
using System.Diagnostics;
using WinFix.System;

namespace WinFixUsb.System;

/// <summary>The real Mac: the only class that starts processes. Arguments go through ArgumentList, one argv entry each, never through a shell.</summary>
public sealed class MacSystem : IMacSystem
{
    /// <summary>The shell's "command not found" code, returned when the program cannot start.</summary>
    public const int NotFoundExitCode = 127;

    public async Task<CommandResult> RunCommandAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            return new CommandResult(NotFoundExitCode, string.Empty, ex.Message);
        }

        // Both pipes drain while the process runs; reading one after the other can deadlock on a full pipe.
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return new CommandResult(process.ExitCode, await stdout, await stderr);
    }

    public string? ReadFile(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    public long? FileSize(string path) => File.Exists(path) ? new FileInfo(path).Length : null;

    public void WriteFile(string path, string content)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(path, content);
    }

    public async Task<string?> AskAsync(string prompt, CancellationToken ct)
    {
        Console.Out.Write($"{prompt} ");
        return await Console.In.ReadLineAsync(ct);
    }
}
