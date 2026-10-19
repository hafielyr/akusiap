using System.Net;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;

namespace Akusiap.IntegrationTests.Smoke;

[Collection(UsesDatabase.Name)]
public sealed class SmokeTests(PostgresFixture database)
{
    [Fact]
    public async Task Health_returns_200()
    {
        await using var app = new AppFactory(database);
        using var client = app.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Home_page_is_mobile_ready_and_has_whatsapp_support()
    {
        await using var app = new AppFactory(database);
        using var client = app.CreateClient();

        var html = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("name=\"viewport\"", html, StringComparison.Ordinal);
        Assert.Contains("lang=\"id\"", html, StringComparison.Ordinal);
        Assert.Contains($"https://wa.me/{AppFactory.SupportWhatsAppNumber}", html, StringComparison.Ordinal);
    }
}
