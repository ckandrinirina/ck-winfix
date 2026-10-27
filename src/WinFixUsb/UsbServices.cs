using System.Globalization;
using WinFix.Media;

namespace WinFixUsb;

/// <summary>What the flow needs from the network and disk beside the Mac commands; the defaults are the real ones, tests replace them.</summary>
public sealed class UsbServices
{
    public HttpClient Http { get; init; } = new();

    /// <summary>Null: the Microsoft source on <see cref="Http"/>.</summary>
    public IIsoSource? IsoSource { get; init; }

    public IDownloadFiles Files { get; init; } = new DiskDownloadFiles();

    public string DownloadsFolder { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    public string ToolFolder { get; init; } = AppContext.BaseDirectory;

    public string TempExe { get; init; } = Path.Combine(Path.GetTempPath(), "WinFixUsb", "WinFix.exe");

    /// <summary>The Mac's UI language, e.g. fr-FR; the ISO must match the PC it will repair.</summary>
    public string Language { get; init; } = CultureInfo.CurrentUICulture.Name is { Length: > 0 } name ? name : "en-US";

    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;
}
