using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Problems;

/// <summary>Disk errors or corrupt system files: a dirty volume, chkdsk errors, or a repairable component store.</summary>
public sealed class DiskProblem(IReadOnlyList<IRepairStep>? repairs = null) : IProblemModule
{
    public string Name => "disk";

    public IReadOnlyList<IRepairStep> Repairs { get; } =
        repairs ?? [new DismRestoreHealthStep(), new SfcScanStep(), new ChkdskStep()];

    public async Task<Health> DetectAsync(ISystem system, CancellationToken ct)
    {
        var dirty = await system.RunCommandAsync("fsutil", "dirty query C:", ct);
        if (Contains(dirty.Output, "is dirty"))
        {
            return Broken("Drive C: is marked dirty.");
        }

        var scan = await system.RunCommandAsync("chkdsk", "C: /scan", ct);
        if (scan.ExitCode == 1 || Contains(scan.Output, "found problems") || Contains(scan.Output, "errors found"))
        {
            return Broken("The disk check found errors on drive C:.");
        }

        var store = await system.RunCommandAsync("DISM", "/Online /Cleanup-Image /CheckHealth", ct);
        return Contains(store.Output, "is repairable")
            ? Broken("The Windows component store is damaged but can be repaired.")
            : new Health(true, "The disk and system files look healthy.");
    }

    private static bool Contains(string output, string text) =>
        output.Replace("\0", string.Empty).Contains(text, StringComparison.OrdinalIgnoreCase);

    private static Health Broken(string reason) => new(false, reason);
}
