using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using WinFixUsb.System;

namespace WinFixUsb.Disks;

/// <summary>An external physical disk; size is bytes as diskutil reports them (decimal).</summary>
public sealed record Disk(string Identifier, string Name, long SizeBytes)
{
    public bool IsBigEnough => SizeBytes >= DiskList.MinimumBytes;

    public string SizeText => $"{(SizeBytes / 1e9).ToString("0.0", CultureInfo.InvariantCulture)} GB";
}

public static class DiskList
{
    /// <summary>8 GB, decimal, as diskutil counts.</summary>
    public const long MinimumBytes = 8_000_000_000;

    private const string UnnamedDisk = "Unnamed disk";

    public static async Task<IReadOnlyList<Disk>> ListAsync(IMacSystem system, CancellationToken ct)
    {
        var result = await system.RunCommandAsync("diskutil", ["list", "-plist", "external", "physical"], ct);
        return result.ExitCode == 0 ? Parse(result.Output) : [];
    }

    /// <summary>Whole disks from a `diskutil list -plist` document; internal and synthesized (APFS container) disks are dropped.</summary>
    public static IReadOnlyList<Disk> Parse(string plist)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(plist);
        }
        catch (XmlException)
        {
            return [];
        }

        var entries = Value(doc.Root?.Element("dict"), "AllDisksAndPartitions")?.Elements("dict") ?? [];
        return
        [
            .. entries
                .Where(e => Value(e, "Internal")?.Name.LocalName != "true" && Value(e, "APFSVolumes") is null)
                .Select(ToDisk)
                .OfType<Disk>(),
        ];
    }

    private static Disk? ToDisk(XElement entry)
    {
        var id = Value(entry, "DeviceIdentifier")?.Value;
        if (string.IsNullOrEmpty(id) || !long.TryParse(Value(entry, "Size")?.Value, CultureInfo.InvariantCulture, out var size))
        {
            return null;
        }

        var name = Value(entry, "MediaName")?.Value
            ?? Value(entry, "Partitions")?.Elements("dict").Select(p => Value(p, "VolumeName")?.Value).FirstOrDefault(v => v is not null)
            ?? UnnamedDisk;
        return new Disk(id, name, size);
    }

    /// <summary>The element after the &lt;key&gt; named <paramref name="key"/> in a plist dict.</summary>
    internal static XElement? Value(XElement? dict, string key) =>
        dict?.Elements("key").FirstOrDefault(k => k.Value == key)?.NextNode as XElement;
}
