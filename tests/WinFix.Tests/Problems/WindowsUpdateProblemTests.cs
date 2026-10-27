using System.Text;
using WinFix.Problems;
using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Tests.Problems;

public class WindowsUpdateProblemTests
{
    private const string Distribution = @"C:\Windows\SoftwareDistribution";

    private static string ServiceConfig(string service, int startType, string label) =>
        $"[SC] QueryServiceConfig SUCCESS\n\nSERVICE_NAME: {service}\n        TYPE               : 20  WIN32_SHARE_PROCESS\n        START_TYPE         : {startType}   {label}\n";

    private static string FailureEvents(int count)
    {
        var text = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            text.Append("Event[").Append(i).Append("]:\n  Provider Name: Microsoft-Windows-WindowsUpdateClient\n  Event ID: 20\n  Level: Error\n\n");
        }

        return text.ToString();
    }

    private static FakeSystem Machine(string? disabled = null, int failures = 0)
    {
        return new FakeSystem
        {
            CommandResponder = (file, args) =>
            {
                if (file == "sc" && args.StartsWith("qc ", StringComparison.Ordinal))
                {
                    var service = args[3..];
                    return service == disabled
                        ? new CommandResult(0, ServiceConfig(service, 4, "DISABLED"))
                        : new CommandResult(0, ServiceConfig(service, 3, "DEMAND_START"));
                }

                if (file == "wevtutil")
                {
                    return new CommandResult(0, FailureEvents(failures));
                }

                return new CommandResult(0, string.Empty);
            },
        };
    }

    [Theory]
    [InlineData("wuauserv")]
    [InlineData("bits")]
    [InlineData("cryptsvc")]
    public async Task Disabled_service_is_unhealthy(string service)
    {
        var health = await new WindowsUpdateProblem().DetectAsync(Machine(disabled: service), CancellationToken.None);

        Assert.False(health.Healthy);
        Assert.Contains(service, health.Reason);
    }

    [Fact]
    public async Task Three_install_failures_are_unhealthy()
    {
        var health = await new WindowsUpdateProblem().DetectAsync(Machine(failures: 3), CancellationToken.None);

        Assert.False(health.Healthy);
        Assert.Contains("3", health.Reason);
    }

    [Fact]
    public async Task Two_install_failures_are_healthy()
    {
        var health = await new WindowsUpdateProblem().DetectAsync(Machine(failures: 2), CancellationToken.None);

        Assert.True(health.Healthy);
    }

    [Fact]
    public async Task Failure_query_covers_the_last_14_days_of_update_client_install_failures()
    {
        var fake = Machine();

        await new WindowsUpdateProblem().DetectAsync(fake, CancellationToken.None);

        var query = Assert.Single(fake.Calls, c => c.StartsWith("RunCommand wevtutil", StringComparison.Ordinal));
        Assert.Contains("1209600000", query);
        Assert.Contains("Microsoft-Windows-WindowsUpdateClient", query);
        Assert.Contains("EventID=20", query);
    }

    [Fact]
    public async Task Running_services_and_no_failures_are_healthy()
    {
        var health = await new WindowsUpdateProblem().DetectAsync(Machine(), CancellationToken.None);

        Assert.True(health.Healthy);
    }

    [Fact]
    public async Task Unreadable_service_and_event_log_do_not_count_as_broken()
    {
        var fake = new FakeSystem { CommandExitCode = 1060, CommandOutput = "The specified service does not exist." };

        var health = await new WindowsUpdateProblem().DetectAsync(fake, CancellationToken.None);

        Assert.True(health.Healthy);
    }

    [Fact]
    public void Module_is_named_windowsupdate_and_runs_the_reset_step()
    {
        var module = new WindowsUpdateProblem();

        Assert.Equal("windowsupdate", module.Name);
        Assert.IsType<ResetUpdateComponentsStep>(Assert.Single(module.Repairs));
    }

    [Fact]
    public async Task Reset_runs_the_exact_command_sequence()
    {
        var fake = new FakeSystem();

        var result = await new ResetUpdateComponentsStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        string[] expected =
        [
            "RunCommand net stop bits",
            "RunCommand net stop wuauserv",
            "RunCommand net stop cryptsvc",
            @"RunCommand cmd.exe /c del /f /q ""%ALLUSERSPROFILE%\Microsoft\Network\Downloader\qmgr*.dat""",
            $@"RunCommand cmd.exe /c if exist ""{Distribution}\DataStore.bak"" rd /s /q ""{Distribution}\DataStore.bak""",
            $@"RunCommand cmd.exe /c if exist ""{Distribution}\DataStore"" ren ""{Distribution}\DataStore"" DataStore.bak",
            $@"RunCommand cmd.exe /c if exist ""{Distribution}\Download.bak"" rd /s /q ""{Distribution}\Download.bak""",
            $@"RunCommand cmd.exe /c if exist ""{Distribution}\Download"" ren ""{Distribution}\Download"" Download.bak",
            @"RunCommand cmd.exe /c if exist ""C:\Windows\System32\catroot2.bak"" rd /s /q ""C:\Windows\System32\catroot2.bak""",
            @"RunCommand cmd.exe /c if exist ""C:\Windows\System32\catroot2"" ren ""C:\Windows\System32\catroot2"" catroot2.bak",
            "RunCommand sc config wuauserv start= demand",
            "RunCommand sc config bits start= delayed-auto",
            "RunCommand sc config cryptsvc start= auto",
            "RunCommand net start cryptsvc",
            "RunCommand net start bits",
            "RunCommand net start wuauserv",
        ];
        Assert.Equal(expected, fake.Calls);
    }

    [Fact]
    public async Task Services_that_were_already_stopped_do_not_fail_the_reset()
    {
        var fake = new FakeSystem
        {
            CommandResponder = (file, args) => file == "net" && args.StartsWith("stop ", StringComparison.Ordinal)
                ? new CommandResult(2, "The service is not started.")
                : new CommandResult(0, string.Empty),
        };

        var result = await new ResetUpdateComponentsStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("RunCommand net start wuauserv", fake.Calls);
    }

    [Fact]
    public async Task Failed_rename_fails_the_step_but_services_are_still_restarted()
    {
        var fake = new FakeSystem
        {
            CommandResponder = (_, args) => args.Contains(" ren ", StringComparison.Ordinal)
                ? new CommandResult(1, "Access is denied.")
                : new CommandResult(0, string.Empty),
        };

        var result = await new ResetUpdateComponentsStep().RunAsync(fake, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("RunCommand net start wuauserv", fake.Calls);
    }

    [Fact]
    public async Task Existing_bak_folder_is_removed_before_the_rename()
    {
        var fake = new FakeSystem();

        await new ResetUpdateComponentsStep().RunAsync(fake, CancellationToken.None);

        var calls = fake.Calls.ToList();
        var removeBak = calls.FindIndex(c => c.Contains(@"rd /s /q ""C:\Windows\System32\catroot2.bak""", StringComparison.Ordinal));
        var rename = calls.FindIndex(c => c.Contains(@"ren ""C:\Windows\System32\catroot2""", StringComparison.Ordinal));
        Assert.True(removeBak >= 0 && rename > removeBak);
    }

    [Fact]
    public async Task Paths_follow_the_windows_directory()
    {
        var fake = new FakeSystem { WindowsDirectory = @"D:\Win" };

        await new ResetUpdateComponentsStep().RunAsync(fake, CancellationToken.None);

        Assert.Contains(fake.Calls, c => c.Contains(@"ren ""D:\Win\System32\catroot2""", StringComparison.Ordinal));
        Assert.Contains(fake.Calls, c => c.Contains(@"ren ""D:\Win\SoftwareDistribution\Download""", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(@"C:\Win""&calc&""dows")]
    [InlineData(@"C:\Win%COMSPEC%dows")]
    public async Task A_crafted_windows_folder_fails_the_reset_without_running_anything(string windows)
    {
        var fake = new FakeSystem { WindowsDirectory = windows };

        var result = await new ResetUpdateComponentsStep().RunAsync(fake, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("RunCommand", StringComparison.Ordinal));
        Assert.Contains(fake.Logs, l => l.Contains(windows, StringComparison.Ordinal));
    }
}
