using WinFix.Problems;
using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Tests.Problems;

public class NetworkProblemTests
{
    private static FakeSystem Machine(bool adapterUp, bool dnsWorks, bool tcpWorks)
    {
        return new FakeSystem
        {
            CommandResponder = (file, args) =>
            {
                if (file != "powershell")
                {
                    return new CommandResult(0, string.Empty);
                }

                if (args.Contains("Get-NetAdapter", StringComparison.Ordinal))
                {
                    return new CommandResult(0, adapterUp ? "1\r\n" : "0\r\n");
                }

                if (args.Contains("Resolve-DnsName", StringComparison.Ordinal))
                {
                    return new CommandResult(0, dnsWorks ? "True\r\n" : "False\r\n");
                }

                if (args.Contains("Test-NetConnection", StringComparison.Ordinal))
                {
                    return new CommandResult(0, tcpWorks ? "True\r\n" : "False\r\n");
                }

                return new CommandResult(0, string.Empty);
            },
        };
    }

    [Fact]
    public async Task Adapter_up_with_neither_dns_nor_tcp_is_unhealthy()
    {
        var health = await new NetworkProblem().DetectAsync(Machine(true, false, false), CancellationToken.None);

        Assert.False(health.Healthy);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Adapter_up_with_dns_or_tcp_working_is_healthy(bool dns, bool tcp)
    {
        var health = await new NetworkProblem().DetectAsync(Machine(true, dns, tcp), CancellationToken.None);

        Assert.True(health.Healthy);
    }

    [Fact]
    public async Task No_adapter_up_reports_it_and_is_not_repaired()
    {
        var fake = Machine(false, false, false);

        var health = await new NetworkProblem().DetectAsync(fake, CancellationToken.None);

        Assert.True(health.Healthy);
        Assert.Equal("No network adapter connected", health.Reason);
        Assert.DoesNotContain(fake.Calls, c => c.Contains("Resolve-DnsName", StringComparison.Ordinal));
        Assert.DoesNotContain(fake.Calls, c => c.Contains("Test-NetConnection", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unreadable_probes_do_not_count_as_broken()
    {
        var fake = new FakeSystem { CommandExitCode = 1, CommandOutput = "error" };

        var health = await new NetworkProblem().DetectAsync(fake, CancellationToken.None);

        Assert.True(health.Healthy);
    }

    [Fact]
    public async Task Probes_target_microsoft_on_port_443()
    {
        var fake = Machine(true, true, true);

        await new NetworkProblem().DetectAsync(fake, CancellationToken.None);

        Assert.Contains(fake.Calls, c => c.Contains("Resolve-DnsName", StringComparison.Ordinal) && c.Contains("www.microsoft.com", StringComparison.Ordinal));
        Assert.Contains(fake.Calls, c => c.Contains("Test-NetConnection", StringComparison.Ordinal) && c.Contains("www.microsoft.com", StringComparison.Ordinal) && c.Contains("-Port 443", StringComparison.Ordinal));
    }

    [Fact]
    public void Module_is_named_network_and_runs_the_reset_step()
    {
        var module = new NetworkProblem();

        Assert.Equal("network", module.Name);
        Assert.IsType<ResetNetworkStackStep>(Assert.Single(module.Repairs));
    }

    [Fact]
    public async Task Reset_runs_the_three_commands_in_order_and_requests_a_reboot()
    {
        var fake = new FakeSystem();

        var result = await new ResetNetworkStackStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.RebootRequired);
        string[] expected =
        [
            "RunCommand ipconfig /flushdns",
            "RunCommand netsh winsock reset",
            "RunCommand netsh int ip reset",
        ];
        Assert.Equal(expected, fake.Calls);
    }

    [Fact]
    public async Task Failed_command_fails_the_step_but_the_others_still_run()
    {
        var fake = new FakeSystem
        {
            CommandResponder = (file, _) => file == "ipconfig" ? new CommandResult(1, "denied") : new CommandResult(0, string.Empty),
        };

        var result = await new ResetNetworkStackStep().RunAsync(fake, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("RunCommand netsh int ip reset", fake.Calls);
    }
}
