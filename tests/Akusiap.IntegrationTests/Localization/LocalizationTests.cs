using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Akusiap.IntegrationTests.Localization;

public sealed class LocalizationTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string IndonesianHeading = "Latihan soal ujian, kapan saja dari ponsel Anda";
    private const string EnglishHeading = "Practise exam questions anytime from your phone";

    [Fact]
    public async Task Default_ui_is_indonesian_even_when_browser_prefers_english()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains(IndonesianHeading, html, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"id\">", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switching_to_english_persists_across_requests()
    {
        using var client = factory.CreateClient();

        var switched = await client.GetStringAsync(new Uri("/bahasa/en?returnUrl=%2F", UriKind.Relative));
        var nextRequest = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains(EnglishHeading, switched, StringComparison.Ordinal);
        Assert.Contains(EnglishHeading, nextRequest, StringComparison.Ordinal);
        Assert.Contains("<html lang=\"en\">", nextRequest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Switching_back_to_indonesian_works()
    {
        using var client = factory.CreateClient();

        await client.GetStringAsync(new Uri("/bahasa/en", UriKind.Relative));
        var html = await client.GetStringAsync(new Uri("/bahasa/id-ID", UriKind.Relative));

        Assert.Contains(IndonesianHeading, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_culture_is_ignored()
    {
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(new Uri("/bahasa/fr", UriKind.Relative));

        Assert.Contains(IndonesianHeading, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task External_return_url_is_rejected()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri("/bahasa/en?returnUrl=https%3A%2F%2Fevil.example", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }
}
