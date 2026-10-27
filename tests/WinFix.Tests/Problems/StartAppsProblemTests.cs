using WinFix.Problems;
using WinFix.Repairs;
using WinFix.System;
using WinFix.Tests.System;

namespace WinFix.Tests.Problems;

public class StartAppsProblemTests
{
    private const string StartHost = "Microsoft.Windows.StartMenuExperienceHost";
    private const string Cbs = "MicrosoftWindows.Client.CBS";
    private const string Settings = "windows.immersivecontrolpanel";

    private static string Entry(string name, string status, string? location) =>
        $$"""{"Name":"{{name}}","Status":"{{status}}","InstallLocation":{{(location is null ? "null" : $"\"{location.Replace("\\", "\\\\")}\"")}}}""";

    private static string Location(string name) => $@"C:\Windows\SystemApps\{name}";

    private static string Json(params (string Name, string Status)[] packages) =>
        "[" + string.Join(",", packages.Select(p => Entry(p.Name, p.Status, Location(p.Name)))) + "]";

    private static FakeSystem WithPackages(string json) => new()
    {
        CommandResponder = (_, args) => args.Contains("Get-AppxPackage")
            ? new CommandResult(0, json)
            : new CommandResult(0, string.Empty),
    };

    private static IEnumerable<string> Registrations(FakeSystem fake) =>
        fake.Calls.Where(c => c.Contains("Add-AppxPackage"));

    [Fact]
    public async Task Healthy_when_all_three_packages_are_Ok()
    {
        var fake = WithPackages(Json((StartHost, "Ok"), (Cbs, "Ok"), (Settings, "Ok")));

        var health = await new StartAppsProblem().DetectAsync(fake, CancellationToken.None);

        Assert.True(health.Healthy);
    }

    [Fact]
    public async Task Broken_when_a_package_is_missing()
    {
        var fake = WithPackages(Json((StartHost, "Ok"), (Settings, "Ok")));

        var health = await new StartAppsProblem().DetectAsync(fake, CancellationToken.None);

        Assert.False(health.Healthy);
        Assert.Contains(Cbs, health.Reason);
    }

    [Theory]
    [InlineData("Modified")]
    [InlineData("DependencyIssue")]
    [InlineData("LicenseIssue")]
    public async Task Broken_when_a_package_is_not_in_the_Ok_status(string status)
    {
        var fake = WithPackages(Json((StartHost, "Ok"), (Cbs, "Ok"), (Settings, status)));

        var health = await new StartAppsProblem().DetectAsync(fake, CancellationToken.None);

        Assert.False(health.Healthy);
        Assert.Contains(Settings, health.Reason);
        Assert.DoesNotContain(StartHost, health.Reason);
    }

    [Fact]
    public async Task Broken_when_nothing_could_be_read()
    {
        var fake = new FakeSystem { CommandExitCode = 1, CommandOutput = "boom" };

        var health = await new StartAppsProblem().DetectAsync(fake, CancellationToken.None);

        Assert.False(health.Healthy);
    }

    [Fact]
    public async Task A_single_package_PowerShell_prints_as_an_object_not_an_array_is_read()
    {
        var fake = WithPackages(Entry(StartHost, "Ok", Location(StartHost)));

        var health = await new StartAppsProblem().DetectAsync(fake, CancellationToken.None);

        Assert.False(health.Healthy);
        Assert.DoesNotContain(StartHost, health.Reason);
        Assert.Contains(Cbs, health.Reason);
    }

    [Fact]
    public async Task A_package_with_one_Ok_copy_is_not_broken()
    {
        var fake = WithPackages(Json((StartHost, "Ok"), (Cbs, "Modified"), (Cbs, "Ok"), (Settings, "Ok")));

        var health = await new StartAppsProblem().DetectAsync(fake, CancellationToken.None);

        Assert.True(health.Healthy);
    }

    [Fact]
    public void Module_is_named_startapps_and_has_the_re_register_step()
    {
        var module = new StartAppsProblem();

        Assert.Equal("startapps", module.Name);
        Assert.Single(module.Repairs, r => r is ReRegisterSystemAppsStep);
    }

