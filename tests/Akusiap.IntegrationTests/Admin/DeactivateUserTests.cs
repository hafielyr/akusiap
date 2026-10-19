using System.Net;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Admin;

/// <summary>USR-03: deactivating signs the user out on their next request and blocks sign-in; reactivating undoes it.</summary>
[Collection(UsesDatabase.Name)]
public sealed class DeactivateUserTests(PostgresFixture database)
{
    private const string Password = "test-sandi-2026"; // CreateSignedInClientAsync's password

    [Fact]
    public async Task A_deactivated_user_is_signed_out_at_once_cannot_sign_in_and_keeps_their_records()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        using var before = await learner.GetAsync(new Uri("/learn", UriKind.Relative));

        using var deactivated = await admin.PostFormAsync($"/admin/pengguna/{user.Id}", new Dictionary<string, string> { ["active"] = "false" }, $"/admin/pengguna/{user.Id}?handler=Active");
        using var after = await learner.GetAsync(new Uri("/learn", UriKind.Relative));
        using var fresh = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var signIn = await fresh.PostFormAsync("/akun/masuk", SignIn(user.Email!));

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, deactivated.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.Contains("/akun/masuk", after.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.Contains("Akun dinonaktifkan", WebUtility.HtmlDecode(await signIn.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        await using var db = database.CreateContext();
        var saved = await db.Users.SingleAsync(u => u.Id == user.Id);
        Assert.False(saved.IsActive);
        Assert.Equal(app.Clock.UtcNow, saved.DeactivatedAt);
        Assert.Equal(user.Email, saved.Email);
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "User.Deactivate" && a.EntityId == user.Id.ToString()));

        using var reactivated = await admin.PostFormAsync($"/admin/pengguna/{user.Id}", new Dictionary<string, string> { ["active"] = "true" }, $"/admin/pengguna/{user.Id}?handler=Active");
        using var again = await fresh.PostFormAsync("/akun/masuk", SignIn(user.Email!));

        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
        await using var check = database.CreateContext();
        Assert.Null((await check.Users.SingleAsync(u => u.Id == user.Id)).DeactivatedAt);
    }

    [Fact]
    public async Task Admins_cannot_deactivate_themselves()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var adminUser = TestData.User().Build();
        using var admin = await app.CreateSignedInClientAsync(adminUser, RoleNames.Admin);

        using (await admin.PostFormAsync($"/admin/pengguna/{adminUser.Id}", new Dictionary<string, string> { ["active"] = "false" }, $"/admin/pengguna/{adminUser.Id}?handler=Active"))
        {
        }

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync(new Uri($"/admin/pengguna/{adminUser.Id}", UriKind.Relative)));
        Assert.Contains("Anda tidak bisa menonaktifkan akun Anda sendiri.", page, StringComparison.Ordinal);
        await using var db = database.CreateContext();
        Assert.True((await db.Users.SingleAsync(u => u.Id == adminUser.Id)).IsActive);
    }

    private static Dictionary<string, string> SignIn(string email) => new()
    {
        ["Input.Email"] = email,
        ["Input.Password"] = Password,
    };
}
