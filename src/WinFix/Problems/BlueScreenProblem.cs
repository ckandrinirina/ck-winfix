using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Problems;

/// <summary>Repeated blue screens: two or more BugCheck / Kernel-Power 41 events in the last 7 days.</summary>
public sealed class BlueScreenProblem(Func<DateTimeOffset>? clock = null) : IProblemModule
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.Now);

    public string Name => "bluescreen";

    public IReadOnlyList<IRepairStep> Repairs { get; } =
    [
        new RollbackDriverStep(clock),
        new RollbackUpdateStep(clock),
    ];

    public async Task<Health> DetectAsync(ISystem system, CancellationToken ct)
    {
        var crashes = await CrashHistory.ReadAsync(system, _clock(), ct);
        if (crashes.Count < CrashHistory.MinCrashes)
        {
            return new Health(true, "The PC has not crashed repeatedly in the last 7 days.");
        }

        var stopCodes = crashes.Select(CrashHistory.StopCode).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var crash in crashes)
        {
            system.Log($"Crash at {crash.Time:yyyy-MM-dd HH:mm} (event {crash.EventId}) stop code: {CrashHistory.StopCode(crash) ?? "not recorded"}");
        }

        foreach (var dump in await CrashHistory.ListMinidumpsAsync(system, ct))
        {
            system.Log($"Minidump: {dump}");
        }

        var codes = stopCodes.Count > 0 ? $" Stop codes: {string.Join(", ", stopCodes)}." : string.Empty;
        return new Health(false, $"The PC crashed {crashes.Count} times in the last 7 days.{codes}");
    }
}
