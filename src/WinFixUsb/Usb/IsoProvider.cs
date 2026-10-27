using WinFix.Media;

namespace WinFixUsb.Usb;

/// <summary>Downloads the Windows 11 x64 ISO into a folder through the shared <see cref="IsoDownloader"/>.</summary>
public sealed class IsoProvider(
    IIsoSource source, HttpClient http, IDownloadFiles files, string downloadsFolder, string language,
    Func<TimeSpan, CancellationToken, Task> delay)
{
    public const string ManualPage = "https://www.microsoft.com/software-download/windows11";

    /// <summary>The ISO's path, or null after a plain message naming the manual download page.</summary>
    public async Task<string?> DownloadAsync(TextWriter output, CancellationToken ct)
    {
        var path = Path.Combine(downloadsFolder, $"Windows11_x64_{language}.iso");
        var downloader = new IsoDownloader(source, http, files, output);
        if (await downloader.DownloadFileAsync(new IsoRequest("11", "x64", language), path, output.WriteLine, delay, ct))
        {
            return path;
        }

        output.WriteLine($"Windows could not be downloaded. Download the Windows 11 ISO yourself from {ManualPage} and run: WinFixUsb --iso <path>. Nothing was erased.");
        return null;
    }
}
