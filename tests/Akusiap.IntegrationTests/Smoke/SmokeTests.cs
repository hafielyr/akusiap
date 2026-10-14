using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Akusiap.IntegrationTests.Smoke;

public sealed class SmokeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory = factory.WithWebHostBuilder(builder =>
        builder.UseSetting("Support:WhatsAppNumber", "6281200000000"));

    [Fact]
    public async Task Health_returns_200()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Home_page_is_mobile_ready_and_has_whatsapp_support()
    {
        using var client = _factory.CreateClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("name=\"viewport\"", html, StringComparison.Ordinal);
        Assert.Contains("lang=\"id\"", html, StringComparison.Ordinal);
        Assert.Contains("https://wa.me/6281200000000", html, StringComparison.Ordinal);
    }
}
