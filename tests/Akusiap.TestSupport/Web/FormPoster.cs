using System.Net;
using System.Text.RegularExpressions;

namespace Akusiap.TestSupport.Web;

/// <summary>
/// Submits Razor Pages forms like a browser: GETs the page for its antiforgery token, then POSTs the fields with the
/// same client (and so the same cookies). Use a client with <c>AllowAutoRedirect = false</c> to inspect redirects.
/// </summary>
public static partial class FormPoster
{
    public static async Task<HttpResponseMessage> PostFormAsync(
        this HttpClient client, string pageUrl, IReadOnlyDictionary<string, string> fields, string? postUrl = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(fields);
        var token = await GetAntiforgeryTokenAsync(client, pageUrl);
        var form = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token };
        using var content = new FormUrlEncodedContent(form);
        return await client.PostAsync(new Uri(postUrl ?? pageUrl, UriKind.Relative), content);
    }

    public static async Task<string> GetAntiforgeryTokenAsync(this HttpClient client, string pageUrl)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var response = await client.GetAsync(new Uri(pageUrl, UriKind.Relative));
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"GET {pageUrl} returned {(int)response.StatusCode}, so there is no form to read.");
        }

        var html = await response.Content.ReadAsStringAsync();
        var match = TokenInput().Match(html);
        return match.Success
            ? WebUtility.HtmlDecode(match.Groups["token"].Value)
            : throw new InvalidOperationException($"No antiforgery token on {pageUrl}.");
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex TokenInput();
}
