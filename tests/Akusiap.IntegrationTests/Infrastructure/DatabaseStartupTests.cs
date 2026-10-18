using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Akusiap.IntegrationTests.Infrastructure;

public sealed class DatabaseStartupTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    // Uses ConnectionStrings:Default from user-secrets (dev) or env vars (CI); see D-15.
    [Fact]
    public async Task Health_reports_database_healthy_when_connection_string_is_set()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("Healthy", json.RootElement.GetProperty("checks").GetProperty("database").GetString());
    }

    [Fact]
    public void Startup_fails_with_a_message_naming_the_missing_setting()
    {
        using var withoutDatabase = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:Default", string.Empty));

        var error = Assert.Throws<InvalidOperationException>(() => withoutDatabase.CreateClient());

        Assert.Contains("ConnectionStrings:Default", error.Message, StringComparison.Ordinal);
        Assert.Contains("dotnet user-secrets set", error.Message, StringComparison.Ordinal);
    }
}
