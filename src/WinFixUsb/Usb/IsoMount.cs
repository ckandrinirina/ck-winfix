using System.Xml;
using System.Xml.Linq;
using WinFix.System;
using WinFixUsb.Disks;
using WinFixUsb.System;

namespace WinFixUsb.Usb;

/// <summary>A Windows ISO attached read-only by hdiutil.</summary>
public sealed record IsoMount(string MountPoint)
{
    /// <summary>Mount is null when the attach failed or the plist names no mount point; the result is hdiutil's.</summary>
    public static async Task<(IsoMount? Mount, CommandResult Result)> AttachAsync(
        IMacSystem system, string iso, CancellationToken ct)
    {
        var result = await system.RunCommandAsync("hdiutil", $"attach -nobrowse -readonly -plist {UsbWriter.Quote(iso)}", ct);
        var mountPoint = result.ExitCode == 0 ? ParseMountPoint(result.Output) : null;
        return (mountPoint is null ? null : new IsoMount(mountPoint), result);
    }

    public Task<CommandResult> DetachAsync(IMacSystem system, CancellationToken ct) =>
        system.RunCommandAsync("hdiutil", $"detach {UsbWriter.Quote(MountPoint)}", ct);

    /// <summary>The first mount-point string in an `hdiutil attach -plist` document.</summary>
    public static string? ParseMountPoint(string plist)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(plist);
        }
        catch (XmlException)
        {
            return null;
        }

        var entities = DiskList.Value(doc.Root?.Element("dict"), "system-entities")?.Elements("dict") ?? [];
        return entities
            .Select(e => DiskList.Value(e, "mount-point")?.Value)
            .FirstOrDefault(m => !string.IsNullOrEmpty(m));
    }
}