    [Fact]
    public async Task Step_re_registers_a_broken_package_from_its_install_location()
    {
        var fake = WithPackages(Json((StartHost, "Ok"), (Cbs, "Ok"), (Settings, "Modified")));

        var result = await new ReRegisterSystemAppsStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        var call = Assert.Single(Registrations(fake));
        Assert.Contains("Add-AppxPackage -DisableDevelopmentMode -Register", call);
        Assert.Contains($@"{Location(Settings)}\AppxManifest.xml", call);
    }

    [Fact]
    public async Task Step_re_registers_only_the_broken_packages()
    {
        var fake = WithPackages(Json((StartHost, "Modified"), (Cbs, "Ok"), (Settings, "DependencyIssue")));

        var result = await new ReRegisterSystemAppsStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        var calls = Registrations(fake).ToList();
        Assert.Equal(2, calls.Count);
        Assert.Contains(calls, c => c.Contains(Location(StartHost)));
        Assert.Contains(calls, c => c.Contains(Location(Settings)));
        Assert.DoesNotContain(calls, c => c.Contains(Location(Cbs)));
    }

    [Fact]
    public async Task Step_registers_nothing_when_every_package_is_Ok()
    {
        var fake = WithPackages(Json((StartHost, "Ok"), (Cbs, "Ok"), (Settings, "Ok")));

        var result = await new ReRegisterSystemAppsStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(Registrations(fake));
    }

    [Fact]
    public async Task Step_logs_and_fails_a_missing_package_but_still_repairs_the_others()
    {
        var fake = WithPackages(Json((StartHost, "Modified"), (Settings, "Ok")));

        var result = await new ReRegisterSystemAppsStep().RunAsync(fake, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(Cbs, string.Join('\n', fake.Logs));
        var call = Assert.Single(Registrations(fake));
        Assert.Contains(Location(StartHost), call);
    }

    [Fact]
    public async Task Step_fails_when_a_registration_command_fails()
    {
        var fake = new FakeSystem
        {
            CommandResponder = (_, args) => args.Contains("Get-AppxPackage")
                ? new CommandResult(0, Json((StartHost, "Modified"), (Cbs, "Ok"), (Settings, "Ok")))
                : new CommandResult(1, "denied"),
        };

        var result = await new ReRegisterSystemAppsStep().RunAsync(fake, CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Step_escapes_a_single_quote_in_the_install_location()
    {
        var json = "[" + Entry(StartHost, "Modified", @"C:\Odd'Path\Host") + "," + Entry(Cbs, "Ok", Location(Cbs))
            + "," + Entry(Settings, "Ok", Location(Settings)) + "]";
        var fake = WithPackages(json);

        await new ReRegisterSystemAppsStep().RunAsync(fake, CancellationToken.None);

        Assert.Contains(@"C:\Odd''Path\Host\AppxManifest.xml", Assert.Single(Registrations(fake)));
    }

    [Theory]
    [InlineData("'")]
    [InlineData("\u2018")]
    [InlineData("\u2019")]
    [InlineData("\u201A")]
    [InlineData("\u201B")]
    public async Task Step_keeps_any_quote_in_the_install_location_inside_the_literal(string quote)
    {
        var location = $@"C:\Odd{quote};calc;{quote}Path\Host";
        var json = "[" + Entry(StartHost, "Modified", location) + "," + Entry(Cbs, "Ok", Location(Cbs))
            + "," + Entry(Settings, "Ok", Location(Settings)) + "]";
        var fake = WithPackages(json);

        await new ReRegisterSystemAppsStep().RunAsync(fake, CancellationToken.None);

        var registered = PsLiteral.After(Assert.Single(Registrations(fake)), "-Register ");
        Assert.Equal($@"{location}\AppxManifest.xml", registered.Value);
        Assert.Equal("\"", registered.Tail);
    }

    [Fact]
    public async Task Step_refuses_an_install_location_with_a_double_quote()
    {
        var json = "[" + """{"Name":"Microsoft.Windows.StartMenuExperienceHost","Status":"Modified","InstallLocation":"C:\\Odd\";calc;\"Path"}"""
            + "," + Entry(Cbs, "Ok", Location(Cbs)) + "," + Entry(Settings, "Ok", Location(Settings)) + "]";
        var fake = WithPackages(json);

        var result = await new ReRegisterSystemAppsStep().RunAsync(fake, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(Registrations(fake));
        Assert.DoesNotContain(fake.Calls, c => c.Contains("calc", StringComparison.Ordinal));
        Assert.Contains(StartHost, string.Join('\n', fake.Logs));
    }
}
