using System.Net;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Akusiap.IntegrationTests.Authorization;

/// <summary>AC-06: AdminOnly and SignedIn policies, and the Indonesian 403/404 pages.</summary>
[Collection(UsesDatabase.Name)]
public sealed class AuthorizationTests(PostgresFixture database)
{
    [Theory]
    [InlineData("/admin")]
    [InlineData("/learn")]
    public async Task Anonymous_user_is_redirected_to_sign_in_with_a_return_url(string path)
    {
        await using var app = await StartAsync();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/akun/masuk?ReturnUrl={Uri.EscapeDataString(path)}", response.Headers.Location?.PathAndQuery);
    }

    [Fact]
    public async Task Learner_gets_403_with_the_indonesian_access_denied_page_on_admin()
    {
        await using var app = await StartAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);

        using var response = await learner.GetAsync(new Uri("/admin", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("data-status=\"403\"", html, StringComparison.Ordinal);
        Assert.Contains("Anda tidak punya akses ke halaman ini.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_opens_the_admin_area()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);

        using var response = await admin.GetAsync(new Uri("/admin", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Dasbor admin", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Signed_in_learner_opens_the_learn_area()
    {
        await using var app = await StartAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);

        using var response = await learner.GetAsync(new Uri("/learn", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ruang belajar", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/ujian")]
    [InlineData("/akun/masuk")]
    [InlineData("/akun/daftar")]
    [InlineData("/health")]
    public async Task Public_pages_stay_open_to_guests(string path)
    {
        await using var app = await StartAsync();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_address_gets_404_with_the_indonesian_not_found_page()
    {
        await using var app = await StartAsync();
        using var client = app.CreateClient();

        using var response = await client.GetAsync(new Uri("/tidak-ada-halaman-ini", UriKind.Relative));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("data-status=\"404\"", html, StringComparison.Ordinal);
        Assert.Contains("Halaman tidak ditemukan.", html, StringComparison.Ordinal);
        Assert.Contains("lang=\"id\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Not_found_page_follows_the_chosen_language()
    {
        await using var app = await StartAsync();
        using var client = app.CreateClient();
        await client.GetAsync(new Uri("/bahasa/en", UriKind.Relative));

        using var response = await client.GetAsync(new Uri("/tidak-ada-halaman-ini", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Page not found.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_page_opened_directly_behaves_like_a_missing_page()
    {
        await using var app = await StartAsync();
        using var client = app.CreateClient();

        using var response = await client.GetAsync(new Uri("/status/403", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("data-status=\"404\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Posting_to_an_unknown_address_still_shows_the_not_found_page()
    {
        await using var app = await StartAsync();
        using var client = app.CreateClient();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["x"] = "1" });

        using var response = await client.PostAsync(new Uri("/tidak-ada-halaman-ini", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Halaman tidak ditemukan.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private async Task<AppFactory> StartAsync()
    {
        var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        return app;
    }
}
