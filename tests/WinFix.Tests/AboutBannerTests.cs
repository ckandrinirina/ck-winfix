using WinFix.System;

namespace WinFix.Tests;

public class AboutBannerTests
{
    private const string Author = "by ANDRINIRINA Erick · github.com/ckandrinirina";

    [Fact]
    public void Line_has_product_version_author_and_link()
    {
        Assert.Equal($"WinFix 1.2.3 — {Author}", AboutBanner.Line("WinFix", "1.2.3"));
    }

    [Theory]
    [InlineData("1.2.3+abc123def", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta.1+abc", "1.2.3-beta.1")]
    public void Version_drops_the_commit_suffix(string informational, string expected)
    {
        Assert.Equal(expected, AboutBanner.StripCommit(informational));
    }

    [Fact]
    public void Version_of_an_assembly_never_carries_a_commit_suffix()
    {
        var version = AboutBanner.VersionOf(typeof(Program).Assembly);

        Assert.NotEmpty(version);
        Assert.DoesNotContain('+', version);
    }

    [Fact]
    public void Version_of_an_assembly_without_informational_version_falls_back_to_unknown_or_assembly_version()
    {
        Assert.NotEmpty(AboutBanner.VersionOf(typeof(string).Assembly));
    }

    [Fact]
    public void Banner_is_reusable_with_another_product_name()
    {
        var output = new StringWriter();

        AboutBanner.Write(output, "WinFixUsb", typeof(Program).Assembly);

        var expected = $"WinFixUsb {AboutBanner.VersionOf(typeof(Program).Assembly)} — {Author}";
        Assert.Equal(expected + Environment.NewLine, output.ToString());
    }

    [Theory]
    [InlineData("--dry-run")]
    [InlineData("--dry-run --only shell")]
    [InlineData("--only shell")]
    public async Task First_line_of_a_run_is_the_banner_in_every_mode(string argLine)
    {
        var output = new StringWriter();
        var expected = AboutBanner.Line("WinFix", AboutBanner.VersionOf(typeof(Program).Assembly));

        await Program.RunAsync(argLine.Split(' '), output, isAdmin: () => true, system: new FakeSystem(), readLine: () => "no");

        Assert.Equal(expected, FirstLine(output));
    }

    [Fact]
    public async Task First_line_is_the_banner_on_the_not_admin_refusal()
    {
        var output = new StringWriter();

        var exit = await Program.RunAsync(["--only", "shell"], output, isAdmin: () => false, system: new FakeSystem());

        Assert.Equal(2, exit);
        Assert.Equal(AboutBanner.Line("WinFix", AboutBanner.VersionOf(typeof(Program).Assembly)), FirstLine(output));
        Assert.True(output.ToString().Split('\n').Length > 2);
    }

    [Fact]
    public async Task First_line_is_the_banner_when_the_arguments_are_refused()
    {
        var output = new StringWriter();

        await Program.RunAsync(["--dry-run", "--only"], output);

        Assert.StartsWith("WinFix ", FirstLine(output));
        Assert.Contains(Author, FirstLine(output));
    }

    private static string FirstLine(StringWriter output) => output.ToString().Split('\n')[0].TrimEnd('\r');
}
