using System.Globalization;
using System.Text.Json;

namespace WinFix.Media;

/// <summary>
/// Asks the software-download-connector API behind microsoft.com/software-download for an ISO link: session, then the
/// language SKU of the Windows edition, then the links of that SKU. Undocumented and unverified against the live service;
/// any unexpected status or shape is logged and gives no link.
/// </summary>
public sealed class MicrosoftIsoSource(HttpClient http, Action<string> log) : IIsoSource
{
    private const string Profile = "606624d44113";
    private const string OrgId = "y6jn8c31";
    private const string Api = "https://www.microsoft.com/software-download-connector/api/";

    private static readonly Dictionary<string, int> DownloadTypes = new(StringComparer.OrdinalIgnoreCase) { ["x86"] = 0, ["x64"] = 1, ["arm64"] = 2 };

    // Microsoft's own names where they differ from the .NET English name.
    private static readonly Dictionary<string, string> LanguageAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en-GB"] = "English International",
        ["pt-BR"] = "Brazilian Portuguese",
        ["fr-CA"] = "French Canadian",
        ["zh-CN"] = "Chinese Simplified",
        ["zh-TW"] = "Chinese Traditional",
        ["es-MX"] = "Spanish (Mexico)",
    };

    /// <summary>Only https on microsoft.com or a subdomain; anything else is never downloaded.</summary>
    public static bool IsTrustedUrl(Uri url) =>
        url.IsAbsoluteUri
        && url.Scheme == Uri.UriSchemeHttps
        && (url.IdnHost.Equals("microsoft.com", StringComparison.OrdinalIgnoreCase)
            || url.IdnHost.EndsWith(".microsoft.com", StringComparison.OrdinalIgnoreCase));

    public async Task<IsoLink?> GetLinkAsync(IsoRequest request, CancellationToken ct)
    {
        try
        {
            return await LookUpAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log($"Microsoft ISO lookup failed: {ex.Message}");
            return null;
        }
    }

    private async Task<IsoLink?> LookUpAsync(IsoRequest request, CancellationToken ct)
    {
        var session = Guid.NewGuid();
        var locale = Uri.EscapeDataString(request.Language);
        var page = request.WindowsVersion == "11" ? "windows11" : "windows10ISO";
        var referer = $"https://www.microsoft.com/software-download/{page}";
        var edition = request.WindowsVersion == "11" ? 3813 : 2618;

        using (await http.GetAsync($"https://vlscppe.microsoft.com/tags?org_id={OrgId}&session_id={session}", ct))
        {
        }

        var skus = await GetJsonAsync(
            $"{Api}getskuinformationbyproductedition?profile={Profile}&productEditionId={edition}&SKU=undefined&friendlyFileName=undefined&Locale={locale}&sessionID={session}",
            referer, ct);
        if (skus is null || ApiError(skus.Value) || !TryFindSku(skus.Value, request.Language, out var sku))
        {
            return null;
        }

        var links = await GetJsonAsync(
            $"{Api}GetProductDownloadLinksBySku?profile={Profile}&productEditionId=undefined&SKU={Uri.EscapeDataString(sku)}&friendlyFileName=undefined&Locale={locale}&sessionID={session}",
            referer, ct);
        if (links is null || ApiError(links.Value))
        {
            return null;
        }

        return TryFindLink(links.Value, request.Architecture, out var url) ? new IsoLink(url, null) : null;
    }

    private async Task<JsonElement?> GetJsonAsync(string url, string referer, CancellationToken ct)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.Referrer = new Uri(referer);
        using var response = await http.SendAsync(message, ct);
        if (!response.IsSuccessStatusCode)
        {
            log($"Microsoft ISO lookup: the server answered {(int)response.StatusCode} {response.StatusCode}.");
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document.RootElement.Clone();
            }
        }
        catch (JsonException)
        {
        }

        log("Microsoft ISO lookup: the answer was not the expected JSON object.");
        return null;
    }

    private bool ApiError(JsonElement root)
    {
        if (!root.TryGetProperty("Errors", out var errors) || errors.ValueKind != JsonValueKind.Array || errors.GetArrayLength() == 0)
        {
            return false;
        }

        var first = errors[0];
        var message = first.ValueKind == JsonValueKind.Object && first.TryGetProperty("Value", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : "no details";
        log($"Microsoft ISO lookup: the server refused the request ({message}).");
        return true;
    }

    private bool TryFindSku(JsonElement root, string language, out string sku)
    {
        sku = string.Empty;
        if (!root.TryGetProperty("Skus", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            log("Microsoft ISO lookup: the answer holds no list of languages.");
            return false;
        }

        var entries = list.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.Object && Text(e, "Id") is not null)
            .Select(e => (Id: Text(e, "Id")!, Names: new[] { Text(e, "Language"), Text(e, "LocalizedLanguage") }))
            .ToList();
        foreach (var name in LanguageNames(language))
        {
            var match = entries.FirstOrDefault(e => e.Names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)));
            if (match.Id is not null)
            {
                sku = match.Id;
                return true;
            }
        }

        log($"Microsoft ISO lookup: no download is offered in the language {language}.");
        return false;
    }

    private bool TryFindLink(JsonElement root, string architecture, out Uri url)
    {
        url = null!;
        if (!root.TryGetProperty("ProductDownloadOptions", out var options) || options.ValueKind != JsonValueKind.Array
            || !DownloadTypes.TryGetValue(architecture, out var type))
        {
            log($"Microsoft ISO lookup: the answer holds no download list for {architecture}.");
            return false;
        }

        var address = options.EnumerateArray()
            .Where(o => o.ValueKind == JsonValueKind.Object
                && o.TryGetProperty("DownloadType", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var n) && n == type)
            .Select(o => Text(o, "Uri"))
            .FirstOrDefault(u => u is not null);
        if (address is null)
        {
            log($"Microsoft ISO lookup: no download is offered for {architecture}.");
            return false;
        }

        if (!Uri.TryCreate(address, UriKind.Absolute, out var parsed) || !IsTrustedUrl(parsed))
        {
            log("Microsoft ISO lookup: the download address is not https on a Microsoft host, so it was rejected.");
            return false;
        }

        url = parsed;
        return true;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IEnumerable<string> LanguageNames(string language)
    {
        var culture = TryCulture(language);
        if (culture is null)
        {
            return [language];
        }

        var names = new List<string> { culture.EnglishName };
        if (LanguageAliases.TryGetValue(language, out var alias))
        {
            names.Add(alias);
        }

        if (culture.Parent.EnglishName.Length > 0)
        {
            names.Add(culture.Parent.EnglishName);
        }

        return names;
    }

    private static CultureInfo? TryCulture(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
