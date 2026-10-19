using System.Net;
using Akusiap.Domain.Access;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Admin;

/// <summary>OWN-01: plan caps. Plans are shared reference data, so each test gets its own database.</summary>
public sealed class PlanLimitsTests : IAsyncLifetime
{
    private const string Page = "/admin/paket/batas";
    private TestDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestDatabase.CreateMigratedAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Admin_sets_caps_and_the_next_request_sees_them()
    {
        await using var app = new AppFactory(_database.ConnectionString);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var learner = await PlanAsync(PlanCode.Learner);

        var before = await admin.GetStringAsync(new Uri(Page, UriKind.Relative));
        using var saved = await admin.PostFormAsync(Page, Form(learner.Id, "5000", "100", ""));
        var after = WebUtility.HtmlDecode(await admin.GetStringAsync(new Uri(Page, UriKind.Relative)));

        Assert.Contains("value=\"50\"", before, StringComparison.Ordinal); // Free plan: 50 questions, 1 exam
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains("Batas paket disimpan.", after, StringComparison.Ordinal);
        Assert.Contains("value=\"5000\"", after, StringComparison.Ordinal);
        var plan = await PlanAsync(PlanCode.Learner);
        Assert.Equal((5000, 100, (int?)null), (plan.MaxOwnQuestions!.Value, plan.MaxOwnExams!.Value, plan.MaxImportRows));
        await using var db = _database.CreateContext();
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "Plan.Limits"));
    }

    [Fact]
    public async Task Bad_values_are_refused_and_nothing_changes()
    {
        await using var app = new AppFactory(_database.ConnectionString);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var free = await PlanAsync(PlanCode.Free);

        using var response = await admin.PostFormAsync(Page, Form(free.Id, "abc", "-1", "2000000"));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, html.Split("data-testid=\"limit-error\"").Length - 1);
        Assert.Contains("Maks. soal sendiri harus angka bulat 0 sampai 1000000, atau kosong.", html, StringComparison.Ordinal);
        Assert.Contains("value=\"abc\"", html, StringComparison.Ordinal);
        var plan = await PlanAsync(PlanCode.Free);
        Assert.Equal((50, 1, 50), (plan.MaxOwnQuestions!.Value, plan.MaxOwnExams!.Value, plan.MaxImportRows!.Value));
    }

    private static Dictionary<string, string> Form(long planId, string questions, string exams, string rows) => new()
    {
        ["planId"] = planId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["maxOwnQuestions"] = questions,
        ["maxOwnExams"] = exams,
        ["maxImportRows"] = rows,
    };

    private async Task<Plan> PlanAsync(PlanCode code)
    {
        await using var db = _database.CreateContext();
        return await db.Plans.AsNoTracking().SingleAsync(p => p.Code == code);
    }
}
