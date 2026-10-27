using System.Text.RegularExpressions;

namespace WinFix.Tests;

public class SitePageTests
{
    private const string Releases = "https://github.com/ckandrinirina/ck-winfix/releases/latest/download/";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WinFix.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("WinFix.sln not found above the test assembly");
    }

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([RepoRoot(), .. path]));

    private static string Page() => Read("site", "index.html");

    private static string Pages() => Read(".github", "workflows", "pages.yml");

    private static List<string> ReleaseAssets() =>
        Regex.Matches(Read(".github", "workflows", "release.yml"), @"(?m)^\s+asset:\s*(\S+)\s*$")
            .Select(m => m.Groups[1].Value)
            .ToList();

    private static List<string> DownloadLinks() =>
        Regex.Matches(Page(), @"href=""(https://github\.com/[^""]*/releases/[^""]*)""")
            .Select(m => m.Groups[1].Value)
            .ToList();

    [Fact]
    public void Release_workflow_uploads_the_three_known_assets()
    {
        Assert.Equal(["WinFix.exe", "WinFixUsb-macos-arm64", "WinFixUsb-macos-x64"], ReleaseAssets());
    }

    [Fact]
    public void Every_release_asset_has_a_download_link_with_its_exact_name()
    {
        var links = DownloadLinks();

        foreach (var asset in ReleaseAssets())
        {
            Assert.Contains(Releases + asset, links);
        }
    }

    [Fact]
    public void There_is_one_download_button_per_asset_and_no_other_release_link()
    {
        var expected = ReleaseAssets().Select(a => Releases + a).Order().ToList();

        Assert.Equal(expected, DownloadLinks().Order().ToList());
    }

    [Fact]
    public void Download_links_are_buttons()
    {
        foreach (var asset in ReleaseAssets())
        {
            Assert.Matches($@"<a[^>]*class=""[^""]*\bbtn\b[^""]*""[^>]*href=""{Regex.Escape(Releases + asset)}""|<a[^>]*href=""{Regex.Escape(Releases + asset)}""[^>]*class=""[^""]*\bbtn\b", Page());
        }
    }

    [Fact]
    public void Page_says_what_WinFix_fixes()
    {
        var page = Page();

        foreach (var topic in new[] { "taskbar", "blue screen", "Windows Update", "Start menu", "temporary profile", "network" })
        {
            Assert.Contains(topic, page, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Page_explains_starting_as_administrator_from_Task_Manager()
    {
        var page = Page();

        Assert.Contains("Ctrl+Shift+Esc", page);
        Assert.Contains("Run new task", page);
        Assert.Contains("Create this task with administrative privileges", page);
    }

    [Fact]
    public void Page_gives_the_Mac_USB_steps_with_the_real_flags()
    {
        var page = Page();

        Assert.Contains("brew install wimlib", page);
        Assert.Contains("--dry-run", page);
        Assert.Contains("--iso", page);
        Assert.Contains("--disk", page);
        Assert.Contains("com.apple.quarantine", page);
    }

    [Fact]
    public void Page_shows_the_author_line_and_the_MIT_license()
    {
        var page = Page();

        Assert.Contains("ANDRINIRINA Erick", page);
        Assert.Contains("github.com/ckandrinirina", page);
        Assert.Contains("MIT", page);
    }

    [Fact]
    public void Page_is_one_static_file_without_external_resources()
    {
        var page = Page();

        Assert.Contains("<style>", page);
        Assert.DoesNotContain("<script", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", page);
        Assert.DoesNotMatch(@"(?:src|url\()\s*=?\s*[""']?https?:", page);
        Assert.Single(Directory.GetFiles(Path.Combine(RepoRoot(), "site")));
    }

    [Fact]
    public void Page_is_readable_at_phone_width_in_light_and_dark()
    {
        var page = Page();

        Assert.Matches(@"<meta\s+name=""viewport""\s+content=""[^""]*width=device-width", page);
        Assert.Contains("prefers-color-scheme: dark", page);
        Assert.Matches(@"<html[^>]*\blang=""en""", page);
    }

    [Fact]
    public void Pages_workflow_deploys_the_site_folder_on_push_to_main()
    {
        var workflow = Pages();

        Assert.Matches(@"(?m)^on:\s*\n\s+push:\s*\n\s+branches:\s*(\[\s*main\s*\]|\n\s+- main)", workflow);
        Assert.Matches(@"path:\s*'?site/?'?\s*\n", workflow);
    }

    [Fact]
    public void Pages_workflow_grants_the_deploy_permissions_to_the_deploy_job_only()
    {
        var workflow = Pages();

        Assert.Matches(@"(?m)^permissions:\s*\n\s+contents:\s*read\s*\n", workflow);
        Assert.Single(Regex.Matches(workflow, @"pages:\s*write"));
        Assert.Single(Regex.Matches(workflow, @"id-token:\s*write"));
        var deploy = workflow[Regex.Match(workflow, @"(?m)^  deploy:\s*$").Index..];
        Assert.Contains("pages: write", deploy);
        Assert.Contains("id-token: write", deploy);
        Assert.Matches(@"environment:\s*\n\s+name:\s*github-pages", deploy);
    }

    [Fact]
    public void Pages_workflow_uses_the_action_majors_recorded_in_the_stack()
    {
        var workflow = Pages();
        var allowed = new Dictionary<string, string>
        {
            ["actions/checkout"] = "v7",
            ["actions/configure-pages"] = "v6",
            ["actions/upload-pages-artifact"] = "v5",
            ["actions/deploy-pages"] = "v5",
        };

        var uses = Regex.Matches(workflow, @"uses:\s*([\w./-]+)@(\S+)");
        Assert.NotEmpty(uses);
        foreach (Match use in uses)
        {
            Assert.True(allowed.TryGetValue(use.Groups[1].Value, out var major), $"{use.Groups[1].Value} is not in ## Stack");
            Assert.Equal(major, use.Groups[2].Value);
        }

        Assert.Contains("actions/upload-pages-artifact@v5", workflow);
        Assert.Contains("actions/deploy-pages@v5", workflow);
    }
}
