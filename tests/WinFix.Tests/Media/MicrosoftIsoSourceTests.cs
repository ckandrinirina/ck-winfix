using System.Net;
using System.Text;
using WinFix.Media;

namespace WinFix.Tests.Media;

public class MicrosoftIsoSourceTests
{
    private const string SkuJson = """
        {"Errors":[],"Skus":[
          {"Id":"111","Language":"Arabic","LocalizedLanguage":"العربية"},
          {"Id":"222","Language":"English","LocalizedLanguage":"English (United States)"},
          {"Id":"333","Language":"English International","LocalizedLanguage":"English (United Kingdom)"},
          {"Id":"444","Language":"French","LocalizedLanguage":"Français"}
        ]}
        """;

    private const string LinksJson = """
        {"Errors":[],"ProductDownloadOptions":[
          {"Uri":"https://software.download.prss.microsoft.com/dbazure/Win11_en-US_x86.iso?t=1","DownloadType":0,"Language":"English"},
          {"Uri":"https://software.download.prss.microsoft.com/dbazure/Win11_en-US_x64.iso?t=1","DownloadType":1,"Language":"English"},
          {"Uri":"https://software.download.prss.microsoft.com/dbazure/Win11_en-US_arm64.iso?t=1","DownloadType":2,"Language":"English"}
        ]}
        """;

    private sealed class Api
    {
        public string Skus { get; set; } = SkuJson;

        public string Links { get; set; } = LinksJson;

        public HttpStatusCode SkuStatus { get; set; } = HttpStatusCode.OK;

        public List<Uri> Calls { get; } = [];

        public HttpClient Client() => new(new Handler(this));

