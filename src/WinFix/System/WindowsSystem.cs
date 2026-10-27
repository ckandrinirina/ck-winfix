using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;

namespace WinFix.System;

/// <summary>The real machine. Commands run as given, never wrapped in an extra shell; every command is appended to the log.</summary>
public sealed class WindowsSystem : ISystem
{
    public const string DefaultLogPath = @"%ProgramData%\WinFix\winfix.log";

    /// <summary>The exit code a command killed at its timeout reports.</summary>
    public const int TimedOutExitCode = -1;

    private const int MaxLoggedOutput = 500;

    // DISM, SFC and setup.exe can run for over an hour on a slow disk.
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromHours(2);

    private static readonly Lock LogLock = new();

    private readonly string _logPath;
    private readonly TimeSpan _commandTimeout;

    /// <param name="logPath">May hold %ENV% variables.</param>
    public WindowsSystem(string logPath = DefaultLogPath, TimeSpan? commandTimeout = null)
    {
        _logPath = logPath;
        _commandTimeout = commandTimeout ?? DefaultCommandTimeout;
    }

    public string WindowsDirectory => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public string ExecutablePath => Environment.ProcessPath ?? string.Empty;

    public async Task<CommandResult> RunCommandAsync(string fileName, string arguments, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            Log($"Command: {fileName} {arguments} | could not start: {ex.Message}");
            throw;
        }

        // Both pipes drain while the process runs; reading one after the other can deadlock on a full pipe.
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(_commandTimeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            if (ct.IsCancellationRequested)
            {
                Log($"Command: {fileName} {arguments} | cancelled after {Seconds(watch.Elapsed)}");
                throw;
            }

            timedOut = true;
        }

        var result = timedOut
            ? new CommandResult(TimedOutExitCode, await stdout, await stderr, TimedOut: true)
            : new CommandResult(process.ExitCode, await stdout, await stderr);
        Log(FormatCommandLog(fileName, arguments, result, watch.Elapsed));
        return result;
    }

    /// <summary>One log line: command, exit code, duration, and stdout/stderr flattened and truncated.</summary>
    public static string FormatCommandLog(string fileName, string arguments, CommandResult result, TimeSpan duration)
    {
        var exit = result.TimedOut ? "timed out, killed" : $"exit {result.ExitCode}";
        return $"Command: {fileName} {arguments} | {exit} | {Seconds(duration)} | out: {Flatten(result.Output)} | err: {Flatten(result.Error)}";
    }

    public void Log(string message)
    {
        var line = $"{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {message}{Environment.NewLine}";
        var path = Environment.ExpandEnvironmentVariables(_logPath);
        lock (LogLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A log that cannot be written must never stop a repair.
            }
        }
    }

    [SupportedOSPlatform("windows")]
    public string? ReadRegistryValue(string keyPath, string valueName)
    {
        var (hive, subKey) = SplitKey(keyPath);
        using var key = hive.OpenSubKey(subKey);
        return key?.GetValue(valueName) switch
        {
            null => null,
            string text => text,
            var other => Convert.ToString(other, CultureInfo.InvariantCulture),
        };
    }

    [SupportedOSPlatform("windows")]
    public void WriteRegistryValue(string keyPath, string valueName, string value)
    {
        Log($"Registry: set {keyPath} {valueName}");
        var (hive, subKey) = SplitKey(keyPath);
        using var key = hive.CreateSubKey(subKey, writable: true);
        key.SetValue(valueName, value);
    }

    [SupportedOSPlatform("windows")]
    public void DeleteRegistryValue(string keyPath, string valueName)
    {
        Log($"Registry: delete value {keyPath} {valueName}");
        var (hive, subKey) = SplitKey(keyPath);
        using var key = hive.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    [SupportedOSPlatform("windows")]
    public bool DeleteRegistryKey(string keyPath)
    {
        Log($"Registry: delete key {keyPath}");
        var (hive, subKey) = SplitKey(keyPath);
        var slash = subKey.LastIndexOf('\\');
        using var parent = slash < 0 ? hive.OpenSubKey(string.Empty, writable: true) : hive.OpenSubKey(subKey[..slash], writable: true);
        var name = subKey[(slash + 1)..];
        using (var existing = parent?.OpenSubKey(name))
        {
            if (existing is null)
            {
                return false;
            }
        }

        parent!.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        return true;
    }

    [SupportedOSPlatform("windows")]
    public IReadOnlyList<string> GetRegistrySubKeyNames(string keyPath)
    {
        var (hive, subKey) = SplitKey(keyPath);
        using var key = hive.OpenSubKey(subKey);
        return key?.GetSubKeyNames() ?? [];
    }

    public IReadOnlyList<int> FindProcesses(string name)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name));
        var ids = processes.Select(p => p.Id).ToList();
        foreach (var process in processes)
        {
            process.Dispose();
        }

        return ids;
    }

    public void KillProcess(int processId)
    {
        Log($"Process: kill {processId}");
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill();
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    public void StartProcess(string fileName)
    {
        Log($"Process: start {fileName}");
        using var process = Process.Start(new ProcessStartInfo(fileName) { UseShellExecute = false });
    }

    [SupportedOSPlatform("windows")]
    public bool FindWindow(string className) => NativeFindWindow(className, null) != IntPtr.Zero;

    public string? ReadFile(string path)
    {
        var full = Environment.ExpandEnvironmentVariables(path);
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    public void WriteFile(string path, string content)
    {
        var full = Environment.ExpandEnvironmentVariables(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void DeleteFile(string path)
    {
        var full = Environment.ExpandEnvironmentVariables(path);
        if (File.Exists(full))
        {
            File.Delete(full);
        }
    }

    public Task DelayAsync(TimeSpan duration, CancellationToken ct) => Task.Delay(duration, ct);

    public Task RebootAsync(CancellationToken ct) => RunCommandAsync("shutdown", "/r /t 0", ct);

    [SupportedOSPlatform("windows")]
    private static (RegistryKey Hive, string SubKey) SplitKey(string keyPath)
    {
        var slash = keyPath.IndexOf('\\');
        var hiveName = slash < 0 ? keyPath : keyPath[..slash];
        var hive = hiveName.ToUpperInvariant() switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKU" or "HKEY_USERS" => Registry.Users,
            "HKCR" or "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            _ => throw new ArgumentException($"Unknown registry hive in {keyPath}.", nameof(keyPath)),
        };
        return (hive, slash < 0 ? string.Empty : keyPath[(slash + 1)..]);
    }

    private static string Seconds(TimeSpan duration) => duration.TotalSeconds.ToString("0.0 s", CultureInfo.InvariantCulture);

    /// <summary>One line: NULs (sfc prints UTF-16) dropped, line breaks to spaces, cut at <see cref="MaxLoggedOutput"/>.</summary>
    private static string Flatten(string text)
    {
        var flat = new StringBuilder(Math.Min(text.Length, MaxLoggedOutput));
        foreach (var c in text.Trim())
        {
            if (c == '\0')
            {
                continue;
            }

            if (flat.Length == MaxLoggedOutput)
            {
                return flat.Append('…').ToString();
            }

            flat.Append(char.IsControl(c) ? ' ' : c);
        }

        return flat.ToString();
    }

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    private static extern IntPtr NativeFindWindow(string? className, string? windowName);
}
