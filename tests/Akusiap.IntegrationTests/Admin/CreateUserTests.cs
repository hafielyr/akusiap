using System.Net;
using System.Text.RegularExpressions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Akusiap.IntegrationTests.Admin;

/// <summary>USR-02: an admin creates an account; the user sets a password from the emailed link and signs in.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class CreateUserTests(PostgresFixture database)
{
    [Fact]
    public async Task The_new_user_sets_a_password_from_the_link_and_signs_in()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var email = $"guru-{TestData.Unique()}@example.com";

        using var created = await admin.PostFormAsync("/admin/pengguna/baru", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.DisplayName"] = "Bu Guru",
            ["Input.PreferredLanguage"] = "id",
            ["Input.Roles"] = RoleNames.Teacher,
        });

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.StartsWith("/admin/pengguna/", created.Headers.Location!.OriginalString, StringComparison.Ordinal);
        var message = Assert.Single(app.Email.To(email));
        Assert.Equal("Akun Akusiap Anda sudah dibuat", message.Subject);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await users.FindByEmailAsync(email);
            Assert.False(await users.HasPasswordAsync(user!));
            Assert.Equal([RoleNames.Teacher], await users.GetRolesAsync(user!));
        }

        using var newcomer = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var link = new Uri(LinkPattern().Match(message.TextBody!).Value).PathAndQuery;
        var code = Uri.UnescapeDataString(Regex.Match(link, "[?&]code=([^&]+)").Groups[1].Value);
        using var set = await newcomer.PostFormAsync(link, new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Code"] = code,
            ["Input.Password"] = "kapur-tulis-2026",
        }, "/akun/atur-ulang-sandi");
        using var signIn = await newcomer.PostFormAsync("/akun/masuk", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = "kapur-tulis-2026",
        });

        Assert.Equal(HttpStatusCode.Redirect, set.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
    }

    [Fact]
    public async Task Duplicate_emails_and_missing_roles_are_refused_and_the_invite_can_be_resent()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var existing = await app.CreateUserAsync(TestData.User().Build(), "kopi-susu-2026", RoleNames.Learner);

        using var duplicate = await admin.PostFormAsync("/admin/pengguna/baru", new Dictionary<string, string>
        {
            ["Input.Email"] = existing.Email!.ToUpperInvariant(),
            ["Input.DisplayName"] = "Lagi",
            ["Input.Roles"] = RoleNames.Learner,
        });
        using var noRole = await admin.PostFormAsync("/admin/pengguna/baru", new Dictionary<string, string>
        {
            ["Input.Email"] = $"baru-{TestData.Unique()}@example.com",
            ["Input.DisplayName"] = "Tanpa Peran",
        });
        using var resent = await admin.PostFormAsync($"/admin/pengguna/{existing.Id}", new Dictionary<string, string>(), $"/admin/pengguna/{existing.Id}?handler=Invite");

        Assert.Contains("sudah terdaftar", WebUtility.HtmlDecode(await duplicate.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Contains("Pilih minimal satu peran.", WebUtility.HtmlDecode(await noRole.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, resent.StatusCode);
        Assert.Single(app.Email.To(existing.Email!));
    }

    [GeneratedRegex("https?://\\S+")]
    private static partial Regex LinkPattern();
}
