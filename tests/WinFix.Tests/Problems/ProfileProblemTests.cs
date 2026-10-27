using WinFix.Problems;
using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Tests.Problems;

public class ProfileProblemTests
{
    private const string List = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    private const string Sid = "S-1-5-21-1111-2222-3333-1001";
    private const string OtherSid = "S-1-5-21-1111-2222-3333-1002";
    private const string Real = @"C:\Users\alice";
    private const string Temp = @"C:\Users\TEMP";

    private static readonly DateTimeOffset Now = new(2026, 10, 27, 14, 30, 5, TimeSpan.Zero);

    private static FakeSystem Machine(
        string? sidPath = null,
        string? bakPath = null,
        bool realFolderExists = false,
        bool whoamiFails = false,
        bool exportFails = false,
        bool promoteFails = false,
        bool otherUserBak = false,
        string? whoami = null)
    {
        var system = new FakeSystem
        {
            CommandResponder = (file, args) =>
            {
                if (file == "whoami")
                {
                    return whoamiFails
                        ? new CommandResult(1, "ERROR")
                        : new CommandResult(0, whoami ?? $"\"desktop-1\\alice\",\"{Sid}\"\r\n");
                }

                if (file == "cmd.exe" && args.Contains("NTUSER.DAT", StringComparison.Ordinal))
                {
                    return new CommandResult(realFolderExists ? 0 : 1, string.Empty);
                }

                if (file == "cmd.exe" && args.Contains("reg export", StringComparison.Ordinal))
                {
                    return new CommandResult(exportFails ? 1 : 0, exportFails ? "Access denied" : string.Empty);
                }

                if (promoteFails && file == "reg" && args.StartsWith($"copy \"{List}\\{Sid}.bak\" \"{List}\\{Sid}\"", StringComparison.Ordinal))
                {
                    return new CommandResult(1, "copy failed");
                }

                return new CommandResult(0, string.Empty);
            },
        };

        if (sidPath is not null)
        {
            system.RegistryValues[($@"{List}\{Sid}", "ProfileImagePath")] = sidPath;
            system.RegistryValues[($@"{List}\{Sid}", "State")] = "0x8000";
        }

        if (bakPath is not null)
        {
            system.RegistryValues[($@"{List}\{Sid}.bak", "ProfileImagePath")] = bakPath;
        }

        if (otherUserBak)
        {
            system.RegistryValues[($@"{List}\{OtherSid}", "ProfileImagePath")] = @"C:\Users\bob";
            system.RegistryValues[($@"{List}\{OtherSid}.bak", "ProfileImagePath")] = @"C:\Users\bob";
        }

        return system;
    }

    private static Task<Health> Detect(FakeSystem system) => new ProfileProblem().DetectAsync(system, CancellationToken.None);

    private static Task<StepResult> Repair(FakeSystem system) => new RestoreProfileStep(() => Now).RunAsync(system, CancellationToken.None);

    private static bool IsChange(string call) =>
        call.StartsWith("RunCommand reg ", StringComparison.Ordinal)
        || call.StartsWith("DeleteRegistryKey", StringComparison.Ordinal)
        || call.StartsWith("WriteRegistry", StringComparison.Ordinal);

    private static int IndexOf(FakeSystem system, string prefix) =>
        system.Calls.ToList().FindIndex(c => c.StartsWith(prefix, StringComparison.Ordinal));

    [Fact]
    public void Module_is_named_profile_and_has_the_restore_step()
    {
        var module = new ProfileProblem();

        Assert.Equal("profile", module.Name);
        Assert.IsType<RestoreProfileStep>(Assert.Single(module.Repairs));
    }

    [Fact]
    public async Task A_normal_profile_is_healthy()
    {
        var health = await Detect(Machine(sidPath: Real));

        Assert.True(health.Healthy);
    }

    [Fact]
    public async Task Both_sid_and_bak_keys_are_unhealthy()
    {
        var health = await Detect(Machine(sidPath: Temp, bakPath: Real));

        Assert.False(health.Healthy);
        Assert.Contains(".bak", health.Reason);
    }

