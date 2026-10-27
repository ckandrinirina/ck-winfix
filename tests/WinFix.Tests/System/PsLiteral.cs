using System.Text;

namespace WinFix.Tests.System;

/// <summary>Reads a single-quoted literal the way PowerShell's tokenizer does: any of the five single-quote characters ends it unless doubled.</summary>
internal static class PsLiteral
{
    private const string Quotes = "'‘’‚‛";

    /// <summary>The value of the literal right after <paramref name="marker"/>, and the command text that follows it.</summary>
    public static (string Value, string Tail) After(string text, string marker)
    {
        var i = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(i >= 0, $"'{marker}' not found in: {text}");
        i += marker.Length;
        Assert.True(i < text.Length && Quotes.Contains(text[i]), $"No literal after '{marker}' in: {text}");

        var value = new StringBuilder();
        for (i++; i < text.Length; i++)
        {
            if (!Quotes.Contains(text[i]))
            {
                value.Append(text[i]);
            }
            else if (i + 1 < text.Length && Quotes.Contains(text[i + 1]))
            {
                value.Append(text[++i]);
            }
            else
            {
                return (value.ToString(), text[(i + 1)..]);
            }
        }

        Assert.Fail($"Unterminated literal in: {text}");
        return default;
    }
}
