using System.Text.RegularExpressions;
using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Problems;

/// <summary>A temporary user profile: Windows logged the user into C:\Users\TEMP, or left both the profile key and its .bak in the ProfileList.</summary>
public sealed partial class ProfileProblem : IProblemModule
{
    internal const string ProfileListKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";

    internal const string ImagePathValue = "ProfileImagePath";

    public string Name => "profile";

    public IReadOnlyList<IRepairStep> Repairs { get; } = [new RestoreProfileStep()];

    public async Task<Health> DetectAsync(ISystem system, CancellationToken ct)
    {
        var user = await CurrentUser.ReadAsync(system, ct);
        if (user is null)
        {
            return new Health(true, "The current user could not be identified, so the profile was not checked.");
        }

        var keys = system.GetRegistrySubKeyNames(ProfileListKey);
        if (keys.Contains(user.Sid, StringComparer.OrdinalIgnoreCase) && keys.Contains(user.Sid + ".bak", StringComparer.OrdinalIgnoreCase))
        {
            return new Health(false, $"The profile list holds both {user.Sid} and {user.Sid}.bak.");
        }

        var path = system.ReadRegistryValue($@"{ProfileListKey}\{user.Sid}", ImagePathValue);
        return IsTempPath(path)
            ? new Health(false, $"The user profile is a TEMP profile ({path}).")
            : new Health(true, "The user profile looks healthy.");
    }

    internal static bool IsTempPath(string? path) => path is not null && TempFolderPattern().IsMatch(path);

    /// <summary>The logged-on user; an elevated token from Task Manager is still that user's.</summary>
    internal sealed partial record CurrentUser(string Sid, string Name)
    {
        public static async Task<CurrentUser?> ReadAsync(ISystem system, CancellationToken ct)
        {
            var result = await system.RunCommandAsync("whoami", "/user /fo csv /nh", ct);
            var match = result.ExitCode == 0 ? WhoAmIPattern().Match(result.Output) : Match.Empty;
            return match.Success
                ? new CurrentUser(match.Groups["sid"].Value, match.Groups["name"].Value)
                : null;
        }

        [GeneratedRegex(@"""(?:[^""\\]*\\)?(?<name>[^""\\]+)"",""(?<sid>S-\d+(?:-\d+)+)""")]
        private static partial Regex WhoAmIPattern();
    }

    [GeneratedRegex(@"^[A-Za-z]:\\Users\\TEMP(?:[.\\]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex TempFolderPattern();
}
