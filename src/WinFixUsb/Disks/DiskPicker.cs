using WinFixUsb.System;

namespace WinFixUsb.Disks;

public static class DiskPicker
{
    /// <summary>
    /// Returns the disk the user confirmed by typing its identifier, or null with the reason printed:
    /// nothing may be erased on null.
    /// </summary>
    public static async Task<Disk?> PickAsync(IMacSystem system, string? requested, TextWriter output, CancellationToken ct)
    {
        var disks = await DiskList.ListAsync(system, ct);
        foreach (var disk in disks)
        {
            var note = disk.IsBigEnough ? "" : "  (too small, needs 8 GB)";
            output.WriteLine($"   {disk.Identifier}  {disk.Name}  {disk.SizeText}{note}");
        }

        var chosen = disks.Where(d => d.IsBigEnough).ToList();
        if (chosen.Count == 0)
        {
            output.WriteLine("Plug in a USB drive of at least 8 GB and run again.");
            return null;
        }

        string prompt;
        if (requested is not null)
        {
            var disk = chosen.FirstOrDefault(d => d.Identifier == requested);
            if (disk is null)
            {
                output.WriteLine($"{requested} is not an external USB disk of at least 8 GB. Nothing was erased.");
                return null;
            }

            chosen = [disk];
            prompt = $"Everything on {disk.Identifier} ({disk.Name}, {disk.SizeText}) will be erased. Type {disk.Identifier} to confirm:";
        }
        else
        {
            prompt = "Everything on the disk you pick will be erased. Type its identifier (for example disk4) to confirm:";
        }

        var answer = (await system.AskAsync(prompt, ct))?.Trim();
        var picked = chosen.FirstOrDefault(d => d.Identifier == answer);
        if (picked is null)
        {
            output.WriteLine("That is not a disk you can erase. Nothing was erased.");
        }

        return picked;
    }
}
