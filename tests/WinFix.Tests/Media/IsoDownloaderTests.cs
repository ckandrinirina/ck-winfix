using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using WinFix.Media;
using WinFix.System;

namespace WinFix.Tests.Media;

public class IsoDownloaderTests
{
    private const string Cpu = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    private const string Nt = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
    private const string IsoPath = @"C:\ProgramData\WinFix\Windows.iso";
    private const string PartPath = @"C:\ProgramData\WinFix\Windows.iso.part";
    private const long EightGiB = 8L * 1024 * 1024 * 1024;
    private static readonly Uri TrustedUrl = new("https://software.download.prss.microsoft.com/dbazure/Win11_en-US.iso");
    private static readonly byte[] Payload = Enumerable.Range(0, 1000).Select(i => (byte)(i % 251)).ToArray();

    private static string Dism(string arch, string edition, string version, string lang) =>
        $"""
        Deployment Image Servicing and Management tool
        Version: 10.0.26100.1

        Details for image : H:\sources\install.wim

        Index : 1
        Name : Windows
        Architecture : {arch}
        Version : {version}
        Edition : {edition}
        Languages :
        	{lang} (Default)

        The operation completed successfully.
        """;

    private sealed class Pc
    {
        public long FreeBytes { get; set; } = 20L * 1024 * 1024 * 1024;

        public string Build { get; set; } = "22631";

        public string Wim { get; set; } = Dism("x64", "Professional", "10.0.22631.1", "en-US");

        public List<string> Commands { get; } = [];

        public int Dismounts => Commands.Count(c => c.Contains("Dismount-DiskImage", StringComparison.Ordinal));

        public FakeSystem Build_(string windowsDirectory = @"C:\Windows")
        {
            var fake = new FakeSystem { CommandResponder = Respond, WindowsDirectory = windowsDirectory };
            fake.RegistryValues[(Cpu, "PROCESSOR_ARCHITECTURE")] = "AMD64";
            fake.RegistryValues[(Nt, "EditionID")] = "Professional";
            fake.RegistryValues[(Nt, "CurrentBuild")] = Build;
            return fake;
        }

        private CommandResult Respond(string file, string args)
        {
            Commands.Add(args);
            if (args.Contains("InstalledUICulture", StringComparison.Ordinal))
            {
                return new(0, "en-US");
            }

            if (args.Contains("Get-PSDrive", StringComparison.Ordinal))
            {
                return new(0, FreeBytes.ToString());
            }

            if (args.Contains("Mount-DiskImage", StringComparison.Ordinal))
            {
                return new(0, "H");
            }

            if (args.Contains("Test-Path", StringComparison.Ordinal))
            {
                return new(0, "True");
            }

            return args.Contains("Get-WimInfo", StringComparison.Ordinal) ? new(0, Wim) : new(0, string.Empty);
        }
    }

    /// <summary>Disk in memory; a stream stays readable after the downloader closes it.</summary>
    private sealed class MemoryFiles : IDownloadFiles
    {
        private sealed class Kept : MemoryStream
        {
            protected override void Dispose(bool disposing)
            {
            }
        }

        public Dictionary<string, MemoryStream> Data { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Seed(string path, byte[] bytes) => Open(path, false).Write(bytes);

        public long Length(string path) => Data.TryGetValue(path, out var s) ? s.Length : 0;

        public Stream Open(string path, bool append)
        {
            if (!append || !Data.ContainsKey(path))
            {
                Data[path] = new Kept();
            }

            Data[path].Seek(0, SeekOrigin.End);
            return Data[path];
        }

        public void Move(string from, string to)
        {
            Data[to] = Data[from];
            Data.Remove(from);
        }

        public void Delete(string path) => Data.Remove(path);

        public string Sha256Hex(string path) => Convert.ToHexString(SHA256.HashData(Data[path].ToArray()));
    }

    private sealed class ChunkStream(byte[] data, int chunk, int failAfter = -1) : Stream
    {
        private int _pos;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (failAfter >= 0 && _pos >= failAfter)
            {
                throw new IOException("connection reset");
            }

