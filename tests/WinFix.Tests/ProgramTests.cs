using WinFix.Pipeline;
using WinFix.System;

namespace WinFix.Tests;

public class ProgramTests
{
    private const string StatePath = @"%ProgramData%\WinFix\state.json";

    private static readonly string[] ModuleOrder = ["shell", "windowsupdate", "bluescreen", "disk", "startapps", "profile", "network"];

    [Fact]
    public void Dry_run_wires_the_fake_system()
    {
        Assert.IsType<FakeSystem>(Program.CreateSystem(dryRun: true));
    }

    [Fact]
    public void Without_dry_run_wires_the_windows_system()
    {
        Assert.IsType<WindowsSystem>(Program.CreateSystem(dryRun: false));
    }

    [Fact]
    public void Default_modules_are_all_seven_problem_modules_in_order()
    {
        Assert.Equal(ModuleOrder, Program.DefaultModules().Select(m => m.Name));
    }

    [Fact]
    public async Task Unknown_module_lists_all_seven_names()
    {
        var output = new StringWriter();

        var exit = await Program.RunAsync(["--dry-run", "--only", "nope"], output);

        Assert.Equal(2, exit);
        Assert.All(ModuleOrder, name => Assert.Contains(name, output.ToString()));
    }

    [Fact]
    public async Task Without_dry_run_only_runs_the_named_module_and_every_failed_reset_exits_4()
    {
        var fake = new FakeSystem { CommandExitCode = 1 };
        var output = new StringWriter();

        var exit = await Program.RunAsync(["--only", "shell"], output, isAdmin: () => true, system: fake, readLine: () => "YES");

        Assert.Equal(4, exit);
        var text = output.ToString();
        Assert.Contains("Shell: Still broken", text);
        Assert.DoesNotContain("Windowsupdate", text);
        Assert.DoesNotContain("Dry run", text);
        Assert.Contains(fake.Calls, c => c.StartsWith("RunCommand reagentc /boottore"));
        Assert.DoesNotContain(fake.Calls, c => c == "Reboot");
    }

    [Fact]
    public async Task Without_dry_run_a_started_reset_exits_0_without_the_simulated_reboot()
    {
        var fake = new FakeSystem();
        var output = new StringWriter();

        var exit = await Program.RunAsync(["--only", "shell"], output, isAdmin: () => true, system: fake, readLine: () => "YES");

        Assert.Equal(0, exit);
        Assert.Contains(fake.Calls, c => c.StartsWith("RunCommand shutdown /r /o"));
        Assert.DoesNotContain(fake.Calls, c => c == "Reboot");
    }

    [Fact]
    public async Task Dry_run_reset_stays_simulated()
    {
        var fake = new FakeSystem();
        var output = new StringWriter();

        var exit = await Program.RunAsync(["--dry-run", "--only", "shell"], output, system: fake);

        Assert.Equal(0, exit);
        Assert.Contains(fake.Calls, c => c == "Reboot");
        Assert.DoesNotContain(fake.Calls, c => c.Contains("reagentc") || c.Contains("schtasks"));
    }

    [Fact]
    public async Task Without_dry_run_declining_the_reset_exits_3()
    {
        var output = new StringWriter();

        var exit = await Program.RunAsync(["--only", "shell"], output, isAdmin: () => true, system: new FakeSystem(), readLine: () => "no");

        Assert.Equal(3, exit);
    }

    [Fact]
    public async Task Without_dry_run_resume_skips_the_steps_already_done()
    {
        var fake = new FakeSystem();
        fake.Files[StatePath] = new RunState(1).ToJson();
        var output = new StringWriter();

        var exit = await Program.RunAsync(["--resume", "--only", "shell"], output, isAdmin: () => true, system: fake, readLine: () => "no");

        Assert.Equal(3, exit);
        Assert.DoesNotContain("Running: Restart Explorer", output.ToString());
        Assert.Contains("Running: Clear IrisService", output.ToString());
    }

    [Fact]
    public async Task Without_dry_run_the_admin_check_still_comes_first()
    {
        var fake = new FakeSystem();
        var output = new StringWriter();

        var exit = await Program.RunAsync(["--only", "shell"], output, isAdmin: () => false, system: fake);

        Assert.Equal(2, exit);
        Assert.Empty(fake.Calls);
    }
}
