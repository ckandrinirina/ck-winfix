using System.Diagnostics;
using System.Xml.Linq;
using WinFix.System;

namespace WinFix.Tests.System;

public class AdminCheckTests
{
    private static (bool Proceed, int Exit, string Output) Check(bool isAdmin)
    {
        var output = new StringWriter();
        var proceed = new AdminCheck(() => isAdmin).TryProceed(output, out var exit);
        return (proceed, exit, output.ToString());
    }

    [Fact]
    public void Non_admin_exits_with_code_2()
    {
        var (proceed, exit, _) = Check(isAdmin: false);

        Assert.False(proceed);
        Assert.Equal(2, exit);
    }

    [Fact]
    public void Non_admin_message_explains_the_task_manager_relaunch()
    {
        var (_, _, output) = Check(isAdmin: false);

        Assert.Contains("Ctrl+Shift+Esc", output);
        Assert.Contains("Run new task", output);
        Assert.Contains("Create this task with administrative privileges", output);
    }

    [Fact]
    public void Admin_proceeds_silently()
    {
        var (proceed, exit, output) = Check(isAdmin: true);

        Assert.True(proceed);
        Assert.Equal(0, exit);
        Assert.Equal("", output);
    }

    [Fact]
    public async Task Program_without_admin_prints_the_relaunch_help_and_exits_2()
    {
        var output = new StringWriter();

        var exit = await Program.RunAsync([], output, isAdmin: () => false);

        Assert.Equal(2, exit);
        Assert.Contains("Run new task", output.ToString());
        Assert.DoesNotContain("Diagnose", output.ToString());
    }

    [Fact]
    public async Task Dry_run_skips_the_admin_check()
    {
        var output = new StringWriter();

        var exit = await Program.RunAsync(["--dry-run"], output, isAdmin: () => false);

        Assert.Equal(0, exit);
        Assert.Contains("Diagnose", output.ToString());
        Assert.DoesNotContain("Run new task", output.ToString());
    }

    [Fact]
    public void Manifest_requires_administrator_and_the_project_embeds_it()
    {
        var src = Path.Combine(RepoRoot(), "src", "WinFix");

        var manifest = XDocument.Load(Path.Combine(src, "app.manifest"));
        var level = manifest.Descendants().Single(e => e.Name.LocalName == "requestedExecutionLevel");
        Assert.Equal("requireAdministrator", (string?)level.Attribute("level"));

        var csproj = XDocument.Load(Path.Combine(src, "WinFix.csproj"));
        Assert.Equal("app.manifest", csproj.Descendants("ApplicationManifest").Single().Value);
    }

    [Fact]
    public async Task Publish_win_x64_single_file_produces_exactly_one_exe()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "winfix-publish-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = Path.Combine(RepoRoot(), "src", "WinFix", "WinFix.csproj");
            var psi = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in new[]
            {
                "publish", project, "-c", "Release", "-r", "win-x64", "--self-contained",
                "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
                "-p:EnableWindowsTargeting=true", "-o", outDir,
            })
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi)!;
            var ct = TestContext.Current.CancellationToken;
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            Assert.True(process.ExitCode == 0, await stdout + await stderr);

            var files = Directory.GetFileSystemEntries(outDir).Select(Path.GetFileName).ToArray();
            Assert.Equal(["WinFix.exe"], files);
        }
        finally
        {
            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
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
}
