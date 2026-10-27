using System.Net;
using WinFix.Media;
using WinFixUsb.Usb;

namespace WinFixUsb.Tests.Usb;

public class IsoProviderTests
{
    private const string Downloads = "/Users/me/Downloads";
    private static readonly Uri IsoUrl = new("https://software.download.prss.microsoft.com/dbazure/Win11_English_x64.iso");
    private static readonly byte[] Payload = Enumerable.Range(0, 3000).Select(i => (byte)(i % 251)).ToArray();

    private static (IsoProvider Provider, FakeHandler Handler, FakeSource Source, MemoryFiles Files, StringWriter Output) Rig(
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null, IsoLink? link = null, Exception? failure = null, string language = "en-US")
    {
        var handler = new FakeHandler(respond ?? (_ => FakeHandler.Bytes(Payload)));
        var source = new FakeSource(failure is null ? link ?? new IsoLink(IsoUrl, null) : null, failure);
        var files = new MemoryFiles();
        var provider = new IsoProvider(source, new HttpClient(handler), files, Downloads, language, (_, _) => Task.CompletedTask);
        return (provider, handler, source, files, new StringWriter());
    }

    [Fact]
    public async Task Downloads_the_Windows_11_x64_ISO_into_the_downloads_folder_and_returns_its_path()
    {
        var rig = Rig();

        var path = await rig.Provider.DownloadAsync(rig.Output, CancellationToken.None);

        Assert.Equal($"{Downloads}/Windows11_x64_en-US.iso", path);
        Assert.Equal(Payload, rig.Files.Data[path!].ToArray());
        Assert.Equal(new IsoRequest("11", "x64", "en-US"), Assert.Single(rig.Source.Requests));
        Assert.Equal(IsoUrl, Assert.Single(rig.Handler.Requests));
    }

    [Fact]
    public async Task The_language_goes_to_the_source_and_into_the_file_name()
    {
        var rig = Rig(language: "fr-FR");

        var path = await rig.Provider.DownloadAsync(rig.Output, CancellationToken.None);

        Assert.Equal($"{Downloads}/Windows11_x64_fr-FR.iso", path);
        Assert.Equal("fr-FR", Assert.Single(rig.Source.Requests).Language);
    }

    [Fact]
    public async Task A_finished_download_leaves_no_part_file()
    {
        var rig = Rig();

        await rig.Provider.DownloadAsync(rig.Output, CancellationToken.None);

        Assert.DoesNotContain(rig.Files.Data.Keys, k => k.EndsWith(".part", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_link_gives_null_and_a_plain_message_with_the_manual_page()
    {
        var rig = Rig(link: null, failure: new InvalidOperationException("no link"));

        var path = await rig.Provider.DownloadAsync(rig.Output, CancellationToken.None);

        Assert.Null(path);
        Assert.Contains(IsoProvider.ManualPage, rig.Output.ToString());
        Assert.Contains("Nothing was erased.", rig.Output.ToString());
        Assert.Empty(rig.Files.Data);
    }

    [Fact]
    public async Task A_server_error_gives_null_and_the_manual_page()
    {
        var rig = Rig(_ => FakeHandler.Status(HttpStatusCode.InternalServerError));

        var path = await rig.Provider.DownloadAsync(rig.Output, CancellationToken.None);

        Assert.Null(path);
        Assert.Contains(IsoProvider.ManualPage, rig.Output.ToString());
    }

    [Fact]
    public async Task A_link_that_is_not_a_Microsoft_host_is_never_downloaded()
    {
        var rig = Rig(link: new IsoLink(new Uri("https://evil.example.com/Win11.iso"), null));

        var path = await rig.Provider.DownloadAsync(rig.Output, CancellationToken.None);

        Assert.Null(path);
        Assert.Empty(rig.Handler.Requests);
        Assert.Contains(IsoProvider.ManualPage, rig.Output.ToString());
    }

    [Fact]
    public void The_manual_page_is_on_microsoft_com()
    {
        Assert.True(MicrosoftIsoSource.IsTrustedUrl(new Uri(IsoProvider.ManualPage)));
    }
}
