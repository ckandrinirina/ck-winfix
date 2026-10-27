using WinFix.Pipeline;
using WinFix.System;

namespace WinFix.Tests.Pipeline;

public class ResetConfirmationTests
{
    private const string LogPath = @"%ProgramData%\WinFix\winfix.log";

    private static async Task<(int Exit, string Output, FakeSystem Fake)> RunAsync(string? answer, bool dryRun = false)
    {
        var fake = new FakeSystem { ShellHealthy = false };
        var output = new StringWriter();
        var pipeline = new FixPipeline(fake, output, readLine: () => answer, dryRun: dryRun);
        var exit = await pipeline.RunAsync(CancellationToken.None);
        return (exit, output.ToString(), fake);
    }

    [Fact]
    public async Task Warning_says_what_reset_keeps_and_removes_and_asks_for_YES()
    {
        var (_, output, _) = await RunAsync("no");

        Assert.Contains("keeps your personal files", output);
        Assert.Contains("removes your apps and settings", output);
        Assert.Contains("type YES", output);
    }

    [Fact]
    public async Task Warning_includes_the_log_file_path()
    {
        var (_, output, _) = await RunAsync("no");

        Assert.Contains(LogPath, output);
    }

    [Fact]
    public async Task Exactly_YES_goes_on_to_the_reset()
    {
        var (exit, output, fake) = await RunAsync("YES");

        Assert.Equal(0, exit);
        Assert.Contains("[4/4] Reset", output);
        Assert.Contains(fake.Calls, c => c == "Reboot");
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("Yes")]
    [InlineData("y")]
    [InlineData("YES ")]
    [InlineData("no")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Any_other_answer_exits_3_and_no_reset_call_reaches_the_system(string? answer)
    {
        var (exit, output, fake) = await RunAsync(answer);

        Assert.Equal(3, exit);
        Assert.DoesNotContain("[4/4] Reset", output);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("Reboot"));
    }

    [Fact]
    public async Task Calls_after_the_prompt_never_include_a_reset_when_declined()
    {
        var (_, _, fake) = await RunAsync("nope");

        var lastRepairCall = fake.Calls.ToList().FindLastIndex(c => c.StartsWith("RunCommand"));
        Assert.DoesNotContain(fake.Calls.Skip(lastRepairCall + 1), c => c.StartsWith("RunCommand") || c == "Reboot");
    }

    [Fact]
    public async Task Dry_run_prints_the_warning_and_simulates_YES_without_reading_input()
    {
        var fake = new FakeSystem { ShellHealthy = false };
        var output = new StringWriter();
        var pipeline = new FixPipeline(fake, output, readLine: () => throw new InvalidOperationException("input read"), dryRun: true);

        var exit = await pipeline.RunAsync(CancellationToken.None);

        var text = output.ToString();
        Assert.Equal(0, exit);
        Assert.Contains(LogPath, text);
        Assert.Contains("Dry run: answering YES", text);
        Assert.Contains("[4/4] Reset", text);
    }

    [Fact]
    public async Task Program_dry_run_still_prints_all_four_stages()
    {
        var output = new StringWriter();

        var exit = await Program.RunAsync(["--dry-run"], output);

        Assert.Equal(0, exit);
        Assert.Contains("Dry run: answering YES", output.ToString());
    }
}
