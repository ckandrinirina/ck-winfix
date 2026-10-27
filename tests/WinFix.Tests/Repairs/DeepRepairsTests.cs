using WinFix.Pipeline;
using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Tests.Repairs;

public class DeepRepairsTests
{
    private const string StatePath = @"%ProgramData%\WinFix\state.json";
    private const string BackupRoot = @"HKLM\Software\WinFix\ShellExtensionBackup";
    private const string FileHandlers = @"HKLM\SOFTWARE\Classes\*\shellex\ContextMenuHandlers";
    private const string FolderHandlers = @"HKLM\SOFTWARE\Classes\Directory\shellex\ContextMenuHandlers";

    private const string SfcRepaired = "Windows Resource Protection found corrupt files and successfully repaired them.";
    private const string SfcClean = "Windows Resource Protection did not find any integrity violations.";
    private const string SfcUnfixed = "Windows Resource Protection found corrupt files but was unable to fix some of them.";
    private const string SfcCannotRun = "Windows Resource Protection could not perform the requested operation.";

    private static string Utf16Noise(string text) => string.Join('\0', text.Select(c => c.ToString())) + "\0";

    private static void AddHandler(FakeSystem fake, string handlersKey, string name, string clsid, string? dll)
    {
        fake.RegistryValues[($@"{handlersKey}\{name}", "")] = clsid;
        if (dll is not null)
        {
            fake.RegistryValues[($@"HKLM\SOFTWARE\Classes\CLSID\{clsid}\InprocServer32", "")] = dll;
        }
    }

    [Fact]
    public async Task Dism_step_runs_restore_health_online()
    {
        var fake = new FakeSystem();

        var result = await new DismRestoreHealthStep().RunAsync(fake, CancellationToken.None);

        Assert.Contains("RunCommand DISM /Online /Cleanup-Image /RestoreHealth", fake.Calls);
        Assert.True(result.Succeeded);
        Assert.False(result.RebootRequired);
    }

    [Fact]
    public async Task Dism_step_maps_a_nonzero_exit_code_to_a_failure_and_logs_the_code()
    {
        var fake = new FakeSystem { CommandExitCode = 87 };

        var result = await new DismRestoreHealthStep().RunAsync(fake, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(87, result.ExitCode);
        Assert.Contains(fake.Logs, l => l.Contains("87"));
    }

    [Fact]
    public async Task Sfc_step_runs_scannow()
    {
        var fake = new FakeSystem { CommandOutput = SfcClean };

        await new SfcScanStep().RunAsync(fake, CancellationToken.None);

        Assert.Contains("RunCommand sfc /scannow", fake.Calls);
    }

    [Fact]
    public async Task Sfc_repaired_files_is_a_success_that_needs_a_reboot()
    {
        var fake = new FakeSystem { CommandOutput = SfcRepaired };

        var result = await new SfcScanStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.RebootRequired);
    }

    [Fact]
    public async Task Sfc_output_in_utf16_with_nul_characters_is_still_understood()
    {
        var fake = new FakeSystem { CommandOutput = Utf16Noise(SfcRepaired) };

        var result = await new SfcScanStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(result.RebootRequired);
    }

    [Fact]
    public async Task Sfc_without_violations_is_a_success_without_reboot()
    {
        var fake = new FakeSystem { CommandOutput = SfcClean };

        var result = await new SfcScanStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(result.RebootRequired);
    }