            var limit = failAfter >= 0 ? Math.Min(failAfter, data.Length) : data.Length;
            var n = Math.Min(Math.Min(count, chunk), limit - _pos);
            Array.Copy(data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(Uri Url, long? RangeFrom)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!, request.Headers.Range?.Ranges.First().From));
            var response = respond(request, Requests.Count - 1);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class FakeSource(IsoLink? link, Exception? failure = null) : IIsoSource
    {
        public List<IsoRequest> Requests { get; } = [];

        public Task<IsoLink?> GetLinkAsync(IsoRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return failure is null ? Task.FromResult(link) : Task.FromException<IsoLink?>(failure);
        }
    }

    private static HttpResponseMessage Full(byte[] data, int chunk = 4096, int failAfter = -1)
    {
        var content = new StreamContent(new ChunkStream(data, chunk, failAfter));
        content.Headers.ContentLength = data.Length;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpResponseMessage Range(byte[] data, long from, int chunk = 4096, int failAfter = -1)
    {
        var content = new StreamContent(new ChunkStream(data[(int)from..], chunk, failAfter));
        content.Headers.ContentLength = data.Length - from;
        content.Headers.ContentRange = new ContentRangeHeaderValue(from, data.Length - 1, data.Length);
        return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content };
    }

    private static (IsoDownloader Downloader, FakeHandler Handler, FakeSource Source, MemoryFiles Files, StringWriter Output) Rig(
        Func<HttpRequestMessage, int, HttpResponseMessage> respond, IsoLink? link = null, Exception? sourceFailure = null)
    {
        var handler = new FakeHandler(respond);
        var source = new FakeSource(link ?? new IsoLink(TrustedUrl, null), sourceFailure);
        var files = new MemoryFiles();
        var output = new StringWriter();
        return (new IsoDownloader(source, new HttpClient(handler), files, output), handler, source, files, output);
    }

    [Fact]
    public async Task Asks_the_source_for_the_installed_version_language_and_architecture()
    {
        var pc = new Pc();
        var rig = Rig((_, _) => Full(Payload));

        await rig.Downloader.DownloadAsync(pc.Build_(), CancellationToken.None);

        Assert.Equal(new IsoRequest("11", "x64", "en-US"), Assert.Single(rig.Source.Requests));
    }

    [Fact]
    public async Task Windows_10_builds_ask_for_Windows_10()
    {
        var pc = new Pc { Build = "19045" };
        var rig = Rig((_, _) => Full(Payload));

        await rig.Downloader.DownloadAsync(pc.Build_(), CancellationToken.None);

        Assert.Equal("10", Assert.Single(rig.Source.Requests).WindowsVersion);
    }

