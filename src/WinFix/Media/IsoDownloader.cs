using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using WinFix.System;

namespace WinFix.Media;

/// <summary>Downloads the Windows ISO that matches this PC when no install media was found. Any failure logs and returns null.</summary>
public sealed partial class IsoDownloader(IIsoSource source, HttpClient http, IDownloadFiles files, TextWriter output)
{
    private const long RequiredFreeBytes = 8L * 1024 * 1024 * 1024;
    private const int MaxAttempts = 5;
    private const int Windows11FirstBuild = 22000;
    private const string CurrentVersionKey = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>The matching, mounted media (same shape <see cref="MediaFinder"/> returns), or null.</summary>
    public async Task<InstallMedia?> DownloadAsync(ISystem system, CancellationToken ct)
    {
        try
        {
            return await RunAsync(system, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            system.Log($"Windows ISO download failed: {ex.Message}");
            return null;
        }
    }

    private async Task<InstallMedia?> RunAsync(ISystem system, CancellationToken ct)
    {
        if (!int.TryParse(system.ReadRegistryValue(CurrentVersionKey, "CurrentBuild"), NumberStyles.None, CultureInfo.InvariantCulture, out var build))
        {
            system.Log("Windows ISO download skipped: the installed Windows version could not be read.");
            return null;
        }

        var installed = await MediaMatcher.ReadInstalledAsync(system, ct);
        if (installed is null)
        {
            system.Log("Windows ISO download skipped: the installed Windows edition or language could not be read.");
            return null;
        }

        var drive = SystemDrive().Match(system.WindowsDirectory);
        if (!drive.Success)
        {
            system.Log($"Windows ISO download skipped: {system.WindowsDirectory} is not on a drive letter.");
            return null;
        }

        var letter = drive.Groups[1].Value.ToUpperInvariant();
        var isoPath = $@"{letter}:\ProgramData\WinFix\Windows.iso";
        var partPath = isoPath + ".part";
        if (!await HasRoomAsync(system, letter, files.Length(partPath), ct))
        {
            return null;
        }

        var request = new IsoRequest(build >= Windows11FirstBuild ? "11" : "10", installed.Architecture, installed.Language);
        if (!await GetFileAsync(request, isoPath, system.Log, system.DelayAsync, ct))
        {
            return null;
        }

        return await CheckAsync(system, isoPath, installed, ct);
    }

    /// <summary>
    /// Looks up the link, downloads (resuming a <c>.part</c> file), checks the SHA-256 when given and moves the file to
    /// <paramref name="isoPath"/>. True when it is there; every failure is logged and gives false.
    /// </summary>
    public async Task<bool> DownloadFileAsync(
        IsoRequest request, string isoPath, Action<string> log, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken ct)
    {
        try
        {
            return await GetFileAsync(request, isoPath, log, delay, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log($"Windows ISO download failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> GetFileAsync(
        IsoRequest request, string isoPath, Action<string> log, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken ct)
    {
        var partPath = isoPath + ".part";
        var link = await source.GetLinkAsync(request, ct);
        if (link is null)
        {
            log("Windows ISO download skipped: no download link was found for this PC.");
            return false;
        }

        if (!MicrosoftIsoSource.IsTrustedUrl(link.Url))
        {
            log("Windows ISO download skipped: the link is not https on a Microsoft host.");
            return false;
        }

        if (!await FetchAsync(log, delay, link.Url, partPath, ct))
        {
            return false;
        }

        if (link.Sha256 is not null && !files.Sha256Hex(partPath).Equals(link.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            log("The downloaded Windows ISO does not match its SHA-256 and was deleted.");
            files.Delete(partPath);
            return false;
        }

        files.Move(partPath, isoPath);
        return true;
    }

    private async Task<bool> HasRoomAsync(ISystem system, string letter, long alreadyDownloaded, CancellationToken ct)
    {
        var script = $"(Get-PSDrive -Name {CommandArgs.QuotePowerShell(letter)} -PSProvider FileSystem).Free";
        var result = await system.RunCommandAsync("powershell", $"-NoProfile -NonInteractive -Command \"{script}\"", ct);
        if (result.ExitCode != 0 || !long.TryParse(result.Output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var free))
        {
            system.Log($"Windows ISO download skipped: could not read the free space on {letter}:.");
            return false;
        }

        if (free + alreadyDownloaded >= RequiredFreeBytes)
        {
            return true;
        }

        system.Log($"Windows ISO download skipped: {letter}: has {free / 1073741824.0:0.0} GB free, 8 GB needed.");
        return false;
    }

    private async Task<bool> FetchAsync(Action<string> log, Func<TimeSpan, CancellationToken, Task> delay, Uri url, string partPath, CancellationToken ct)
    {
        var lastPercent = -1;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var offset = files.Length(partPath);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (offset > 0)
            {
                request.Headers.Range = new RangeHeaderValue(offset, null);
            }

            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.RequestMessage?.RequestUri is { } final && !MicrosoftIsoSource.IsTrustedUrl(final))
                {
                    log("Windows ISO download stopped: the server redirected to an address that is not https on a Microsoft host.");
                    return false;
                }

                var range = response.Content.Headers.ContentRange;
                switch (response.StatusCode)
                {
                    case HttpStatusCode.RequestedRangeNotSatisfiable when range?.Length == offset:
                        return true;
                    case HttpStatusCode.RequestedRangeNotSatisfiable:
                    case HttpStatusCode.PartialContent when range?.From != offset:
                        files.Delete(partPath);
                        continue;
                    case HttpStatusCode.PartialContent:
                        lastPercent = await CopyAsync(response, partPath, append: true, offset, range!.Length, lastPercent, ct);
                        break;
                    case HttpStatusCode.OK:
                        lastPercent = await CopyAsync(response, partPath, append: false, 0, response.Content.Headers.ContentLength, lastPercent, ct);
                        break;
                    default:
                        log($"Windows ISO download failed: the server answered {(int)response.StatusCode} {response.StatusCode}.");
                        return false;
                }

                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                log($"Windows ISO download interrupted at {files.Length(partPath)} bytes ({ex.Message}); attempt {attempt} of {MaxAttempts}.");
                await delay(RetryDelay, ct);
            }
        }

        log("Windows ISO download gave up; the partial file is kept to resume next time.");
        return false;
    }

    private async Task<int> CopyAsync(HttpResponseMessage response, string partPath, bool append, long start, long? total, int lastPercent, CancellationToken ct)
    {
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        await using var target = files.Open(partPath, append);
        var buffer = new byte[81920];
        var done = start;
        int read;
        while ((read = await body.ReadAsync(buffer, ct)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (total is > 0 && (int)(done * 100 / total.Value) is var percent && percent > lastPercent)
            {
                lastPercent = percent;
                await output.WriteLineAsync($"Downloading Windows ISO: {percent}%");
            }
        }

        if (total is not null && done != total)
        {
            throw new IOException($"the download ended at {done} of {total} bytes");
        }

        return lastPercent;
    }

    private async Task<InstallMedia?> CheckAsync(ISystem system, string isoPath, InstalledWindows installed, CancellationToken ct)
    {
        var root = await IsoMounter.MountAsync(system, isoPath, ct);
        if (root is null)
        {
            system.Log("The downloaded Windows ISO could not be mounted and was deleted.");
            files.Delete(isoPath);
            return null;
        }

        var image = await FindImageAsync(system, root, ct);
        var images = image is null ? [] : await MediaMatcher.ReadImagesAsync(system, image, ct);
        var best = MediaMatcher.Match(images, installed, out var reason);
        if (image is null || best is null)
        {
            system.Log($"The downloaded Windows ISO was skipped: {(image is null ? "it holds no setup.exe with install.wim or install.esd" : reason)}.");
            await IsoMounter.DismountAsync(system, isoPath, ct);
            files.Delete(isoPath);
            return null;
        }

        return new InstallMedia(root, image, isoPath, best.Build);
    }

    private static async Task<string?> FindImageAsync(ISystem system, string root, CancellationToken ct)
    {
        if (!await ExistsAsync(system, root + "setup.exe", ct))
        {
            return null;
        }

        foreach (var name in new[] { "install.wim", "install.esd" })
        {
            var path = root + @"sources\" + name;
            if (await ExistsAsync(system, path, ct))
            {
                return path;
            }
        }

        return null;
    }

    private static async Task<bool> ExistsAsync(ISystem system, string path, CancellationToken ct)
    {
        var result = await system.RunCommandAsync(
            "powershell", $"-NoProfile -NonInteractive -Command \"Test-Path -LiteralPath {CommandArgs.QuotePowerShell(path)}\"", ct);
        return result.ExitCode == 0 && result.Output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^([A-Za-z]):\\")]
    private static partial Regex SystemDrive();
}
