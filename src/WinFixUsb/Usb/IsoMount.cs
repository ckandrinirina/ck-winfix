using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using WinFix.System;
using WinFixUsb.Disks;
using WinFixUsb.System;

namespace WinFixUsb.Usb;

/// <summary>A Windows ISO attached read-only by hdiutil.</summary>
public sealed partial record IsoMount(string MountPoint)
{
    private const string UnsafeMountMessage = "The ISO mounted at a path WinFixUsb will not pass to other commands.";

    /// <summary>
    /// Mount is null when the attach failed or its mount point is missing or unsafe (the image is then detached);
    /// the result is hdiutil's, or a failure naming the unsafe path.
    /// </summary>
    public static async Task<(IsoMount? Mount, CommandResult Result)> AttachAsync(
        IMacSystem system, string iso, CancellationToken ct)
    {
        var result = await system.RunCommandAsync("hdiutil", ["attach", "-nobrowse", "-readonly", "-plist", iso], ct);
        if (result.ExitCode != 0)
        {
            return (null, result);
        }

        var (mountPoint, device) = Parse(result.Output);
        if (mountPoint is not null && IsSafe(mountPoint))
        {
            return (new IsoMount(mountPoint), result);
        }

        if (device is not null)
        {
            await system.RunCommandAsync("hdiutil", ["detach", device], ct);
        }

        return (null, mountPoint is null ? result : new CommandResult(1, string.Empty, UnsafeMountMessage));
    }

    public Task<CommandResult> DetachAsync(IMacSystem system, CancellationToken ct) =>
        system.RunCommandAsync("hdiutil", ["detach", MountPoint], ct);

    /// <summary>Under /Volumes/, no quote, backslash, control character or .. segment; \z so a trailing newline fails.</summary>
    private static bool IsSafe(string mountPoint) =>
        SafeMountPoint().IsMatch(mountPoint) && !mountPoint.Split('/').Contains("..");

    [GeneratedRegex(@"^/Volumes/[^""\\\x00-\x1f]+\z")]
    private static partial Regex SafeMountPoint();

    [GeneratedRegex(@"^/dev/disk\d+\z")]
    private static partial Regex WholeDisk();

    /// <summary>The first mount-point string and the first whole-disk device in an `hdiutil attach -plist` document.</summary>
    private static (string? MountPoint, string? Device) Parse(string plist)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(plist);
        }
        catch (XmlException)
        {
            return (null, null);
        }

        var entities = (DiskList.Value(doc.Root?.Element("dict"), "system-entities")?.Elements("dict") ?? []).ToList();
        var mountPoint = entities
            .Select(e => DiskList.Value(e, "mount-point")?.Value)
            .FirstOrDefault(m => !string.IsNullOrEmpty(m));
        var device = entities
            .Select(e => DiskList.Value(e, "dev-entry")?.Value)
            .FirstOrDefault(d => d is not null && WholeDisk().IsMatch(d));
        return (mountPoint, device);
    }
}