    [Fact]
    public async Task An_unreadable_installed_Windows_asks_nothing_and_fails()
    {
        var pc = new Pc();
        var system = pc.Build_();
        system.RegistryValues.Remove((Nt, "CurrentBuild"));
        var rig = Rig((_, _) => Full(Payload));

        var media = await rig.Downloader.DownloadAsync(system, CancellationToken.None);

        Assert.Null(media);
        Assert.Empty(rig.Source.Requests);
        Assert.Empty(rig.Handler.Requests);
        Assert.Contains(system.Logs, l => l.Contains("version", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Less_than_8_GB_free_logs_the_shortfall_and_downloads_nothing()
    {
        var pc = new Pc { FreeBytes = EightGiB - 1 };
        var system = pc.Build_();
        var rig = Rig((_, _) => Full(Payload));

        var media = await rig.Downloader.DownloadAsync(system, CancellationToken.None);

        Assert.Null(media);
        Assert.Empty(rig.Handler.Requests);
        Assert.Contains(system.Logs, l => l.Contains("8 GB", StringComparison.Ordinal) && l.Contains("C:", StringComparison.Ordinal));
        Assert.Contains(pc.Commands, c => c.Contains("Get-PSDrive -Name 'C'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Exactly_8_GB_free_starts_the_download()
    {
        var pc = new Pc { FreeBytes = EightGiB };
        var rig = Rig((_, _) => Full(Payload));

        var media = await rig.Downloader.DownloadAsync(pc.Build_(), CancellationToken.None);

        Assert.NotNull(media);
        Assert.Single(rig.Handler.Requests);
    }

    [Fact]
    public async Task An_unreadable_free_space_fails_without_downloading()
    {
        var pc = new Pc();
        var system = new FakeSystem
        {
            CommandResponder = (f, a) => a.Contains("Get-PSDrive", StringComparison.Ordinal) ? new(1, "boom") : pc.Build_().CommandResponder!(f, a),
        };
        system.RegistryValues[(Cpu, "PROCESSOR_ARCHITECTURE")] = "AMD64";
        system.RegistryValues[(Nt, "EditionID")] = "Professional";
        system.RegistryValues[(Nt, "CurrentBuild")] = "22631";
        var rig = Rig((_, _) => Full(Payload));

        Assert.Null(await rig.Downloader.DownloadAsync(system, CancellationToken.None));
        Assert.Empty(rig.Handler.Requests);
        Assert.Contains(system.Logs, l => l.Contains("free space", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_target_that_is_not_a_drive_letter_path_is_refused()
    {
        var pc = new Pc();
        var system = pc.Build_(@"\\server\share'; calc");
        var rig = Rig((_, _) => Full(Payload));

        Assert.Null(await rig.Downloader.DownloadAsync(system, CancellationToken.None));
        Assert.Empty(rig.Handler.Requests);
        Assert.DoesNotContain(pc.Commands, c => c.Contains("calc", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_finished_download_is_the_complete_file_and_is_returned_as_mounted_media()
    {
        var pc = new Pc();
        var rig = Rig((_, _) => Full(Payload));

        var media = await rig.Downloader.DownloadAsync(pc.Build_(), CancellationToken.None);

        Assert.NotNull(media);
        Assert.Equal(IsoPath, media.IsoPath);
        Assert.Equal(@"H:\", media.Root);
        Assert.Equal(@"H:\sources\install.wim", media.ImagePath);
        Assert.Equal(22631, media.Build);
        Assert.Equal(Payload, rig.Files.Data[IsoPath].ToArray());
        Assert.False(rig.Files.Data.ContainsKey(PartPath));
        Assert.Equal(0, pc.Dismounts);
    }

    [Fact]
    public async Task An_interrupted_download_resumes_from_where_it_stopped()
    {
        var pc = new Pc();
        var rig = Rig((_, n) => n == 0 ? Full(Payload, 100, failAfter: 300) : Range(Payload, 300));

        var media = await rig.Downloader.DownloadAsync(pc.Build_(), CancellationToken.None);

        Assert.NotNull(media);
        Assert.Equal([null, 300L], rig.Handler.Requests.Select(r => r.RangeFrom));
        Assert.Equal(Payload, rig.Files.Data[IsoPath].ToArray());
    }

    [Fact]
    public async Task A_partial_file_from_an_earlier_run_is_continued_with_a_range_request()
    {
        var pc = new Pc();
        var rig = Rig((_, _) => Range(Payload, 400));
        rig.Files.Seed(PartPath, Payload[..400]);

        var media = await rig.Downloader.DownloadAsync(pc.Build_(), CancellationToken.None);

        Assert.NotNull(media);
        Assert.Equal(400L, rig.Handler.Requests[0].RangeFrom);
        Assert.Equal(Payload, rig.Files.Data[IsoPath].ToArray());
    }

    [Fact]
    public async Task A_full_response_to_a_range_request_restarts_from_zero()
    {
        var pc = new Pc();
        var rig = Rig((_, _) => Full(Payload));
        rig.Files.Seed(PartPath, [9, 9, 9, 9]);

        var media = await rig.Downloader.DownloadAsync(pc.Build_(), CancellationToken.None);

        Assert.NotNull(media);
        Assert.Equal(4L, rig.Handler.Requests[0].RangeFrom);
        Assert.Equal(Payload, rig.Files.Data[IsoPath].ToArray());
    }

    [Fact]
    public async Task A_partial_response_that_starts_elsewhere_is_not_appended()
    {
        var pc = new Pc();
        var rig = Rig((_, n) => n == 0 ? Range(Payload, 10) : Full(Payload));
        rig.Files.Seed(PartPath, Payload[..400]);

        var media = await rig.Downloader.DownloadAsync(pc.Build_(), CancellationToken.None);

        Assert.NotNull(media);
        Assert.Equal(Payload, rig.Files.Data[IsoPath].ToArray());
    }

    [Fact]
    public async Task A_range_not_satisfiable_answer_discards_the_partial_and_starts_over()
    {
        var pc = new Pc();
        var rig = Rig((_, n) => n == 0 ? new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable) : Full(Payload));
        rig.Files.Seed(PartPath, [1, 2, 3]);

        var media = await rig.Downloader.DownloadAsync(pc.Build_(), CancellationToken.None);

        Assert.NotNull(media);
        Assert.Equal([3L, null], rig.Handler.Requests.Select(r => r.RangeFrom));
        Assert.Equal(Payload, rig.Files.Data[IsoPath].ToArray());
    }

    [Fact]
    public async Task A_download_that_keeps_breaking_gives_up_and_keeps_the_partial_file()
    {
        var pc = new Pc();
        var system = pc.Build_();
        var rig = Rig((_, n) => n == 0 ? Full(Payload, 100, failAfter: 200) : Range(Payload, 200, 100, failAfter: 0));

        var media = await rig.Downloader.DownloadAsync(system, CancellationToken.None);

        Assert.Null(media);
        Assert.InRange(rig.Handler.Requests.Count, 2, 10);
        Assert.Equal(200, rig.Files.Length(PartPath));
        Assert.Contains(system.Logs, l => l.Contains("interrupted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Progress_prints_each_whole_percent_once()
    {
        var pc = new Pc();
        var rig = Rig((_, _) => Full(Payload, chunk: 7));

        await rig.Downloader.DownloadAsync(pc.Build_(), CancellationToken.None);

        var lines = rig.Output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.EndsWith('%')).ToList();
        Assert.Equal(lines.Distinct().Count(), lines.Count);
        Assert.InRange(lines.Count, 2, 101);
        Assert.Contains(lines, l => l.EndsWith("50%", StringComparison.Ordinal));
        Assert.EndsWith("100%", lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_downloaded_ISO_that_does_not_match_is_dismounted_deleted_and_refused()
    {
        var pc = new Pc { Wim = Dism("x64", "Professional", "10.0.22631.1", "fr-FR") };
        var system = pc.Build_();
        var rig = Rig((_, _) => Full(Payload));

        var media = await rig.Downloader.DownloadAsync(system, CancellationToken.None);

        Assert.Null(media);
        Assert.Equal(1, pc.Dismounts);
        Assert.False(rig.Files.Data.ContainsKey(IsoPath));
        Assert.Contains(system.Logs, l => l.Contains("language", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_downloaded_ISO_that_cannot_be_mounted_is_deleted_and_refused()
    {
        var pc = new Pc();
        var system = new FakeSystem
        {
            CommandResponder = (f, a) => a.Contains("Mount-DiskImage", StringComparison.Ordinal) ? new(1, "") : pc.Build_().CommandResponder!(f, a),
        };
        system.RegistryValues[(Cpu, "PROCESSOR_ARCHITECTURE")] = "AMD64";
        system.RegistryValues[(Nt, "EditionID")] = "Professional";
        system.RegistryValues[(Nt, "CurrentBuild")] = "22631";
        var rig = Rig((_, _) => Full(Payload));

        Assert.Null(await rig.Downloader.DownloadAsync(system, CancellationToken.None));
        Assert.False(rig.Files.Data.ContainsKey(IsoPath));
    }

    [Fact]
    public async Task A_matching_SHA256_is_accepted()
    {
        var pc = new Pc();
        var hash = Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant();
        var rig = Rig((_, _) => Full(Payload), new IsoLink(TrustedUrl, hash));

        Assert.NotNull(await rig.Downloader.DownloadAsync(pc.Build_(), CancellationToken.None));
    }

    [Fact]
    public async Task A_SHA256_mismatch_discards_the_file()
    {
        var pc = new Pc();
        var system = pc.Build_();
        var rig = Rig((_, _) => Full(Payload), new IsoLink(TrustedUrl, new string('a', 64)));

        Assert.Null(await rig.Downloader.DownloadAsync(system, CancellationToken.None));
        Assert.Empty(rig.Files.Data);
        Assert.Equal(0, pc.Dismounts);
        Assert.Contains(system.Logs, l => l.Contains("SHA-256", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://software.download.prss.microsoft.com/a.iso")]
    [InlineData("https://evil.example.com/a.iso")]
    [InlineData("https://microsoft.com.evil.example/a.iso")]
    [InlineData("https://notmicrosoft.com/a.iso")]
    public async Task A_link_that_is_not_https_on_a_Microsoft_host_is_never_fetched(string url)
    {
        var pc = new Pc();
        var system = pc.Build_();
        var rig = Rig((_, _) => Full(Payload), new IsoLink(new Uri(url), null));

        Assert.Null(await rig.Downloader.DownloadAsync(system, CancellationToken.None));
        Assert.Empty(rig.Handler.Requests);
        Assert.Contains(system.Logs, l => l.Contains("Microsoft", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_redirect_to_a_foreign_host_is_refused()
    {
        var pc = new Pc();
        var system = pc.Build_();
        var files = new MemoryFiles();
        var downloader = new IsoDownloader(new FakeSource(new IsoLink(TrustedUrl, null)), new HttpClient(new RedirectingHandler()), files, new StringWriter());

        Assert.Null(await downloader.DownloadAsync(system, CancellationToken.None));
        Assert.Empty(files.Data);
    }

    private sealed class RedirectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Payload),
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example.com/a.iso"),
            });
    }

    [Fact]
    public async Task No_link_from_the_source_is_logged_and_fails()
    {
        var pc = new Pc();
        var system = pc.Build_();
        var handler = new FakeHandler((_, _) => Full(Payload));
        var source = new FakeSource(null);
        var downloader = new IsoDownloader(source, new HttpClient(handler), new MemoryFiles(), new StringWriter());

        Assert.Null(await downloader.DownloadAsync(system, CancellationToken.None));
        Assert.Empty(handler.Requests);
        Assert.Contains(system.Logs, l => l.Contains("link", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_source_that_throws_is_logged_and_the_chain_can_move_on()
    {
        var pc = new Pc();
        var system = pc.Build_();
        var rig = Rig((_, _) => Full(Payload), sourceFailure: new HttpRequestException("no network"));

        Assert.Null(await rig.Downloader.DownloadAsync(system, CancellationToken.None));
        Assert.Contains(system.Logs, l => l.Contains("no network", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_server_error_is_logged_and_fails()
    {
        var pc = new Pc();
        var system = pc.Build_();
        var rig = Rig((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Assert.Null(await rig.Downloader.DownloadAsync(system, CancellationToken.None));
        Assert.Contains(system.Logs, l => l.Contains("500", StringComparison.Ordinal) || l.Contains("InternalServerError", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_network_failure_on_the_request_is_logged_and_fails()
    {
        var pc = new Pc();
        var system = pc.Build_();
        var rig = Rig((_, _) => throw new HttpRequestException("name not resolved"));

        Assert.Null(await rig.Downloader.DownloadAsync(system, CancellationToken.None));
        Assert.Contains(system.Logs, l => l.Contains("name not resolved", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        var pc = new Pc();
        var rig = Rig((_, _) => Full(Payload));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Downloader.DownloadAsync(pc.Build_(), cts.Token));
    }
}
