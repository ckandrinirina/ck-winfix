namespace WinFix.Media;

/// <summary>What to download: "10" or "11", the installed architecture ("x64") and UI language ("en-US").</summary>
public sealed record IsoRequest(string WindowsVersion, string Architecture, string Language);

/// <summary>A download address and, when the source supplies one, the ISO's SHA-256 as hex.</summary>
public sealed record IsoLink(Uri Url, string? Sha256);

/// <summary>Where a Windows ISO comes from; kept behind an interface because the real source is undocumented and may break.</summary>
public interface IIsoSource
{
    /// <summary>Null when no matching link exists; a failure is logged by the source or thrown.</summary>
    Task<IsoLink?> GetLinkAsync(IsoRequest request, CancellationToken ct);
}