        private sealed class Handler(Api api) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var url = request.RequestUri!;
                api.Calls.Add(url);
                var (status, body) = url.AbsolutePath switch
                {
                    "/tags" => (HttpStatusCode.OK, ""),
                    var p when p.EndsWith("getskuinformationbyproductedition", StringComparison.OrdinalIgnoreCase) => (api.SkuStatus, api.Skus),
                    var p when p.EndsWith("GetProductDownloadLinksBySku", StringComparison.OrdinalIgnoreCase) => (HttpStatusCode.OK, api.Links),
                    _ => (HttpStatusCode.NotFound, ""),
                };
                return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
            }
        }
    }

    private static async Task<(IsoLink? Link, List<string> Logs)> Ask(Api api, IsoRequest request)
    {
        var logs = new List<string>();
        var link = await new MicrosoftIsoSource(api.Client(), logs.Add).GetLinkAsync(request, CancellationToken.None);
        return (link, logs);
    }

    [Fact]
    public async Task Returns_the_x64_link_for_the_language()
    {
        var api = new Api();

        var (link, _) = await Ask(api, new IsoRequest("11", "x64", "en-US"));

        Assert.NotNull(link);
        Assert.Equal("https://software.download.prss.microsoft.com/dbazure/Win11_en-US_x64.iso?t=1", link.Url.ToString());
        Assert.Null(link.Sha256);
    }

    [Fact]
    public async Task Maps_each_architecture_to_its_download_type()
    {
        var x86 = await Ask(new Api(), new IsoRequest("11", "x86", "en-US"));
        var arm = await Ask(new Api(), new IsoRequest("11", "arm64", "en-US"));

        Assert.Contains("x86", x86.Link!.Url.ToString(), StringComparison.Ordinal);
        Assert.Contains("arm64", arm.Link!.Url.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Walks_session_then_sku_then_links_with_the_edition_for_the_version()
    {
        var api = new Api();

        await Ask(api, new IsoRequest("10", "x64", "en-US"));

        Assert.Equal(["/tags", "/software-download-connector/api/getskuinformationbyproductedition", "/software-download-connector/api/GetProductDownloadLinksBySku"],
            api.Calls.Select(c => c.AbsolutePath));
        Assert.All(api.Calls, c => Assert.Equal("https", c.Scheme));
        Assert.All(api.Calls, c => Assert.EndsWith(".microsoft.com", c.Host, StringComparison.Ordinal));
        var session = global::System.Web.HttpUtility.ParseQueryString(api.Calls[0].Query)["session_id"];
        Assert.True(Guid.TryParse(session, out _));
        Assert.Contains("sessionID=" + session, api.Calls[2].Query, StringComparison.Ordinal);
        Assert.Contains("productEditionId=2618", api.Calls[1].Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SKU=222", api.Calls[2].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Windows_11_asks_for_the_Windows_11_edition()
    {
        var api = new Api();

        await Ask(api, new IsoRequest("11", "x64", "en-US"));

        Assert.Contains("productEditionId=3813", api.Calls[1].Query, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("fr-FR", "444")]
    [InlineData("en-GB", "333")]
    [InlineData("en-US", "222")]
    public async Task Picks_the_sku_of_the_installed_language(string language, string sku)
    {
        var api = new Api();

        await Ask(api, new IsoRequest("11", "x64", language));

        Assert.Contains("SKU=" + sku, api.Calls[2].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_language_Microsoft_does_not_offer_is_a_logged_failure()
    {
        var (link, logs) = await Ask(new Api(), new IsoRequest("11", "x64", "ja-JP"));

        Assert.Null(link);
        Assert.Contains(logs, l => l.Contains("ja-JP", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_api_error_is_a_logged_failure()
    {
        var api = new Api { Skus = """{"Errors":[{"Type":9,"Value":"blocked 715-123130"}]}""" };

        var (link, logs) = await Ask(api, new IsoRequest("11", "x64", "en-US"));

        Assert.Null(link);
        Assert.Contains(logs, l => l.Contains("715-123130", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"Skus":"x"}""")]
    [InlineData("""{"Skus":[{"Id":5}]}""")]
    public async Task An_unexpected_sku_shape_is_a_logged_failure(string body)
    {
        var (link, logs) = await Ask(new Api { Skus = body }, new IsoRequest("11", "x64", "en-US"));

        Assert.Null(link);
        Assert.NotEmpty(logs);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"ProductDownloadOptions":[]}""")]
    [InlineData("""{"ProductDownloadOptions":[{"Uri":"https://software.download.prss.microsoft.com/a.iso","DownloadType":"1"}]}""")]
    [InlineData("""{"ProductDownloadOptions":[{"DownloadType":1}]}""")]
    public async Task An_unexpected_links_shape_is_a_logged_failure(string body)
    {
        var (link, logs) = await Ask(new Api { Links = body }, new IsoRequest("11", "x64", "en-US"));

        Assert.Null(link);
        Assert.NotEmpty(logs);
    }

    [Theory]
    [InlineData("http://software.download.prss.microsoft.com/a.iso")]
    [InlineData("https://evil.example.com/a.iso")]
    [InlineData("https://microsoft.com.evil.example/a.iso")]
    [InlineData("not a url")]
    public async Task A_download_url_outside_Microsoft_https_is_rejected_and_logged(string url)
    {
        var api = new Api { Links = $$"""{"ProductDownloadOptions":[{"Uri":"{{url}}","DownloadType":1}]}""" };

        var (link, logs) = await Ask(api, new IsoRequest("11", "x64", "en-US"));

        Assert.Null(link);
        Assert.Contains(logs, l => l.Contains("Microsoft", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_http_error_is_a_logged_failure()
    {
        var (link, logs) = await Ask(new Api { SkuStatus = HttpStatusCode.Forbidden }, new IsoRequest("11", "x64", "en-US"));

        Assert.Null(link);
        Assert.Contains(logs, l => l.Contains("403", StringComparison.Ordinal) || l.Contains("Forbidden", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_network_failure_is_a_logged_failure()
    {
        var logs = new List<string>();
        var http = new HttpClient(new Throwing());

        var link = await new MicrosoftIsoSource(http, logs.Add).GetLinkAsync(new IsoRequest("11", "x64", "en-US"), CancellationToken.None);

        Assert.Null(link);
        Assert.Contains(logs, l => l.Contains("offline", StringComparison.Ordinal));
    }

    private sealed class Throwing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("offline");
    }
}