    [Theory]
    [InlineData(@"C:\Users\TEMP")]
    [InlineData(@"C:\Users\temp")]
    [InlineData(@"C:\Users\TEMP.DESKTOP-1")]
    [InlineData(@"C:\Users\TEMP\")]
    public async Task A_profile_path_under_users_temp_is_unhealthy(string path)
    {
        var health = await Detect(Machine(sidPath: path));

        Assert.False(health.Healthy);
        Assert.Contains("TEMP", health.Reason);
    }

    [Fact]
    public async Task A_folder_that_only_starts_with_temp_letters_is_healthy()
    {
        var health = await Detect(Machine(sidPath: @"C:\Users\Temperance"));

        Assert.True(health.Healthy);
    }

    [Fact]
    public async Task Another_users_bak_key_is_ignored()
    {
        var health = await Detect(Machine(sidPath: Real, otherUserBak: true));

        Assert.True(health.Healthy);
    }

    [Fact]
    public async Task An_unknown_current_user_is_reported_healthy_without_changes()
    {
        var system = Machine(sidPath: Temp, bakPath: Real, whoamiFails: true);

        var health = await Detect(system);

        Assert.True(health.Healthy);
        Assert.DoesNotContain(system.Calls, IsChange);
    }

    [Fact]
    public async Task Repair_exports_the_profile_list_before_any_change()
    {
        var system = Machine(sidPath: Temp, bakPath: Real);

        await Repair(system);

        var calls = system.Calls.ToList();
        var export = calls.First(c => c.Contains("reg export", StringComparison.Ordinal));
        Assert.Contains($"\"{List}\"", export);
        Assert.Contains(@"%ProgramData%\WinFix\ProfileList-20261027-143005.reg", export);
        Assert.Contains("/y", export);
        Assert.True(calls.IndexOf(export) < calls.FindIndex(IsChange));
    }

    [Fact]
    public async Task A_failed_export_stops_the_repair_with_no_change()
    {
        var system = Machine(sidPath: Temp, bakPath: Real, exportFails: true);

        var result = await Repair(system);

        Assert.False(result.Succeeded);
        Assert.False(result.RebootRequired);
        Assert.DoesNotContain(system.Calls, IsChange);
    }

    [Fact]
    public async Task Repair_moves_the_temp_key_aside_and_promotes_the_bak_key()
    {
        var system = Machine(sidPath: Temp, bakPath: Real);

        var result = await Repair(system);

        Assert.True(result.Succeeded);
        var copyAside = IndexOf(system, $"RunCommand reg copy \"{List}\\{Sid}\" \"{List}\\{Sid}.temp\" /s /f");
        var deleteSid = IndexOf(system, $"DeleteRegistryKey {List}\\{Sid}");
        var promote = IndexOf(system, $"RunCommand reg copy \"{List}\\{Sid}.bak\" \"{List}\\{Sid}\" /s /f");
        var deleteBak = IndexOf(system, $"DeleteRegistryKey {List}\\{Sid}.bak");
        Assert.True(copyAside >= 0);
        Assert.True(copyAside < deleteSid);
        Assert.True(deleteSid < promote);
        Assert.True(promote < deleteBak);
    }

    [Fact]
    public async Task Repair_resets_state_and_refcount_on_the_active_key()
    {
        var system = Machine(sidPath: Temp, bakPath: Real);

        await Repair(system);

        Assert.Contains($"RunCommand reg add \"{List}\\{Sid}\" /v State /t REG_DWORD /d 0 /f", system.Calls);
        Assert.Contains($"RunCommand reg add \"{List}\\{Sid}\" /v RefCount /t REG_DWORD /d 0 /f", system.Calls);
        var promote = IndexOf(system, $"RunCommand reg copy \"{List}\\{Sid}.bak\"");
        Assert.True(promote < IndexOf(system, "RunCommand reg add"));
    }

    [Fact]
    public async Task Repair_asks_for_a_reboot()
    {
        var result = await Repair(Machine(sidPath: Temp, bakPath: Real));

        Assert.True(result.Succeeded);
        Assert.True(result.RebootRequired);
    }

    [Fact]
    public async Task A_failed_promotion_puts_the_temp_key_back()
    {
        var system = Machine(sidPath: Temp, bakPath: Real, promoteFails: true);

        var result = await Repair(system);

        Assert.False(result.Succeeded);
        Assert.False(result.RebootRequired);
        Assert.Contains($"RunCommand reg copy \"{List}\\{Sid}.temp\" \"{List}\\{Sid}\" /s /f", system.Calls);
        Assert.DoesNotContain($"DeleteRegistryKey {List}\\{Sid}.bak", system.Calls);
    }

    [Fact]
    public async Task When_the_main_key_already_points_at_the_real_folder_only_state_is_reset()
    {
        var system = Machine(sidPath: Real, bakPath: Real);

        var result = await Repair(system);

        Assert.True(result.RebootRequired);
        Assert.DoesNotContain(system.Calls, c => c.StartsWith("DeleteRegistryKey", StringComparison.Ordinal));
        Assert.DoesNotContain(system.Calls, c => c.Contains("reg copy", StringComparison.Ordinal));
        Assert.Contains($"RunCommand reg add \"{List}\\{Sid}\" /v State /t REG_DWORD /d 0 /f", system.Calls);
    }

    [Fact]
    public async Task When_neither_key_points_at_a_real_folder_the_repair_fails_without_changes()
    {
        var system = Machine(sidPath: Temp, bakPath: @"C:\Users\TEMP.001");

        var result = await Repair(system);

        Assert.False(result.Succeeded);
        Assert.False(result.RebootRequired);
        Assert.DoesNotContain(system.Calls, IsChange);
    }

    [Fact]
    public async Task A_lone_temp_key_is_fixed_when_the_real_folder_exists()
    {
        var system = Machine(sidPath: Temp, realFolderExists: true);

        var result = await Repair(system);

        Assert.True(result.Succeeded);
        Assert.True(result.RebootRequired);
        Assert.Equal(Real, system.RegistryValues[($@"{List}\{Sid}", "ProfileImagePath")]);
        Assert.Contains($"RunCommand reg add \"{List}\\{Sid}\" /v RefCount /t REG_DWORD /d 0 /f", system.Calls);
        Assert.Contains(system.Calls, c => c.Contains(@"C:\Users\alice\NTUSER.DAT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_lone_temp_key_with_no_real_folder_fails_without_changes()
    {
        var system = Machine(sidPath: Temp, realFolderExists: false);

        var result = await Repair(system);

        Assert.False(result.Succeeded);
        Assert.False(result.RebootRequired);
        Assert.Equal(Temp, system.RegistryValues[($@"{List}\{Sid}", "ProfileImagePath")]);
        Assert.DoesNotContain(system.Calls, IsChange);
    }

    [Fact]
    public async Task An_unknown_current_user_fails_the_repair()
    {
        var system = Machine(sidPath: Temp, bakPath: Real, whoamiFails: true);

        var result = await Repair(system);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(system.Calls, c => c.Contains("reg export", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_user_name_with_a_percent_sign_is_never_put_in_a_cmd_line()
    {
        var system = Machine(sidPath: Temp, realFolderExists: true, whoami: $"\"desktop-1\\%COMSPEC%\",\"{Sid}\"\r\n");

        var result = await Repair(system);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(system.Calls, c => c.Contains("%COMSPEC%", StringComparison.Ordinal));
        Assert.DoesNotContain(system.Calls, IsChange);
        Assert.Equal(Temp, system.RegistryValues[($@"{List}\{Sid}", "ProfileImagePath")]);
    }

    [Fact]
    public async Task A_sid_with_non_ascii_digits_is_refused_without_changes()
    {
        const string crafted = "S-1-5-21-1111-2222-3333-\uFF11\uFF10\uFF10\uFF11";
        var system = Machine(whoami: $"\"desktop-1\\alice\",\"{crafted}\"\r\n");
        system.RegistryValues[($@"{List}\{crafted}", "ProfileImagePath")] = Temp;
        system.RegistryValues[($@"{List}\{crafted}.bak", "ProfileImagePath")] = Real;

        var result = await Repair(system);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(system.Calls, IsChange);
        Assert.DoesNotContain(system.Calls, c => c.Contains("reg export", StringComparison.Ordinal));
    }
}
