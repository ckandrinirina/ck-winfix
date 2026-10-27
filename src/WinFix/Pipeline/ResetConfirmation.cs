namespace WinFix.Pipeline;

/// <summary>Warns what a reset does and proceeds only on the exact answer YES; null or EOF is a refusal.</summary>
public sealed class ResetConfirmation(TextWriter output, Func<string?> readLine, bool dryRun = false)
{
    /// <summary>The real system expands %ProgramData%.</summary>
    public const string LogPath = @"%ProgramData%\WinFix\winfix.log";

    public const string Answer = "YES";

    public bool Confirm()
    {
        output.WriteLine("Every repair failed. The last option is to reset Windows.");
        output.WriteLine("Reset keeps your personal files.");
        output.WriteLine("Reset removes your apps and settings.");
        output.WriteLine($"Details of what WinFix tried are in {LogPath}");
        if (dryRun)
        {
            output.WriteLine($"Dry run: answering {Answer} for you.");
            return true;
        }

        output.WriteLine($"To go on, type {Answer} and press Enter. Anything else cancels.");
        return readLine() == Answer;
    }
}
