using System.Net;
using System.Security.Cryptography;
using WinFix.Media;

namespace WinFixUsb.Tests.Usb;

/// <summary>Disk in memory; a stream stays readable after the downloader closes it.</summary>
internal sealed class MemoryFiles : IDownloadFiles
{
    private sealed class Kept : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
        }
    }

    public Dictionary<string, MemoryStream> Data { get; } = new(StringComparer.Ordinal);

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

/// <summary>Answers every request from a function and records the URLs; nothing leaves the process.</summary>
internal sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    public Action<HttpRequestMessage>? OnRequest { get; init; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request.RequestUri!);
        OnRequest?.Invoke(request);
        var response = respond(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }

    public static HttpResponseMessage Bytes(byte[] data) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };

    public static HttpResponseMessage Status(HttpStatusCode code) => new(code);
}

internal sealed class FakeSource(IsoLink? link, Exception? failure = null) : IIsoSource
{
    public List<IsoRequest> Requests { get; } = [];

    public Task<IsoLink?> GetLinkAsync(IsoRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return failure is null ? Task.FromResult(link) : Task.FromException<IsoLink?>(failure);
    }
}
