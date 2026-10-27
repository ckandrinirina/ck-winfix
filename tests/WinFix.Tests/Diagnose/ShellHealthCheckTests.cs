using WinFix.Diagnose;
using WinFix.System;

namespace WinFix.Tests.Diagnose;

public class ShellHealthCheckTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(20);

    private static async Task<(ShellHealth Health, FakeSystem Fake)> CheckAsync(
        bool taskbar, params int[][] explorerSamples)
    {
        var fake = new FakeSystem { ShellHealthy = taskbar, ExplorerProcessIdSamples = explorerSamples };
        var health = await new ShellHealthCheck(fake).CheckAsync(CancellationToken.None);
        return (health, fake);
    }

    [Fact]
    public async Task Missing_taskbar_is_unhealthy_and_the_reason_names_it()
    {
        var (health, fake) = await CheckAsync(taskbar: false, [100]);

        Assert.False(health.Healthy);
        Assert.Contains("Shell_TrayWnd", health.Reason);
        Assert.Contains(fake.Calls, c => c == "FindWindow Shell_TrayWnd");
    }

    [Fact]
    public async Task Missing_taskbar_is_reported_without_waiting_out_the_window()
    {
        var (_, fake) = await CheckAsync(taskbar: false, [100]);

        Assert.Equal(TimeSpan.Zero, fake.TotalDelay);
    }

    [Fact]
    public async Task Three_explorer_starts_in_the_window_is_unhealthy()
    {
        var (health, _) = await CheckAsync(taskbar: true, [100], [100], [200], [200], [300]);

        Assert.False(health.Healthy);
        Assert.Contains("explorer.exe", health.Reason);
        Assert.Contains("3", health.Reason);
    }

    [Fact]
    public async Task Two_explorer_starts_in_the_window_is_still_healthy()
    {
        var (health, _) = await CheckAsync(taskbar: true, [100], [200]);

        Assert.True(health.Healthy);
    }

    [Fact]
    public async Task Taskbar_and_a_stable_explorer_pid_is_healthy()
    {
        var (health, _) = await CheckAsync(taskbar: true, [4242]);

        Assert.True(health.Healthy);
        Assert.Contains("4242", health.Reason);
    }

    [Fact]
    public async Task Watch_window_is_sampled_for_its_whole_duration()
    {
        var (_, fake) = await CheckAsync(taskbar: true, [100]);

        Assert.Equal(Window, fake.TotalDelay);
    }

    [Fact]
    public async Task Watch_window_is_injectable()
    {
        var fake = new FakeSystem { ShellHealthy = true };
        var check = new ShellHealthCheck(fake, TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(2));

        await check.CheckAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(6), fake.TotalDelay);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_reason_is_written_to_the_log(bool taskbar)
    {
        var (health, fake) = await CheckAsync(taskbar, [100]);

        Assert.Contains(health.Reason, fake.Logs);
    }

    [Fact]
    public async Task The_crash_loop_reason_is_written_to_the_log()
    {
        var (health, fake) = await CheckAsync(taskbar: true, [1], [2], [3]);

        Assert.Contains(health.Reason, fake.Logs);
    }
}
