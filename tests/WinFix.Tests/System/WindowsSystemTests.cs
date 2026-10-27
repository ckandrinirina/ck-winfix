using System.Diagnostics;
using System.Runtime.Versioning;
using WinFix.System;

namespace WinFix.Tests.System;

public sealed class WindowsSystemTests : IDisposable
{
    private const string WindowsOnly = "Runs only on Windows.";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "winfix-ws-" + Guid.NewGuid().ToString("N"));

    private string LogPath => Path.Combine(_dir, "winfix.log");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Default_log_path_is_the_programdata_winfix_log()
    {
        Assert.Equal(@"%ProgramData%\WinFix\winfix.log", WindowsSystem.DefaultLogPath);
    }

    [Fact]
    public void Log_appends_lines_to_the_expanded_path_and_creates_the_folder()
    {
        const string variable = "WINFIX_TEST_LOG_DIR";
        Environment.SetEnvironmentVariable(variable, _dir);
        try
        {
            var system = new WindowsSystem(Path.Combine($"%{variable}%", "sub", "winfix.log"));

            system.Log("first");
            system.Log("second");

            var lines = File.ReadAllLines(Path.Combine(_dir, "sub", "winfix.log"));
            Assert.Equal(2, lines.Length);
            Assert.EndsWith("first", lines[0]);
            Assert.EndsWith("second", lines[1]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void Command_log_line_holds_command_exit_code_duration_and_output_on_one_line()
    {
        var result = new CommandResult(3, "line one\r\nline two", "bad thing");

        var line = WindowsSystem.FormatCommandLog("cmd", "/c exit 3", result, TimeSpan.FromMilliseconds(1234));

        Assert.Contains("cmd /c exit 3", line);
        Assert.Contains("exit 3", line);
        Assert.Contains("1.2 s", line);
        Assert.Contains("line one", line);
        Assert.Contains("line two", line);
        Assert.Contains("bad thing", line);
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
    }

    [Fact]
    public void Command_log_line_truncates_long_output()
    {
        var result = new CommandResult(0, new string('x', 10_000), new string('y', 10_000));

        var line = WindowsSystem.FormatCommandLog("dism", "/Online", result, TimeSpan.FromSeconds(1));

        Assert.True(line.Length < 2_000, $"log line is {line.Length} characters");
        Assert.Contains("…", line);
    }

    [Fact]
    public void Command_log_line_says_when_the_command_timed_out()
    {
        var result = new CommandResult(WindowsSystem.TimedOutExitCode, "", "", TimedOut: true);

        var line = WindowsSystem.FormatCommandLog("sfc", "/scannow", result, TimeSpan.FromMinutes(5));

        Assert.Contains("timed out", line);
    }

    [Fact]
    public void Files_expand_environment_variables_and_create_folders()
    {
        const string variable = "WINFIX_TEST_STATE_DIR";
        Environment.SetEnvironmentVariable(variable, _dir);
        try
        {
            ISystem system = new WindowsSystem(LogPath);
            var path = Path.Combine($"%{variable}%", "WinFix", "state.json");

            system.WriteFile(path, "{}");
            var read = system.ReadFile(path);
            system.DeleteFile(path);
            system.DeleteFile(path);

            Assert.Equal("{}", read);
            Assert.Null(system.ReadFile(path));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task Command_returns_its_exit_code()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);

        var result = await new WindowsSystem(LogPath).RunCommandAsync("cmd", "/c exit 3", TestContext.Current.CancellationToken);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.TimedOut);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task Command_captures_stdout_and_stderr()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);

        var result = await new WindowsSystem(LogPath).RunCommandAsync(
            "cmd", "/c \"echo to-out & echo to-err 1>&2\"", TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("to-out", result.Output);
        Assert.DoesNotContain("to-err", result.Output);
        Assert.Contains("to-err", result.Error);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task Command_past_its_timeout_is_killed_with_its_children()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        var watch = Stopwatch.StartNew();

        var result = await new WindowsSystem(LogPath, TimeSpan.FromSeconds(1)).RunCommandAsync(
            "cmd", "/c ping -n 30 127.0.0.1", TestContext.Current.CancellationToken);

        Assert.True(result.TimedOut);
        Assert.Equal(WindowsSystem.TimedOutExitCode, result.ExitCode);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"took {watch.Elapsed}");
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task Every_command_is_appended_to_the_log()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        var system = new WindowsSystem(LogPath);

        await system.RunCommandAsync("cmd", "/c exit 3", TestContext.Current.CancellationToken);
        await system.RunCommandAsync("cmd", "/c exit 0", TestContext.Current.CancellationToken);

        var lines = File.ReadAllLines(LogPath);
        Assert.Contains(lines, l => l.Contains("cmd /c exit 3") && l.Contains("exit 3"));
        Assert.Contains(lines, l => l.Contains("cmd /c exit 0") && l.Contains("exit 0"));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Registry_values_and_keys_round_trip()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        ISystem system = new WindowsSystem(LogPath);
        var root = @"HKCU\Software\WinFix.Tests\" + Guid.NewGuid().ToString("N");
        try
        {
            system.WriteRegistryValue(root + @"\Child", "Name", "Value");

            Assert.Equal("Value", system.ReadRegistryValue(root + @"\Child", "Name"));
            Assert.Equal(["Child"], system.GetRegistrySubKeyNames(root));
            system.DeleteRegistryValue(root + @"\Child", "Name");
            system.DeleteRegistryValue(root + @"\Child", "Name");
            Assert.Null(system.ReadRegistryValue(root + @"\Child", "Name"));
            Assert.Null(system.ReadRegistryValue(root + @"\Missing", "Name"));
            Assert.Empty(system.GetRegistrySubKeyNames(root + @"\Missing"));
            Assert.True(system.DeleteRegistryKey(root));
            Assert.False(system.DeleteRegistryKey(root));
        }
        finally
        {
            system.DeleteRegistryKey(root);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void FindWindow_is_false_for_a_class_no_window_has()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);

        Assert.False(new WindowsSystem(LogPath).FindWindow("WinFix.NoSuchClass." + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Windows_directory_and_executable_path_come_from_the_environment()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        ISystem system = new WindowsSystem(LogPath);

        Assert.True(Directory.Exists(system.WindowsDirectory));
        Assert.Equal(Environment.ProcessPath, system.ExecutablePath);
    }
}
