using WinFixUsb.Disks;
using WinFixUsb.System;

namespace WinFixUsb.Tests.Disks;

/// <summary>Builds `diskutil list -plist external physical` output.</summary>
internal static class DiskFixtures
{
    public static string Usb(string id, long size, string? mediaName = "SanDisk Ultra") =>
        Entry(id, size, mediaName is null ? "" : $"<key>MediaName</key><string>{mediaName}</string>");

    public static string Internal(string id, long size) =>
        Entry(id, size, "<key>Internal</key><true/><key>MediaName</key><string>APPLE SSD</string>");

    public static string Synthesized(string id, long size) =>
        Entry(id, size, "<key>APFSVolumes</key><array><dict><key>DeviceIdentifier</key><string>" + id + "s1</string></dict></array>");

    public static string NamedByVolume(string id, long size, string volume) =>
        Entry(id, size, $"<key>Partitions</key><array><dict><key>DeviceIdentifier</key><string>{id}s1</string><key>VolumeName</key><string>{volume}</string></dict></array>");

    public static string Plist(params string[] entries) =>
        """<?xml version="1.0" encoding="UTF-8"?><plist version="1.0"><dict><key>AllDisksAndPartitions</key><array>"""
        + string.Concat(entries)
        + "</array></dict></plist>";

    private static string Entry(string id, long size, string extra) =>
        $"<dict><key>DeviceIdentifier</key><string>{id}</string><key>Size</key><integer>{size}</integer>{extra}</dict>";

    public static FakeMacSystem Fake(params string[] entries) => new() { ExternalDisks = Plist(entries) };
}

public class DiskListTests
{
    [Fact]
    public void Parse_reads_identifier_name_and_size()
    {
        var plist = DiskFixtures.Plist(DiskFixtures.Usb("disk4", 32_000_000_000, "SanDisk Ultra"));

        var disk = Assert.Single(DiskList.Parse(plist));

        Assert.Equal("disk4", disk.Identifier);
        Assert.Equal("SanDisk Ultra", disk.Name);
        Assert.Equal(32_000_000_000, disk.SizeBytes);
    }

    [Fact]
    public void Parse_never_offers_an_internal_disk()
    {
        var plist = DiskFixtures.Plist(
            DiskFixtures.Internal("disk0", 500_000_000_000),
            DiskFixtures.Usb("disk4", 32_000_000_000));

        Assert.Equal(["disk4"], DiskList.Parse(plist).Select(d => d.Identifier));
    }

    [Fact]
    public void Parse_never_offers_a_synthesized_disk()
    {
        var plist = DiskFixtures.Plist(
            DiskFixtures.Synthesized("disk3", 500_000_000_000),
            DiskFixtures.Usb("disk4", 32_000_000_000));

        Assert.Equal(["disk4"], DiskList.Parse(plist).Select(d => d.Identifier));
    }

    [Fact]
    public void Parse_names_a_disk_by_its_first_volume_when_it_has_no_media_name()
    {
        var plist = DiskFixtures.Plist(DiskFixtures.NamedByVolume("disk4", 32_000_000_000, "BACKUP"));

        Assert.Equal("BACKUP", Assert.Single(DiskList.Parse(plist)).Name);
    }

    [Fact]
    public void Parse_gives_a_nameless_disk_a_plain_placeholder_name()
    {
        var plist = DiskFixtures.Plist(DiskFixtures.Usb("disk4", 32_000_000_000, mediaName: null));

        Assert.Equal("Unnamed disk", Assert.Single(DiskList.Parse(plist)).Name);
    }

    [Theory]
    [InlineData(7_999_999_999, false)]
    [InlineData(8_000_000_000, true)]
    [InlineData(4_000_000_000, false)]
    public void A_disk_needs_at_least_8_000_000_000_bytes(long size, bool expected)
    {
        var plist = DiskFixtures.Plist(DiskFixtures.Usb("disk6", size));

        Assert.Equal(expected, Assert.Single(DiskList.Parse(plist)).IsBigEnough);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a plist")]
    [InlineData("<plist><dict></dict></plist>")]
    public void Parse_returns_no_disks_for_output_without_any(string output)
    {
        Assert.Empty(DiskList.Parse(output));
    }

    [Fact]
    public async Task ListAsync_runs_diskutil_for_external_physical_disks_through_the_system()
    {
        var fake = DiskFixtures.Fake(DiskFixtures.Usb("disk4", 32_000_000_000));

        var disks = await DiskList.ListAsync(fake, CancellationToken.None);

        Assert.Equal("disk4", Assert.Single(disks).Identifier);
        Assert.Equal(["RunCommand diskutil list -plist external physical"], fake.Calls);
    }

    [Fact]
    public async Task The_default_fake_lists_two_usb_disks()
    {
        var disks = await DiskList.ListAsync(new FakeMacSystem(), CancellationToken.None);

        Assert.Equal(["disk4", "disk5"], disks.Select(d => d.Identifier));
    }

    [Theory]
    [InlineData(32_000_000_000, "32.0 GB")]
    [InlineData(500_107_862_016, "500.1 GB")]
    public void Size_text_is_decimal_gigabytes(long size, string expected)
    {
        Assert.Equal(expected, new Disk("disk4", "x", size).SizeText);
    }
}
