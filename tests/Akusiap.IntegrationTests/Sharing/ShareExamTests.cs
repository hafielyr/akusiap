using System.Net;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Sharing;

/// <summary>SHR-01: the owner shares, stops and resets the link (behind Features:Sharing); only all-own exams can be shared.</summary>
[Collection(UsesDatabase.Name)]
public sealed class ShareExamTests(PostgresFixture database)
{
    [Fact]
    public async Task A_reset_link_invalidates_the_old_one()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        await using var site = app.WithWebHostBuilder(b => b.UseSetting("Features:Sharing", "true"));
        var owner = TestData.User().Build();
        using var client = await site.CreateSignedInClientAsync(owner, RoleNames.Learner);
        var examId = await SeedOwnExamAsync(owner);
        var page = $"/learn/ujian-saya/{examId}";

        using (await client.PostFormAsync(page, new Dictionary<string, string>(), $"{page}?handler=Share"))
        {
        }

        var first = await LoadAsync(examId);
        var html = await client.GetStringAsync(new Uri(page, UriKind.Relative));
        using (await client.PostFormAsync(page, new Dictionary<string, string>(), $"{page}?handler=ResetShare"))
        {
        }

        var second = await LoadAsync(examId);
        using (await client.PostFormAsync(page, new Dictionary<string, string>(), $"{page}?handler=StopShare"))
        {
        }

        var stopped = await LoadAsync(examId);

        Assert.True(first.IsShared);
        Assert.Matches("^[A-Za-z0-9_-]{22}$", first.ShareToken);
        Assert.Contains($"/s/{first.ShareToken}", html, StringComparison.Ordinal);
        Assert.True(second.IsShared);
        Assert.NotEqual(first.ShareToken, second.ShareToken);
        Assert.Equal(AccessLevel.None, Decide(second, first.ShareToken).Level);
        Assert.Equal(AccessLevel.Shared, Decide(second, second.ShareToken).Level);
        Assert.False(stopped.IsShared);
        Assert.Equal(AccessLevel.None, Decide(stopped, second.ShareToken).Level);
    }

    [Fact]
    public async Task An_exam_with_a_question_the_owner_does_not_own_cannot_be_shared()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        await using var site = app.WithWebHostBuilder(b => b.UseSetting("Features:Sharing", "true"));
        var owner = TestData.User().Build();
        using var client = await site.CreateSignedInClientAsync(owner, RoleNames.Learner);
        var examId = await SeedOwnExamAsync(owner);
        await using (var db = database.CreateContext())
        {
            var foreign = await db.SaveAsync(TestData.Exam().Free().WithQuestions(1).Build());
            db.ExamQuestions.Add(new ExamQuestion { ExamId = examId, QuestionId = foreign.Questions.Single().QuestionId, SortOrder = 99 });
            await db.SaveChangesAsync();
        }

        var page = $"/learn/ujian-saya/{examId}";
        var html = await client.GetStringAsync(new Uri(page, UriKind.Relative));
        using var share = await client.PostFormAsync(page, new Dictionary<string, string>(), $"{page}?handler=Share");

        Assert.Contains("data-testid=\"share-blocked\"", html, StringComparison.Ordinal);
        Assert.False((await LoadAsync(examId)).IsShared);
    }

    [Fact]
    public async Task With_the_flag_off_there_is_no_sharing()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var owner = TestData.User().Build();
        using var client = await app.CreateSignedInClientAsync(owner, RoleNames.Learner);
        var examId = await SeedOwnExamAsync(owner);
        var page = $"/learn/ujian-saya/{examId}";

        var html = await client.GetStringAsync(new Uri(page, UriKind.Relative));
        using var share = await client.PostFormAsync(page, new Dictionary<string, string>(), $"{page}?handler=Share");

        Assert.DoesNotContain("data-testid=\"share-panel\"", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, share.StatusCode);
        Assert.False((await LoadAsync(examId)).IsShared);
    }

    private static AccessDecision Decide(Exam exam, string? token) =>
        EntitlementPolicy.Evaluate(
            ViewerContext.Guest,
            new ExamAccessFacts(exam.Id, exam.OwnerUserId, exam.OwnerKind, exam.Status, exam.IsFree, exam.IsShared, exam.ShareToken, false),
            token,
            TestData.Now);

    private async Task<long> SeedOwnExamAsync(User owner)
    {
        await using var db = database.CreateContext();
        return (await db.SaveAsync(TestData.Exam().OwnedBy(owner).WithQuestions(2).Build())).Id;
    }

    private async Task<Exam> LoadAsync(long examId)
    {
        await using var db = database.CreateContext();
        return await db.Exams.AsNoTracking().SingleAsync(e => e.Id == examId);
    }
}
