using System.Text.RegularExpressions;
using WinFix.Media;
using WinFix.System;
using WinFix.Tests.System;

namespace WinFix.Tests.Media;

public partial class MediaFinderTests
{
    private const string Cpu = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    private const string Nt = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    private static string Dism(string arch, string edition, string version, string lang = "en-US", int index = 1) =>
        $"""
        Deployment Image Servicing and Management tool
        Version: 10.0.26100.1

        Details for image : E:\sources\install.wim

        Index : {index}
        Name : Windows
        Description : Windows
        Size : 16,000,000,000 bytes
        Architecture : {arch}
        Hal : <undefined>
        Version : {version}
        ServicePack Build : 1
        ServicePack Level : 0
        Edition : {edition}
        Installation : Client
        System Root : WINDOWS
        Languages :
        	{lang} (Default)

        The operation completed successfully.
        """;

    /// <summary>A scripted PC: drives, files, ISOs and DISM answers behind the command seam.</summary>
    private sealed class Machine
    {
        public Dictionary<string, int> Drives { get; } = new() { ["C:"] = 3 };

        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Isos { get; } = [];

        public Dictionary<string, string> IsoLetters { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Image path to its DISM answer per index (1-based).</summary>
        public Dictionary<string, List<string>> Images { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Dismounted { get; } = [];

        public string Language { get; set; } = "en-US";

        public FakeSystem Build()
        {
            var fake = new FakeSystem { CommandResponder = Respond };
            fake.RegistryValues[(Cpu, "PROCESSOR_ARCHITECTURE")] = "AMD64";
            fake.RegistryValues[(Nt, "EditionID")] = "Professional";
            fake.RegistryValues[(Nt, "CurrentBuild")] = "22631";
            return fake;
        }

        public void AddMedia(string root, string dism, string file = "install.wim")
        {
            Files.Add(root + "setup.exe");
            Files.Add(root + @"sources\" + file);
            Images[root + @"sources\" + file] = [dism];
        }

        private CommandResult Respond(string file, string args)
        {
            if (args.Contains("Win32_LogicalDisk", StringComparison.Ordinal))
            {
                return new(0, string.Join("\r\n", Drives.Select(d => $"{d.Key}|{d.Value}")));
            }

            if (args.Contains("Test-Path", StringComparison.Ordinal))
            {
                return new(0, Files.Contains(Quoted().Match(args).Groups[1].Value) ? "True" : "False");
            }

            if (args.Contains("*.iso", StringComparison.Ordinal))
            {
                return new(0, string.Join("\r\n", Isos));
            }

            if (args.Contains("Dismount-DiskImage", StringComparison.Ordinal))
            {
                Dismounted.Add(Quoted().Match(args).Groups[1].Value);
                return new(0, "");
            }

            if (args.Contains("Mount-DiskImage", StringComparison.Ordinal))
            {
                var letter = IsoLetters.GetValueOrDefault(Quoted().Match(args).Groups[1].Value);
                return letter is null ? new(1, "Mount failed") : new(0, letter);
            }

            if (args.Contains("InstalledUICulture", StringComparison.Ordinal))
            {
                return new(0, Language);
            }

            if (file.Contains("dism", StringComparison.OrdinalIgnoreCase))
            {
                var path = WimPath().Match(args).Groups[1].Value;
                var index = int.Parse(IndexArg().Match(args).Groups[1].Value);
                return Images.TryGetValue(path, out var list) && index <= list.Count
                    ? new(0, list[index - 1])
                    : new(87, "Error: 87");
            }

            return new(1, "unexpected " + file + " " + args);
        }
    }

    private sealed class NoInput : TextReader
    {
        public override int Peek() => throw new InvalidOperationException("The search asked for input.");

        public override int Read() => throw new InvalidOperationException("The search asked for input.");

        public override string ReadLine() => throw new InvalidOperationException("The search asked for input.");
    }

    [GeneratedRegex("'([^']*)'")]
    private static partial Regex Quoted();

    [GeneratedRegex("/WimFile:\"([^\"]*)\"")]
    private static partial Regex WimPath();

    [GeneratedRegex(@"/Index:(\d+)")]
    private static partial Regex IndexArg();

    private static Task<InstallMedia?> Find(FakeSystem fake) => new MediaFinder().FindAsync(fake, CancellationToken.None);

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task Media_on_removable_fixed_and_optical_drive_roots_is_found(int driveType)
    {
        var machine = new Machine();
        machine.Drives["E:"] = driveType;
        machine.AddMedia(@"E:\", Dism("x64", "Professional", "10.0.22631"));

        var media = await Find(machine.Build());

        Assert.NotNull(media);
        Assert.Equal(@"E:\", media.Root);
        Assert.Equal(@"E:\sources\install.wim", media.ImagePath);
        Assert.Null(media.IsoPath);
    }

    [Fact]
    public async Task Install_esd_counts_as_media()
    {
        var machine = new Machine();
        machine.Drives["E:"] = 2;
        machine.AddMedia(@"E:\", Dism("x64", "Professional", "10.0.22631"), "install.esd");

        var media = await Find(machine.Build());

        Assert.Equal(@"E:\sources\install.esd", media?.ImagePath);
    }

    [Fact]
    public async Task A_drive_without_setup_or_image_or_a_network_drive_is_not_media()
    {
        var machine = new Machine();
        machine.Drives["E:"] = 2;
        machine.Drives["F:"] = 4;
        machine.Drives["G:"] = 3;
        machine.Files.Add(@"E:\setup.exe");
        machine.AddMedia(@"F:\", Dism("x64", "Professional", "10.0.22631"));
        machine.Files.Add(@"G:\sources\install.wim");

        Assert.Null(await Find(machine.Build()));
    }

    [Fact]
    public async Task Isos_in_drive_roots_downloads_and_desktop_are_mounted_and_found()
    {
        var machine = new Machine();
        machine.Isos.AddRange([@"D:\win.iso", @"C:\Users\me\Downloads\win11.iso"]);
        machine.IsoLetters[@"C:\Users\me\Downloads\win11.iso"] = "H";
        machine.AddMedia(@"H:\", Dism("x64", "Professional", "10.0.22631"));
        machine.IsoLetters[@"D:\win.iso"] = "I";
        var fake = machine.Build();

        var media = await Find(fake);

        Assert.Equal(@"C:\Users\me\Downloads\win11.iso", media?.IsoPath);
        Assert.Equal(@"H:\", media?.Root);
        Assert.Contains(fake.Calls, c => c.Contains("Mount-DiskImage", StringComparison.Ordinal) && c.Contains("win11.iso", StringComparison.Ordinal));
        Assert.Contains(fake.Calls, c => c.Contains("Downloads", StringComparison.Ordinal) && c.Contains("Desktop", StringComparison.Ordinal) && c.Contains("*.iso", StringComparison.Ordinal));
        Assert.Contains(fake.Calls, c => c.Contains("Mount-DiskImage", StringComparison.Ordinal) && c.Contains(@"D:\win.iso", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_iso_that_will_not_mount_is_logged_and_skipped()
    {
        var machine = new Machine();
        machine.Isos.Add(@"D:\broken.iso");
        var fake = machine.Build();

        Assert.Null(await Find(fake));
        Assert.Contains(fake.Logs, l => l.Contains("broken.iso", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("x86", "Professional", "en-US", "architecture")]
    [InlineData("x64", "Core", "en-US", "edition")]
    [InlineData("x64", "Professional", "fr-FR", "language")]
    public async Task A_mismatched_iso_is_logged_with_the_reason_and_dismounted(string arch, string edition, string lang, string reason)
    {
        var machine = new Machine();
        machine.Isos.Add(@"D:\win.iso");
        machine.IsoLetters[@"D:\win.iso"] = "H";
        machine.AddMedia(@"H:\", Dism(arch, edition, "10.0.22631", lang));
        var fake = machine.Build();

        var media = await Find(fake);

        Assert.Null(media);
        Assert.Contains(fake.Logs, l => l.Contains(@"win.iso", StringComparison.Ordinal) && l.Contains(reason, StringComparison.OrdinalIgnoreCase));
        Assert.Equal([@"D:\win.iso"], machine.Dismounted);
    }

    [Fact]
    public async Task A_mismatched_drive_is_logged_and_never_dismounted()
    {
        var machine = new Machine();
        machine.Drives["E:"] = 5;
        machine.AddMedia(@"E:\", Dism("arm64", "Professional", "10.0.22631"));
        var fake = machine.Build();

        Assert.Null(await Find(fake));
        Assert.Contains(fake.Logs, l => l.Contains(@"E:\", StringComparison.Ordinal) && l.Contains("architecture", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(machine.Dismounted);
    }

    [Fact]
    public async Task The_highest_build_wins_and_the_other_iso_is_dismounted()
    {
        var machine = new Machine();
        machine.Drives["E:"] = 5;
        machine.AddMedia(@"E:\", Dism("x64", "Professional", "10.0.22621"));
        machine.Isos.AddRange([@"D:\old.iso", @"D:\new.iso"]);
        machine.IsoLetters[@"D:\old.iso"] = "H";
        machine.IsoLetters[@"D:\new.iso"] = "I";
        machine.AddMedia(@"H:\", Dism("x64", "Professional", "10.0.19045"));
        machine.AddMedia(@"I:\", Dism("x64", "Professional", "10.0.26100"));

        var media = await Find(machine.Build());

        Assert.Equal(@"D:\new.iso", media?.IsoPath);
        Assert.Equal([@"D:\old.iso"], machine.Dismounted);
    }

    [Fact]
    public async Task A_multi_edition_image_matches_when_any_index_matches()
    {
        var machine = new Machine();
        machine.Drives["E:"] = 5;
        machine.AddMedia(@"E:\", Dism("x64", "Core", "10.0.22631"));
        machine.Images[@"E:\sources\install.wim"].Add(Dism("x64", "Professional", "10.0.22631", index: 2));

        Assert.NotNull(await Find(machine.Build()));
    }

    [Fact]
    public async Task The_search_never_asks_the_user_anything()
    {
        var machine = new Machine();
        machine.Drives["E:"] = 5;
        machine.Isos.Add(@"D:\win.iso");
        machine.IsoLetters[@"D:\win.iso"] = "H";
        machine.AddMedia(@"E:\", Dism("x64", "Professional", "10.0.22631"));
        machine.AddMedia(@"H:\", Dism("x86", "Professional", "10.0.22631"));
        var original = Console.In;
        Console.SetIn(new NoInput());
        try
        {
            var media = await Find(machine.Build());

            Assert.NotNull(media);
        }
        finally
        {
            Console.SetIn(original);
        }
    }

    [Fact]
    public void Dism_output_is_parsed_into_architecture_edition_version_and_languages()
    {
        var image = MediaMatcher.ParseWimInfo(Dism("x64", "Professional", "10.0.22631", "fr-FR"));

        Assert.NotNull(image);
        Assert.Equal("x64", image.Architecture);
        Assert.Equal("Professional", image.Edition);
        Assert.Equal(22631, image.Build);
        Assert.Equal(["fr-FR"], image.Languages);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Error: 87")]
    public void Unreadable_dism_output_gives_no_image(string output)
    {
        Assert.Null(MediaMatcher.ParseWimInfo(output));
    }

    [Fact]
    public async Task The_installed_system_is_read_from_the_registry_and_the_ui_language()
    {
        var machine = new Machine { Language = "fr-FR" };
        var fake = machine.Build();
        fake.RegistryValues[(Cpu, "PROCESSOR_ARCHITECTURE")] = "ARM64";

        var installed = await MediaMatcher.ReadInstalledAsync(fake, CancellationToken.None);

        Assert.Equal(new InstalledWindows("arm64", "Professional", "fr-FR"), installed);
    }

    public static TheoryData<string> QuotedIsoNames =>
    [
        "x';calc;'.iso",
        "x\u2018;calc;\u2018.iso",
        "x\u2019;calc;\u2019.iso",
        "x\u201A;calc;\u201A.iso",
        "x\u201B;calc;\u201B.iso",
    ];

    /// <summary>A PC whose Downloads holds <paramref name="iso"/>; the ISO mounts as H:\ but holds no setup, so it is dismounted.</summary>
    private static FakeSystem OneIso(string iso)
    {
        var fake = new FakeSystem
        {
            CommandResponder = (_, args) =>
                args.Contains("Win32_LogicalDisk", StringComparison.Ordinal) ? new(0, "C:|3")
                : args.Contains("*.iso", StringComparison.Ordinal) ? new(0, iso)
                : args.Contains("Dismount-DiskImage", StringComparison.Ordinal) ? new(0, "")
                : args.Contains("Mount-DiskImage", StringComparison.Ordinal) ? new(0, "H")
                : args.Contains("InstalledUICulture", StringComparison.Ordinal) ? new(0, "en-US")
                : new(0, "False"),
        };
        fake.RegistryValues[(Cpu, "PROCESSOR_ARCHITECTURE")] = "AMD64";
        fake.RegistryValues[(Nt, "EditionID")] = "Professional";
        return fake;
    }

    [Theory]
    [MemberData(nameof(QuotedIsoNames))]
    public async Task A_quote_in_an_iso_name_stays_inside_the_mount_and_dismount_literals(string name)
    {
        var iso = $@"C:\Users\me\Downloads\{name}";
        var fake = OneIso(iso);

        await Find(fake);

        var mount = Assert.Single(fake.Calls, c => c.Contains("(Mount-DiskImage", StringComparison.Ordinal));
        var mounted = PsLiteral.After(mount, "-ImagePath ");
        Assert.Equal(iso, mounted.Value);
        Assert.Equal(" -PassThru | Get-Volume).DriveLetter\"", mounted.Tail);

        var dismount = Assert.Single(fake.Calls, c => c.Contains("Dismount-DiskImage", StringComparison.Ordinal));
        var dismounted = PsLiteral.After(dismount, "-ImagePath ");
        Assert.Equal(iso, dismounted.Value);
        Assert.Equal("\"", dismounted.Tail);
    }

    [Fact]
    public async Task An_iso_path_with_a_double_quote_is_logged_and_never_put_in_a_command()
    {
        var iso = @"C:\Users\me\Downloads\x"";calc;"".iso";
        var fake = OneIso(iso);

        Assert.Null(await Find(fake));

        Assert.DoesNotContain(fake.Calls, c => c.Contains("calc", StringComparison.Ordinal));
        Assert.Contains(fake.Logs, l => l.Contains(iso, StringComparison.Ordinal) && l.Contains("skipped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_wim_path_with_a_double_quote_is_never_passed_to_dism()
    {
        var fake = new FakeSystem();

        var images = await MediaMatcher.ReadImagesAsync(fake, @"E:\a"" /Apply-Image /ImageFile:""x.wim", CancellationToken.None);

        Assert.Empty(images);
        Assert.Empty(fake.Calls);
    }
}
