using WinFix.Media;
using WinFixUsb.System;

namespace WinFixUsb.Usb;

/// <summary>Finds WinFix.exe beside the tool, else downloads the latest release into a temp file, before the disk is erased.</summary>
public sealed class WinFixExeProvider(IMacSystem system, HttpClient http, IDownloadFiles files, string toolFolder, string tempFile)
{
    public const string DownloadUrl = "https://github.com/ckandrinirina/ck-winfix/releases/latest/download/WinFix.exe";

    /// <summary>The exe's path, or null after a warning: the USB is then finished without it.</summary>
    public async Task<string?> StageAsync(TextWriter output, CancellationToken ct)
    {
        var beside = Path.Combine(toolFolder, "WinFix.exe");
        if (system.FileSize(beside) is not null)
        {
            return beside;
        }

        output.WriteLine("WinFix.exe is not beside this tool. Downloading it from GitHub.");
        try
        {
            using var response = await http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using (var body = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = files.Open(tempFile, append: false))
            {
                await body.CopyToAsync(target, ct);
            }

            if (files.Length(tempFile) > 0)
            {
                return tempFile;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            output.WriteLine($"WinFix.exe download failed: {ex.Message}");
        }

        files.Delete(tempFile);
        output.WriteLine($"Warning: WinFix.exe was not found and could not be downloaded, so it will not be on the USB. Download it from {DownloadUrl} and copy it there yourself.");
        return null;
    }

    /// <summary>Removes the downloaded copy; the exe beside the tool is never touched.</summary>
    public void Cleanup() => files.Delete(tempFile);
}
