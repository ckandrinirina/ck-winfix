namespace WinFix.Tests;

public class RepoFilesTests
{
    private const string Releases = "https://github.com/ckandrinirina/ck-winfix/releases/latest/download/";
    private const string PagesSite = "https://ckandrinirina.github.io/ck-winfix/";

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WinFix.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("WinFix.sln not found above the test assembly");
    }

    private static string Read(string name) => File.ReadAllText(Path.Combine(RepoRoot(), name));

    private static string Section(string readme, string heading)
    {
        var start = readme.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"README has no '{heading}' heading");
        var next = readme.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return next < 0 ? readme[start..] : readme[start..next];
    }

    [Fact]
    public void License_is_MIT_with_the_author_copyright()
    {
        var license = Read("LICENSE");

        Assert.StartsWith("MIT License", license.TrimStart());
        Assert.Contains("Copyright (c) 2026 ANDRINIRINA Erick", license);
        Assert.Contains("Permission is hereby granted, free of charge", license);
        Assert.Contains("THE SOFTWARE IS PROVIDED \"AS IS\"", license);
    }

    [Theory]
    [InlineData("WinFix.exe")]
    [InlineData("WinFixUsb-macos-arm64")]
    [InlineData("WinFixUsb-macos-x64")]
    public void Download_section_links_every_release_asset(string asset)
    {
        Assert.Contains(Releases + asset, Section(Read("README.md"), "## Download"));
    }

    [Fact]
    public void Download_section_links_the_pages_site()
    {
        Assert.Contains(PagesSite, Section(Read("README.md"), "## Download"));
    }

    [Theory]
    [InlineData("brew install wimlib")]
    [InlineData("xattr -d com.apple.quarantine")]
    [InlineData("chmod +x")]
    [InlineData("./WinFixUsb-macos-arm64 --dry-run")]
    [InlineData("./WinFixUsb-macos-arm64\n")]
    public void Mac_section_gives_each_step(string snippet)
    {
        Assert.Contains(snippet, Section(Read("README.md"), "## Only have a Mac?"));
    }

    [Fact]
    public void Mac_section_explains_booting_the_pc_from_the_usb()
    {
        var section = Section(Read("README.md"), "## Only have a Mac?");

        Assert.Contains("boot", section, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USB", section);
    }

    [Fact]
    public void Readme_names_the_author_and_the_MIT_license()
    {
        var readme = Read("README.md");

        Assert.Contains("ANDRINIRINA Erick", readme);
        Assert.Contains("MIT", readme);
        Assert.Contains("LICENSE", readme);
    }

    [Fact]
    public void Readme_keeps_its_existing_sections()
    {
        var readme = Read("README.md");

        Assert.Contains("## What it fixes", readme);
        Assert.Contains("## Exit codes", readme);
    }
}
