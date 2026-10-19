using System.Net;
using System.Text.Json;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;

namespace Akusiap.IntegrationTests.Infrastructure;

[Collection(UsesDatabase.Name)]
public sealed class DatabaseStartupTests(PostgresFixture database)
{
    [Fact]
    public async Task Health_reports_database_healthy_when_connection_string_is_set()
    {
        await using var app = new AppFactory(database);
        using var client = app.CreateClient();

        using var response = await client.GetAsync(new Uri("/health", UriKind.Relative));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("Healthy", json.RootElement.GetProperty("checks").GetProperty("database").GetString());
    }

    [Fact]
    public async Task Startup_fails_with_a_message_naming_the_missing_setting()
    {
        await using var app = new AppFactory(connectionString: string.Empty);

        var error = Assert.Throws<InvalidOperationException>(() => app.CreateClient());

        Assert.Contains("ConnectionStrings:Default", error.Message, StringComparison.Ordinal);
        Assert.Contains("dotnet user-secrets set", error.Message, StringComparison.Ordinal);
    }
}
