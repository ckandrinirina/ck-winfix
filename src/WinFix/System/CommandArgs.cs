using System.Text;

namespace WinFix.System;

/// <summary>Puts values read from files, the registry or command output into command lines without letting them become code.</summary>
public static class CommandArgs
{
    // PowerShell ends a single-quoted string at any of these, not only at U+0027.
    private const string SingleQuotes = "'‘’‚‛";

    /// <summary>Whether the value can go into a PowerShell literal inside -Command "...": no double quote, no control character.</summary>
    public static bool CanQuotePowerShell(string value) => !value.Any(c => c == '"' || char.IsControl(c));

    /// <summary>A single-quoted PowerShell literal with every quote character doubled, as PowerShell's own escaper does.</summary>
    public static string QuotePowerShell(string value)
    {
        if (!CanQuotePowerShell(value))
        {
            throw new ArgumentException("The value holds a double quote or a control character.", nameof(value));
        }

        var literal = new StringBuilder(value.Length + 2).Append('\'');
        foreach (var c in value)
        {
            literal.Append(c);
            if (SingleQuotes.Contains(c))
            {
                literal.Append(c);
            }
        }

        return literal.Append('\'').ToString();
    }

    /// <summary>
    /// Whether the value can sit between double quotes on a cmd.exe or native command line: no double quote, no %
    /// (cmd expands it even inside quotes), no control character, and no trailing backslash (it would escape the closing quote).
    /// </summary>
    public static bool CanQuoteCmd(string value) =>
        !value.EndsWith('\\') && !value.Any(c => c is '"' or '%' || char.IsControl(c));
}
