using WinFix.System;

namespace WinFix.Tests.System;

public class CommandArgsTests
{
    [Theory]
    [InlineData("'")]
    [InlineData("\u2018")]
    [InlineData("\u2019")]
    [InlineData("\u201A")]
    [InlineData("\u201B")]
    public void Every_single_quote_variant_stays_inside_the_powershell_literal(string quote)
    {
        var value = $@"D:\x{quote};Start-Process calc;{quote}{quote}.iso";

        var parsed = PsLiteral.After("Test-Path -LiteralPath " + CommandArgs.QuotePowerShell(value), "-LiteralPath ");

        Assert.Equal(value, parsed.Value);
        Assert.Equal(string.Empty, parsed.Tail);
    }

    [Fact]
    public void A_normal_path_is_quoted_as_it_is()
    {
        Assert.Equal(@"'C:\Users\me\Downloads\Win 11.iso'", CommandArgs.QuotePowerShell(@"C:\Users\me\Downloads\Win 11.iso"));
    }

    [Theory]
    [InlineData("a\"b")]
    [InlineData("a\nb")]
    [InlineData("a\0b")]
    public void A_double_quote_or_control_character_cannot_be_quoted_for_powershell(string value)
    {
        Assert.False(CommandArgs.CanQuotePowerShell(value));
        Assert.Throws<ArgumentException>(() => CommandArgs.QuotePowerShell(value));
    }

    [Theory]
    [InlineData("a\"b")]
    [InlineData("%COMSPEC%")]
    [InlineData("a\rb")]
    [InlineData(@"C:\folder\")]
    public void Values_that_would_escape_double_quotes_on_a_command_line_are_refused(string value)
    {
        Assert.False(CommandArgs.CanQuoteCmd(value));
    }

    [Theory]
    [InlineData(@"C:\Users\Tom & Jerry")]
    [InlineData(@"E:\sources\install.wim")]
    public void Normal_paths_can_be_double_quoted(string value)
    {
        Assert.True(CommandArgs.CanQuoteCmd(value));
    }
}
