using WinFix.Repairs;
using WinFix.System;

namespace WinFix.Problems;

public sealed record Health(bool Healthy, string Reason);

/// <summary>One problem family: how to detect it and its repairs, cheapest first.</summary>
public interface IProblemModule
{
    /// <summary>Lowercase; matched case-insensitively by --only.</summary>
    string Name { get; }

    Task<Health> DetectAsync(ISystem system, CancellationToken ct);

    IReadOnlyList<IRepairStep> Repairs { get; }
}
