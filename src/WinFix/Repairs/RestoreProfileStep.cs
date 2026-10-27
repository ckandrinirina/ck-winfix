using System.Text.RegularExpressions;
using WinFix.Problems;
using WinFix.System;

namespace WinFix.Repairs;

/// <summary>
/// Makes the key that points at the real profile folder the active one. With both &lt;SID&gt; (temporary) and &lt;SID&gt;.bak (real),
/// the temporary key is moved to &lt;SID&gt;.temp and .bak is promoted. With a lone temporary key, the path is fixed only when
/// C:\Users\&lt;account&gt;\NTUSER.DAT exists. Reboots afterwards.
/// </summary>
public sealed partial class RestoreProfileStep(Func<DateTimeOffset>? clock = null) : IRepairStep
{
    private const string ListKey = ProfileProblem.ProfileListKey;

    // Left for cmd.exe to expand, so it never goes through CommandArgs.
    private const string ExportFolder = @"%ProgramData%\WinFix";

    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);

    public string Name => "Restore the real user profile";

    public async Task<StepResult> RunAsync(ISystem system, CancellationToken ct)
    {
        var user = await ProfileProblem.CurrentUser.ReadAsync(system, ct);
        if (user is null || !Sid().IsMatch(user.Sid))
        {
            return Fail(system, "The current user could not be identified.");
        }

        var key = $@"{ListKey}\{user.Sid}";
        var bakKey = key + ".bak";
        var keys = system.GetRegistrySubKeyNames(ListKey);
        var hasKey = keys.Contains(user.Sid, StringComparer.OrdinalIgnoreCase);
        var hasBak = keys.Contains(user.Sid + ".bak", StringComparer.OrdinalIgnoreCase);
        var keyIsReal = IsRealPath(system.ReadRegistryValue(key, ProfileProblem.ImagePathValue));
        var bakIsReal = hasBak && IsRealPath(system.ReadRegistryValue(bakKey, ProfileProblem.ImagePathValue));

        if (!hasKey && !bakIsReal)
        {
            return Fail(system, "The profile list has no key for this user.");
        }

        var swap = hasKey && !keyIsReal && bakIsReal;
        string? realPath = null;
        if (hasKey && !keyIsReal && !bakIsReal)
        {
            realPath = await FindRealFolderAsync(system, user, ct);
            if (realPath is null)
            {
                return Fail(system, "No real profile folder was found for this user, so the profile was left as it is.");
            }
        }

        var export = await Export(system, ct);
        if (export.ExitCode != 0)
        {
            return Fail(system, $"The profile list could not be saved first: {export.Output.Trim()}", export.ExitCode);
        }

        if (swap)
        {
            var moved = await MoveBakToKeyAsync(system, key, bakKey, ct);
            if (moved is not null)
            {
                return moved;
            }
        }
        else if (realPath is not null)
        {
            system.WriteRegistryValue(key, ProfileProblem.ImagePathValue, realPath);
        }

        foreach (var name in new[] { "State", "RefCount" })
        {
            var reset = await Reg(system, $"add {CommandArgs.QuoteCmd(key)} /v {name} /t REG_DWORD /d 0 /f", ct);
            if (reset.ExitCode != 0)
            {
                return Fail(system, $"{name} could not be reset: {reset.Output.Trim()}", reset.ExitCode);
            }
        }

        system.Log($"Restored the user profile for {user.Sid}");
        return new StepResult(0, "The user profile was restored. Windows must restart.", RebootRequired: true);
    }

    private static bool IsRealPath(string? path) => !string.IsNullOrWhiteSpace(path) && !ProfileProblem.IsTempPath(path);

    private static StepResult Fail(ISystem system, string message, int exitCode = 1)
    {
        system.Log(message);
        return new StepResult(exitCode, message);
    }

    private static Task<CommandResult> Reg(ISystem system, string arguments, CancellationToken ct) =>
        system.RunCommandAsync("reg", arguments, ct);

    private Task<CommandResult> Export(ISystem system, CancellationToken ct)
    {
        // The only non-constant part is the time, formatted as ASCII digits.
        return system.RunCommandAsync(
            "cmd.exe",
            $"/c (if not exist \"{ExportFolder}\" mkdir \"{ExportFolder}\") && reg export \"{ListKey}\" \"{ExportFolder}\\ProfileList-{_clock():yyyyMMdd-HHmmss}.reg\" /y",
            ct);
    }

    /// <summary>Null on success; on failure the temporary key is put back and the failure returned.</summary>
    private static async Task<StepResult?> MoveBakToKeyAsync(ISystem system, string key, string bakKey, CancellationToken ct)
    {
        var tempKey = key + ".temp";
        var aside = await Reg(system, $"copy {CommandArgs.QuoteCmd(key)} {CommandArgs.QuoteCmd(tempKey)} /s /f", ct);
        if (aside.ExitCode != 0)
        {
            return Fail(system, $"The temporary profile key could not be copied: {aside.Output.Trim()}", aside.ExitCode);
        }

        system.DeleteRegistryKey(key);
        var promote = await Reg(system, $"copy {CommandArgs.QuoteCmd(bakKey)} {CommandArgs.QuoteCmd(key)} /s /f", ct);
        if (promote.ExitCode != 0)
        {
            await Reg(system, $"copy {CommandArgs.QuoteCmd(tempKey)} {CommandArgs.QuoteCmd(key)} /s /f", ct);
            return Fail(system, $"The real profile key could not be activated: {promote.Output.Trim()}", promote.ExitCode);
        }

        system.DeleteRegistryKey(bakKey);
        return null;
    }

    private static async Task<string?> FindRealFolderAsync(ISystem system, ProfileProblem.CurrentUser user, CancellationToken ct)
    {
        var folder = $@"{system.WindowsDirectory[..2]}\Users\{user.Name}";
        if (!CommandArgs.CanQuoteCmd(folder))
        {
            system.Log($"The profile folder {folder} cannot be checked safely.");
            return null;
        }

        var check = await system.RunCommandAsync("cmd.exe", $"/c if exist {CommandArgs.QuoteCmd($@"{folder}\NTUSER.DAT")} (exit /b 0) else (exit /b 1)", ct);
        return check.ExitCode == 0 ? folder : null;
    }

    // Read into reg command lines, so only ASCII digits are accepted.
    [GeneratedRegex(@"^S-1-[0-9]+(?:-[0-9]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex Sid();
}
