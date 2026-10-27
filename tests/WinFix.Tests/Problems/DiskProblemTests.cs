using WinFix.Pipeline;
using WinFix.Problems;
using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Tests.Problems;

public class DiskProblemTests
{
    private const string StatePath = @"%ProgramData%\WinFix\state.json";

    /// <summary>Scripts the detection and repair commands; each argument is the text that command prints.</summary>
    private static FakeSystem Disk(
        string fsutil = "Volume - C: is NOT Dirty",
        string chkdsk = "Windows has scanned the file system and found no problems.",
        string dism = "No component store corruption detected.",
        int chkdskExit = 0,
        string sfc = "Windows Resource Protection did not find any integrity violations.") =>
        new()
        {
            CommandResponder = (file, args) =>
            {
                if (file == "fsutil")
                {
                    return new CommandResult(0, fsutil);
                }

                if (file == "chkdsk" && args == "C: /scan")
                {
                    return new CommandResult(chkdskExit, chkdsk);
                }

                if (file == "DISM" && args.Contains("/CheckHealth"))
                {
                    return new CommandResult(0, dism);
                }

                if (file == "sfc")
                {
                    return new CommandResult(0, sfc);
                }

                return new CommandResult(0, string.Empty);
            },
        };

    [Fact]
    public async Task Healthy_when_no_signal_reports_a_problem()
    {
        var health = await new DiskProblem().DetectAsync(Disk(), CancellationToken.None);

        Assert.True(health.Healthy);
    }

    [Fact]
    public async Task Broken_when_the_volume_is_dirty()
    {
        var fake = Disk(fsutil: "Volume - C: is Dirty");

        var health = await new DiskProblem().DetectAsync(fake, CancellationToken.None);

        Assert.False(health.Healthy);
        Assert.Contains("RunCommand fsutil dirty query C:", fake.Calls);
    }

    [Fact]
    public async Task Broken_when_the_chkdsk_scan_reports_errors()
    {
        var fake = Disk(chkdsk: "Windows has scanned the file system and found problems.", chkdskExit: 1);

        var health = await new DiskProblem().DetectAsync(fake, CancellationToken.None);

        Assert.False(health.Healthy);
        Assert.Contains("RunCommand chkdsk C: /scan", fake.Calls);
    }

    [Fact]
    public async Task Broken_when_the_chkdsk_scan_prints_errors_found_with_exit_code_zero()
    {
        var fake = Disk(chkdsk: "Errors found.  CHKDSK cannot continue in read-only mode.");

        var health = await new DiskProblem().DetectAsync(fake, CancellationToken.None);

        Assert.False(health.Healthy);
    }

    [Fact]
    public async Task Broken_when_the_component_store_is_repairable()
    {
        var fake = Disk(dism: "The component store is repairable.");

        var health = await new DiskProblem().DetectAsync(fake, CancellationToken.None);

        Assert.False(health.Healthy);
        Assert.Contains("RunCommand DISM /Online /Cleanup-Image /CheckHealth", fake.Calls);
    }

    [Fact]
    public async Task Detection_stops_at_the_first_signal()
    {
        var fake = Disk(fsutil: "Volume - C: is Dirty");

        await new DiskProblem().DetectAsync(fake, CancellationToken.None);

        Assert.DoesNotContain(fake.Calls, c => c.Contains("chkdsk") || c.Contains("DISM"));
    }

    [Fact]
    public void Module_is_named_disk_and_repairs_run_dism_then_sfc_then_chkdsk()
    {
        var module = new DiskProblem();

        Assert.Equal("disk", module.Name);
        Assert.Collection(
            module.Repairs,
            r => Assert.IsType<DismRestoreHealthStep>(r),
            r => Assert.IsType<SfcScanStep>(r),
            r => Assert.IsType<ChkdskStep>(r));
    }

    [Fact]
    public async Task Chkdsk_step_schedules_the_check_with_a_yes_answer_and_asks_for_a_reboot()
    {
        var fake = new FakeSystem();

        var result = await new ChkdskStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.RebootRequired);
        Assert.Contains("RunCommand cmd /c echo Y| chkdsk C: /f", fake.Calls);
    }

    [Fact]
    public async Task Chkdsk_step_fails_without_a_reboot_when_the_command_fails()
    {
        var fake = new FakeSystem { CommandExitCode = 3 };

        var result = await new ChkdskStep().RunAsync(fake, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(3, result.ExitCode);
        Assert.False(result.RebootRequired);
    }

    [Fact]
    public async Task Pipeline_runs_dism_and_sfc_then_schedules_chkdsk_and_saves_state_for_resume()
    {
        var fake = Disk(fsutil: "Volume - C: is Dirty");

        var exit = await new FixPipeline(fake, new StringWriter(), modules: [new DiskProblem()]).RunAsync(CancellationToken.None);

        Assert.Equal(0, exit);
        var repairs = fake.Calls
            .Where(c => c.StartsWith("RunCommand DISM /Online /Cleanup-Image /RestoreHealth")
                || c.StartsWith("RunCommand sfc")
                || c.StartsWith("RunCommand cmd"))
            .ToList();
        Assert.Equal(
            ["RunCommand DISM /Online /Cleanup-Image /RestoreHealth", "RunCommand sfc /scannow", "RunCommand cmd /c echo Y| chkdsk C: /f"],
            repairs);
        Assert.Equal("Reboot", fake.Calls[^1]);
        Assert.Equal(new RunState(3, "disk"), RunState.Parse(fake.Files[StatePath]));
        Assert.Contains(fake.Calls, c => c.StartsWith($"WriteRegistry {RunState.RunOnceKey} {RunState.RunOnceName}="));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task After_the_resume_the_module_re_detects_before_any_repair_runs(int nextStage)
    {
        var fake = Disk(fsutil: "Volume - C: is Dirty");
        fake.Files[StatePath] = new RunState(nextStage, "disk").ToJson();

        await new FixPipeline(fake, new StringWriter(), readLine: () => "no", modules: [new DiskProblem()])
            .RunAsync(CancellationToken.None, resume: true);

        var commands = fake.Calls.Where(c => c.StartsWith("RunCommand")).ToList();
        Assert.StartsWith("RunCommand fsutil", commands[0]);
        Assert.DoesNotContain("RunCommand DISM /Online /Cleanup-Image /RestoreHealth", fake.Calls);
        Assert.DoesNotContain("RunCommand sfc /scannow", fake.Calls);
    }

    [Fact]
    public async Task After_the_resume_a_repaired_disk_needs_no_more_repairs()
    {
        var fake = Disk();
        fake.Files[StatePath] = new RunState(3, "disk").ToJson();
        var output = new StringWriter();

        var exit = await new FixPipeline(fake, output, modules: [new DiskProblem()]).RunAsync(CancellationToken.None, resume: true);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(fake.Calls, c => c.Contains("RestoreHealth") || c.Contains("sfc") || c.Contains("/f"));
        Assert.Contains("Disk is healthy", output.ToString());
        Assert.False(fake.Files.ContainsKey(StatePath));
    }
}
