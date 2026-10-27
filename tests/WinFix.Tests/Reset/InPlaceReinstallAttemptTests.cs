using WinFix.Media;
using WinFix.Reset;
using WinFix.System;

namespace WinFix.Tests.Reset;

public class InPlaceReinstallAttemptTests
{
    private const string SetupArguments = "/auto upgrade /eula accept /dynamicupdate disable";

    private static InstallMedia Media(string root = @"E:\") =>
        new(root, root + @"sources\install.wim", null, 26100);

    private static Func<ISystem, CancellationToken, Task<InstallMedia?>> Provides(InstallMedia? media, List<string>? asked = null, string name = "") =>
        (_, _) =>
        {
            asked?.Add(name);
            return Task.FromResult(media);
        };

    private static InPlaceReinstallAttempt Attempt(InstallMedia? found, InstallMedia? downloaded = null, List<string>? asked = null) =>
        new(Provides(found, asked, "find"), Provides(downloaded, asked, "download"));

    private static List<string> SetupCalls(FakeSystem fake) =>
        fake.Calls.Where(c => c.Contains("setup.exe", StringComparison.Ordinal)).ToList();

    [Fact]
    public async Task Setup_runs_from_found_media_with_the_in_place_upgrade_arguments()
    {
        var fake = new FakeSystem();

        var result = await Attempt(Media(@"E:\")).RunAsync(fake, CancellationToken.None);

        Assert.True(result.Started);
        Assert.Equal([$@"RunCommand E:\setup.exe {SetupArguments}"], SetupCalls(fake));
    }

    [Fact]
    public async Task Found_media_is_used_without_a_download()
    {
        var asked = new List<string>();

        await Attempt(Media(), Media(@"F:\"), asked).RunAsync(new FakeSystem(), CancellationToken.None);

        Assert.Equal(["find"], asked);
    }

    [Fact]
    public async Task Setup_runs_from_downloaded_media_when_none_is_found()
    {
        var fake = new FakeSystem();
        var asked = new List<string>();

        var result = await Attempt(null, Media(@"G:\"), asked).RunAsync(fake, CancellationToken.None);

        Assert.True(result.Started);
        Assert.Equal(["find", "download"], asked);
        Assert.Equal([$@"RunCommand G:\setup.exe {SetupArguments}"], SetupCalls(fake));
    }

    [Fact]
    public async Task No_media_anywhere_logs_and_fails_without_running_setup()
    {
        var fake = new FakeSystem();

        var result = await Attempt(null, null).RunAsync(fake, CancellationToken.None);

        Assert.False(result.Started);
        Assert.Contains(fake.Logs, l => l.Contains("No Windows install media found"));
        Assert.Empty(SetupCalls(fake));
    }

    [Theory]
    [InlineData(unchecked((int)0xC1900101), "0xC1900101")]
    [InlineData(1, "0x00000001")]
    public async Task A_setup_failure_is_logged_in_hex_and_fails_the_attempt(int exitCode, string hex)
    {
        var fake = new FakeSystem { CommandExitCode = exitCode };

        var result = await Attempt(Media()).RunAsync(fake, CancellationToken.None);

        Assert.False(result.Started);
        Assert.Contains(fake.Logs, l => l.Contains(hex));
        Assert.Contains(hex, result.Message);
    }

    [Theory]
    [InlineData(@"E:\evil""&calc\")]
    [InlineData(@"E:\%COMSPEC%\")]
    [InlineData("E:\\bad\nname\\")]
    public async Task A_media_path_that_cannot_be_quoted_runs_nothing(string root)
    {
        var fake = new FakeSystem();

        var result = await Attempt(Media(root)).RunAsync(fake, CancellationToken.None);

        Assert.False(result.Started);
        Assert.Empty(fake.Calls);
        Assert.NotEmpty(fake.Logs);
    }

    [Fact]
    public async Task A_media_path_with_spaces_and_a_trailing_backslash_is_accepted()
    {
        var fake = new FakeSystem();

        var result = await Attempt(Media(@"E:\Win 11\")).RunAsync(fake, CancellationToken.None);

        Assert.True(result.Started);
        Assert.Equal([$@"RunCommand E:\Win 11\setup.exe {SetupArguments}"], SetupCalls(fake));
    }

    [Fact]
    public void The_default_chain_starts_with_the_in_place_reinstall_then_the_keep_files_reset()
    {
        var attempts = ResetChain.DefaultAttempts(Provides(null), Provides(null));

        Assert.Collection(
            attempts,
            first => Assert.IsType<InPlaceReinstallAttempt>(first),
            second => Assert.IsType<MdmKeepFilesReset>(second));
    }

    [Fact]
    public async Task A_failed_reinstall_lets_the_chain_move_on_to_the_next_attempt()
    {
        var fake = new FakeSystem { CommandExitCode = unchecked((int)0xC1900101) };
        var next = new RecordingAttempt();

        var result = await new ResetChain([Attempt(Media()), next]).RunAsync(fake, CancellationToken.None);

        Assert.Equal(1, next.Runs);
        Assert.True(result.Started);
    }

    private sealed class RecordingAttempt : IResetAttempt
    {
        public string Name => "Next";

        public int Runs { get; private set; }

        public Task<ResetAttemptResult> RunAsync(ISystem system, CancellationToken ct)
        {
            Runs++;
            return Task.FromResult(new ResetAttemptResult(true, "started"));
        }
    }
}
