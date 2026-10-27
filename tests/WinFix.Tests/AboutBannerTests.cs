using System.Diagnostics;
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
    public void Version_of_a_framework_assembly_is_never_empty()
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

    [Fact]
    public void A_build_with_Version_1_2_3_prints_WinFix_1_2_3_first()
    {
        var work = Path.Combine(Path.GetTempPath(), "winfix-version-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopySource(Path.Combine(RepoRoot(), "src", "WinFix"), work);

            // SourceRevisionId makes the SDK append "+abc123", as a real build does with the commit.
            Run(work, "build", "WinFix.csproj", "-c", "Debug", "-o", "out", "-p:Version=1.2.3", "-p:SourceRevisionId=abc123");
            var printed = Run(work, "out/WinFix.dll", "--dry-run", "--only", "shell");

            Assert.Equal($"WinFix 1.2.3 — {Author}", printed.Split('\n')[0].TrimEnd('\r'));
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WinFix.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("WinFix.sln not found above the test binaries");
    }

    private static void CopySource(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(from, file);
            if (relative.StartsWith("bin" + Path.DirectorySeparatorChar) || relative.StartsWith("obj" + Path.DirectorySeparatorChar))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(to, relative))!);
            File.Copy(file, Path.Combine(to, relative));
        }
    }

    private static string Run(string workingDirectory, params string[] dotnetArgs)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (var arg in dotnetArgs)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(180_000), "dotnet did not finish in time");
        Assert.True(process.ExitCode == 0, $"dotnet {string.Join(' ', dotnetArgs)} exited {process.ExitCode}:\n{stdout}\n{stderr.Result}");
        return stdout;
    }

    private static string FirstLine(StringWriter output) => output.ToString().Split('\n')[0].TrimEnd('\r');
}