    [Theory]
    [InlineData(SfcUnfixed)]
    [InlineData(SfcCannotRun)]
    public async Task Sfc_that_cannot_fix_or_run_is_a_failure_without_reboot(string output)
    {
        var fake = new FakeSystem { CommandOutput = output };

        var result = await new SfcScanStep().RunAsync(fake, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(result.RebootRequired);
    }

    [Fact]
    public async Task Sfc_nonzero_exit_code_with_unknown_output_is_a_failure_with_that_code()
    {
        var fake = new FakeSystem { CommandExitCode = 9, CommandOutput = "???" };

        var result = await new SfcScanStep().RunAsync(fake, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(9, result.ExitCode);
    }

    [Fact]
    public void Sfc_parse_names_each_outcome()
    {
        Assert.Equal(SfcOutcome.Repaired, SfcScanStep.Parse(SfcRepaired));
        Assert.Equal(SfcOutcome.NoViolations, SfcScanStep.Parse(SfcClean));
        Assert.Equal(SfcOutcome.Unrepaired, SfcScanStep.Parse(SfcUnfixed));
        Assert.Equal(SfcOutcome.CouldNotRun, SfcScanStep.Parse(SfcCannotRun));
        Assert.Equal(SfcOutcome.Unknown, SfcScanStep.Parse(string.Empty));
    }

    [Fact]
    public async Task Extension_step_moves_a_non_microsoft_handler_to_the_backup_key()
    {
        var fake = new FakeSystem();
        AddHandler(fake, FileHandlers, "FooShell", "{AAAA}", @"C:\Program Files\Foo\foo.dll");

        var result = await new ShellExtensionsStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(fake.RegistryValues.Keys, k => k.Key.StartsWith($@"{FileHandlers}\FooShell"));
        var backup = Assert.Single(fake.RegistryValues, v => v.Key.Key.StartsWith(BackupRoot) && v.Key.Name == "Clsid");
        Assert.Equal("{AAAA}", backup.Value);
        Assert.Contains(fake.RegistryValues, v => v.Key.Key.StartsWith(BackupRoot) && v.Key.Name == "Key" && v.Value == FileHandlers);
        Assert.Contains(fake.RegistryValues, v => v.Key.Key.StartsWith(BackupRoot) && v.Key.Name == "Name" && v.Value == "FooShell");
    }

    [Fact]
    public async Task Extension_step_leaves_handlers_under_the_windows_directory_alone()
    {
        var fake = new FakeSystem();
        AddHandler(fake, FileHandlers, "Sendto", "{BBBB}", @"C:\WINDOWS\system32\shell32.dll");
        AddHandler(fake, FolderHandlers, "Sharing", "{CCCC}", @"c:\windows\System32\ntshrui.dll");

        await new ShellExtensionsStep().RunAsync(fake, CancellationToken.None);

        Assert.Equal("{BBBB}", fake.RegistryValues[($@"{FileHandlers}\Sendto", "")]);
        Assert.Equal("{CCCC}", fake.RegistryValues[($@"{FolderHandlers}\Sharing", "")]);
        Assert.DoesNotContain(fake.RegistryValues.Keys, k => k.Key.StartsWith(BackupRoot));
    }

    [Fact]
    public async Task Extension_step_leaves_a_handler_alone_when_its_dll_path_is_unknown()
    {
        var fake = new FakeSystem();
        AddHandler(fake, FileHandlers, "Mystery", "{DDDD}", dll: null);

        await new ShellExtensionsStep().RunAsync(fake, CancellationToken.None);

        Assert.Equal("{DDDD}", fake.RegistryValues[($@"{FileHandlers}\Mystery", "")]);
    }

    [Fact]
    public async Task Extension_step_with_no_handlers_succeeds_and_changes_nothing()
    {
        var fake = new FakeSystem();

        var result = await new ShellExtensionsStep().RunAsync(fake, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(fake.RegistryValues);
    }

    [Fact]
    public async Task Backup_is_restorable_and_restoring_removes_the_backup()
    {
        var fake = new FakeSystem();
        AddHandler(fake, FileHandlers, "FooShell", "{AAAA}", @"C:\Program Files\Foo\foo.dll");
        AddHandler(fake, FolderHandlers, "BarShell", "{EEEE}", @"D:\Bar\bar.dll");
        AddHandler(fake, FolderHandlers, "Sharing", "{CCCC}", @"C:\Windows\System32\ntshrui.dll");
        await new ShellExtensionsStep().RunAsync(fake, CancellationToken.None);

        var restored = ShellExtensionsStep.Restore(fake);

        Assert.Equal(2, restored);
        Assert.Equal("{AAAA}", fake.RegistryValues[($@"{FileHandlers}\FooShell", "")]);
        Assert.Equal("{EEEE}", fake.RegistryValues[($@"{FolderHandlers}\BarShell", "")]);
        Assert.DoesNotContain(fake.RegistryValues.Keys, k => k.Key.StartsWith(BackupRoot));
    }

    [Fact]
    public async Task Running_the_extension_step_twice_keeps_the_first_backup()
    {
        var fake = new FakeSystem();
        AddHandler(fake, FileHandlers, "FooShell", "{AAAA}", @"C:\Program Files\Foo\foo.dll");
        await new ShellExtensionsStep().RunAsync(fake, CancellationToken.None);

        await new ShellExtensionsStep().RunAsync(fake, CancellationToken.None);

        Assert.Equal(1, ShellExtensionsStep.Restore(fake));
        Assert.Equal("{AAAA}", fake.RegistryValues[($@"{FileHandlers}\FooShell", "")]);
    }

    [Fact]
    public void Restore_without_a_backup_restores_nothing()
    {
        Assert.Equal(0, ShellExtensionsStep.Restore(new FakeSystem()));
    }

    private sealed class RebootResultStep(string name) : IRepairStep
    {
        public string Name => name;

        public Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
        {
            system.StartProcess($"step-{name}");
            return Task.FromResult(new StepResult(0, "Repaired.", RebootRequired: true));
        }
    }

    private sealed class MarkerStep(string name) : IRepairStep
    {
        public string Name => name;

        public Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
        {
            system.StartProcess($"step-{name}");
            return Task.FromResult(new StepResult(0, $"{name} done."));
        }
    }

    private static async Task<(int Exit, string Output)> RunAsync(
        FakeSystem fake, IReadOnlyList<IRepairStep>? steps = null, bool resume = false)
    {
        var output = new StringWriter();
        var exit = await new FixPipeline(fake, output, steps: steps, readLine: () => "YES").RunAsync(CancellationToken.None, resume);
        return (exit, output.ToString());
    }

    [Fact]
    public async Task Result_that_requests_a_reboot_saves_the_next_stage_and_reboots_through_the_system()
    {
        var fake = new FakeSystem { ExecutablePath = @"C:\Tools\WinFix.exe" };
        IRepairStep[] steps = [new MarkerStep("A"), new RebootResultStep("B"), new MarkerStep("C")];

        var (exit, _) = await RunAsync(fake, steps);

        Assert.Equal(0, exit);
        Assert.Equal(2, RunState.Parse(fake.Files[StatePath])!.NextStage);
        Assert.Contains(fake.RegistryValues, v => v.Key.Name == "WinFix" && v.Value.Contains("--resume"));
        var calls = fake.Calls.ToList();
        Assert.True(calls.IndexOf("Reboot") > calls.IndexOf("StartProcess step-B"));
        Assert.DoesNotContain("StartProcess step-C", calls);
    }

    [Fact]
    public async Task After_resume_the_health_check_runs_before_any_remaining_step()
    {
        var fake = new FakeSystem();
        fake.Files[StatePath] = new RunState(2).ToJson();
        IRepairStep[] steps = [new MarkerStep("A"), new RebootResultStep("B"), new MarkerStep("C")];

        await RunAsync(fake, steps, resume: true);

        var calls = fake.Calls.ToList();
        var firstCheck = calls.FindIndex(c => c.StartsWith("FindWindow"));
        var stepC = calls.IndexOf("StartProcess step-C");
        Assert.True(firstCheck >= 0 && stepC > firstCheck);
        Assert.DoesNotContain("StartProcess step-A", calls);
        Assert.DoesNotContain("StartProcess step-B", calls);
    }

    [Fact]
    public async Task After_resume_a_healthy_shell_ends_the_run_without_running_a_step()
    {
        var fake = new FakeSystem { ShellHealthy = true };
        fake.Files[StatePath] = new RunState(2).ToJson();

        var (exit, _) = await RunAsync(fake, [new MarkerStep("A"), new RebootResultStep("B"), new MarkerStep("C")], resume: true);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("StartProcess step-"));
        Assert.DoesNotContain(StatePath, fake.Files.Keys);
    }

    [Fact]
    public async Task Default_pipeline_runs_the_deep_steps_after_the_quick_repairs()
    {
        var fake = new FakeSystem();

        await RunAsync(fake);

        var calls = fake.Calls.ToList();
        var shellApps = calls.FindIndex(c => c.StartsWith("RunCommand powershell.exe"));
        var dism = calls.IndexOf("RunCommand DISM /Online /Cleanup-Image /RestoreHealth");
        var sfc = calls.IndexOf("RunCommand sfc /scannow");
        Assert.True(shellApps >= 0 && dism > shellApps && sfc > dism, string.Join("\n", calls));
    }

    [Fact]
    public async Task Default_pipeline_reboots_after_sfc_repairs_files_and_skips_the_extension_step()
    {
        var fake = new FakeSystem { CommandOutput = SfcRepaired };
        AddHandler(fake, FileHandlers, "FooShell", "{AAAA}", @"C:\Program Files\Foo\foo.dll");

        await RunAsync(fake);

        Assert.Equal(5, RunState.Parse(fake.Files[StatePath])!.NextStage);
        Assert.Contains("Reboot", fake.Calls);
        Assert.Equal("{AAAA}", fake.RegistryValues[($@"{FileHandlers}\FooShell", "")]);
    }

    [Fact]
    public void Deep_steps_are_named_in_plain_english()
    {
        Assert.Equal("Repair Windows image", new DismRestoreHealthStep().Name);
        Assert.Equal("Scan system files", new SfcScanStep().Name);
        Assert.Equal("Disable add-on shell extensions", new ShellExtensionsStep().Name);
    }
}
