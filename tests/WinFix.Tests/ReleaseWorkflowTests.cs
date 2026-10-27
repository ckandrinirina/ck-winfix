using System.Text.RegularExpressions;

namespace WinFix.Tests;

public class ReleaseWorkflowTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WinFix.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("WinFix.sln not found above the test assembly");
    }

    private static string Workflow() =>
        File.ReadAllText(Path.Combine(RepoRoot(), ".github", "workflows", "release.yml"));

    private static string Job(string workflow, string name)
    {
        var match = Regex.Match(workflow, $@"^  {name}:\s*$", RegexOptions.Multiline);
        Assert.True(match.Success, $"release.yml has no '{name}' job");
        var next = Regex.Match(workflow[(match.Index + match.Length)..], @"^  [\w-]+:\s*$", RegexOptions.Multiline);
        return next.Success ? workflow.Substring(match.Index, match.Length + next.Index) : workflow[match.Index..];
    }

    [Fact]
    public void Runs_only_on_pushed_version_tags()
    {
        var workflow = Workflow();

        Assert.Matches(@"(?m)^on:\s*\n\s+push:\s*\n\s+tags:", workflow);
        Assert.Matches(@"(?m)^\s+- ?'?""?v\*'?""?\s*$|tags:\s*\[\s*['""]?v\*['""]?\s*\]", workflow);
    }

    [Fact]
    public void Test_job_runs_the_whole_solution()
    {
        Assert.Contains("dotnet test WinFix.sln", Job(Workflow(), "test"));
    }

    [Fact]
    public void Test_job_runs_on_the_platform_where_the_suite_passes()
    {
        Assert.Matches(@"runs-on:\s*macos-latest", Job(Workflow(), "test"));
    }

    [Fact]
    public void Build_job_waits_for_the_tests()
    {
        Assert.Matches(@"needs:\s*\[?\s*test\s*\]?\s*\n", Job(Workflow(), "build"));
    }

    [Fact]
    public void Release_job_waits_for_the_tests_and_every_build()
    {
        var release = Job(Workflow(), "release");

        Assert.Matches(@"needs:\s*\[\s*test\s*,\s*build\s*\]|needs:\s*\n\s*- test\s*\n\s*- build", release);
    }

    [Fact]
    public void Publishes_single_file_self_contained_with_the_tag_version()
    {
        var build = Job(Workflow(), "build");

        Assert.Contains("dotnet publish", build);
        Assert.Contains("--self-contained", build);
        Assert.Contains("-p:PublishSingleFile=true", build);
        Assert.Contains("-p:IncludeNativeLibrariesForSelfExtract=true", build);
        Assert.Contains("-p:Version=${GITHUB_REF_NAME#v}", build);
        Assert.DoesNotContain("PublishTrimmed=true", build);
    }

    [Theory]
    [InlineData("win-x64", "src/WinFix", "WinFix.exe")]
    [InlineData("osx-arm64", "src/WinFixUsb", "WinFixUsb-macos-arm64")]
    [InlineData("osx-x64", "src/WinFixUsb", "WinFixUsb-macos-x64")]
    public void Build_matrix_has_an_entry_per_published_file(string rid, string project, string asset)
    {
        var build = Job(Workflow(), "build");

        Assert.Matches($@"rid:\s*{rid}\b", build);
        Assert.Contains($"project: {project}", build);
        Assert.Matches($@"asset:\s*{Regex.Escape(asset)}\s*\n", build);
    }

    [Fact]
    public void Mac_programs_are_built_on_macos_latest()
    {
        var build = Job(Workflow(), "build");

        Assert.Matches(@"os:\s*macos-latest\s*\n\s*rid:\s*osx-arm64", build);
        Assert.Matches(@"os:\s*macos-latest\s*\n\s*rid:\s*osx-x64", build);
        Assert.Contains("runs-on: ${{ matrix.os }}", build);
    }

    [Fact]
    public void Release_attaches_the_three_files_by_exact_name_with_write_permission()
    {
        var release = Job(Workflow(), "release");

        Assert.Contains("softprops/action-gh-release@v3", release);
        Assert.Matches(@"contents:\s*write", release);
        foreach (var asset in new[] { "WinFix.exe", "WinFixUsb-macos-arm64", "WinFixUsb-macos-x64" })
        {
            Assert.Matches($@"(?m)^\s+(dist/)?{Regex.Escape(asset)}\s*$", release);
        }
    }

    [Fact]
    public void Write_permission_is_granted_to_the_release_job_only()
    {
        var workflow = Workflow();

        Assert.Single(Regex.Matches(workflow, @"contents:\s*write"));
        Assert.DoesNotContain("contents: write", Job(workflow, "test"));
        Assert.DoesNotContain("contents: write", Job(workflow, "build"));
    }

    [Fact]
    public void Uses_the_action_majors_recorded_in_the_stack()
    {
        var workflow = Workflow();
        var allowed = new Dictionary<string, string>
        {
            ["actions/checkout"] = "v7",
            ["actions/setup-dotnet"] = "v6",
            ["actions/upload-artifact"] = "v7",
            ["actions/download-artifact"] = "v8",
            ["softprops/action-gh-release"] = "v3",
        };

        var uses = Regex.Matches(workflow, @"uses:\s*([\w./-]+)@(\S+)");
        Assert.NotEmpty(uses);
        foreach (Match use in uses)
        {
            Assert.True(allowed.TryGetValue(use.Groups[1].Value, out var major), $"{use.Groups[1].Value} is not in ## Stack");
            Assert.Equal(major, use.Groups[2].Value);
        }

        Assert.Contains("actions/checkout@v7", workflow);
        Assert.Contains("actions/setup-dotnet@v6", workflow);
        Assert.Contains("actions/upload-artifact@v7", workflow);
        Assert.Contains("actions/download-artifact@v8", workflow);
    }
}
