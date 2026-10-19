using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Application.Access;
using Akusiap.Application.Common;
using Akusiap.Application.Sessions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.Infrastructure.Persistence;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Fakes;
using Akusiap.Web.Sessions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Sessions;

/// <summary>SES-01: starting an attempt for guests (trial) and users (full), with the rules and the stored order.</summary>
[Collection(UsesDatabase.Name)]
public sealed class StartAttemptTests(PostgresFixture database)
{
    [Fact]
    public async Task Guest_trial_attempt_contains_only_active_trial_questions()
    {
        var exam = await SeedExamAsync(TestData.Exam().Paid(49_000).WithQuestions(10, trial: 4), retireFirst: true);
        var token = Guid.NewGuid();

        var result = await StartAsync(FakeCurrentUser.Guest, new StartAttemptRequest(exam.Id, AttemptMode.Practice, GuestToken: token));

        Assert.True(result.Succeeded, Describe(result));
        Assert.Equal((AttemptSource.Trial, 3), (result.Value!.Source, result.Value.QuestionCount));
        var attempt = await LoadAsync(result.Value.AttemptId);
        Assert.Null(attempt.UserId);
        Assert.Equal(token, attempt.GuestToken);
        Assert.Equal(exam.TrialIds.Skip(1).Order(), attempt.Items.Select(i => i.QuestionId).Order());
        Assert.Equal(3, attempt.TotalCount);
    }

    [Fact]
    public async Task Purchase_holder_gets_every_active_question_and_a_rules_snapshot()
    {
        var exam = await SeedExamAsync(TestData.Exam().Paid(49_000).WithQuestions(10, trial: 4), retireFirst: true);
        var user = await BuyerAsync(exam.Id);

        var result = await StartAsync(new FakeCurrentUser(user.Id), new StartAttemptRequest(exam.Id, AttemptMode.Practice));

        Assert.True(result.Succeeded, Describe(result));
        var attempt = await LoadAsync(result.Value!.AttemptId);
        Assert.Equal((AttemptSource.Exam, user.Id, (Guid?)null), (attempt.Source, attempt.UserId!.Value, attempt.GuestToken));
        Assert.Equal(exam.AllIds.Skip(1).Order(), attempt.Items.Select(i => i.QuestionId).Order());
        Assert.Equal(Enumerable.Range(1, 9), attempt.Items.Select(i => i.Position).Order());
        using var rules = JsonDocument.Parse(attempt.RulesSnapshot);
        Assert.Equal("Practice", rules.RootElement.GetProperty("mode").GetString());
        Assert.Equal(9, rules.RootElement.GetProperty("questionCount").GetInt32());
        Assert.Equal(TestClock.DefaultStart, attempt.StartedAt);
    }

    [Fact]
    public async Task Two_attempts_have_different_orders_when_shuffle_is_on()
    {
        var exam = await SeedExamAsync(TestData.Exam().Free().WithQuestions(12));
        var user = await SaveUserAsync();
        var viewer = new FakeCurrentUser(user.Id);

        var first = await StartAsync(viewer, new StartAttemptRequest(exam.Id, AttemptMode.Practice), seed: 1);
        var second = await StartAsync(viewer, new StartAttemptRequest(exam.Id, AttemptMode.Practice), seed: 2);

        var firstOrder = await OrderAsync(first.Value!.AttemptId);
        var secondOrder = await OrderAsync(second.Value!.AttemptId);
        Assert.NotEqual(firstOrder, secondOrder);
        Assert.Equal(firstOrder.Order(), secondOrder.Order());
    }

    [Fact]
    public async Task Without_shuffling_questions_and_options_keep_exam_order()
    {
        var exam = await SeedExamAsync(TestData.Exam().Free().WithQuestions(5));
        var user = await SaveUserAsync();
        var overrides = new RuleOverrides(ShuffleQuestions: false, ShuffleOptions: false);

        var result = await StartAsync(new FakeCurrentUser(user.Id), new StartAttemptRequest(exam.Id, AttemptMode.Practice, overrides));

        Assert.Equal(exam.AllIds, await OrderAsync(result.Value!.AttemptId));
        var attempt = await LoadAsync(result.Value.AttemptId);
        await using var db = database.CreateContext();
        var item = attempt.Items.OrderBy(i => i.Position).First();
        var savedOrder = await db.QuestionOptions.Where(o => o.QuestionId == item.QuestionId).OrderBy(o => o.SortOrder).Select(o => o.Id).ToListAsync();
        Assert.Equal(savedOrder, JsonSerializer.Deserialize<List<long>>(item.OptionOrder!));
    }

    [Fact]
    public async Task Shuffled_options_keep_every_option_of_the_question()
    {
        var exam = await SeedExamAsync(TestData.Exam().Free().WithQuestions(6));
        var user = await SaveUserAsync();

        var result = await StartAsync(new FakeCurrentUser(user.Id), new StartAttemptRequest(exam.Id, AttemptMode.Practice));

        var attempt = await LoadAsync(result.Value!.AttemptId);
        await using var db = database.CreateContext();
        foreach (var item in attempt.Items)
        {
            var saved = await db.QuestionOptions.Where(o => o.QuestionId == item.QuestionId).Select(o => o.Id).OrderBy(id => id).ToListAsync();
            Assert.Equal(saved, JsonSerializer.Deserialize<List<long>>(item.OptionOrder!)!.Order());
        }
    }

    [Fact]
    public async Task Starting_without_access_is_rejected_and_saves_nothing()
    {
        var paidWithoutTrial = await SeedExamAsync(TestData.Exam().Paid(49_000).WithQuestions(3, trial: 0));
        var free = await SeedExamAsync(TestData.Exam().Free().WithQuestions(3));
        var draft = await SeedExamAsync(TestData.Exam().Free().Draft().WithQuestions(3));
        var user = await SaveUserAsync();
        var guestToken = Guid.NewGuid();

        var noTrial = await StartAsync(new FakeCurrentUser(user.Id), new StartAttemptRequest(paidWithoutTrial.Id, AttemptMode.Practice));
        var guestOnFree = await StartAsync(FakeCurrentUser.Guest, new StartAttemptRequest(free.Id, AttemptMode.Practice, GuestToken: guestToken));
        var notPublished = await StartAsync(new FakeCurrentUser(user.Id), new StartAttemptRequest(draft.Id, AttemptMode.Practice));

        Assert.Equal([StartAttempt.NoAccess], noTrial.Problems.Select(p => p.Code));
        Assert.Equal([StartAttempt.NoAccess], guestOnFree.Problems.Select(p => p.Code));
        Assert.True(notPublished.IsNotFound);
        await using var db = database.CreateContext();
        Assert.False(await db.Attempts.AnyAsync(a => a.UserId == user.Id || a.GuestToken == guestToken));
    }

    [Fact]
    public async Task Simulation_respects_max_attempts_for_the_same_user()
    {
        var preset = new RulePreset { Name = "Simulasi " + TestData.Unique(), TimeLimitMinutes = 30, QuestionCount = 2, MaxAttempts = 2 };
        var exam = await SeedExamAsync(TestData.Exam().Free().WithRules(preset).WithQuestions(4));
        var user = await SaveUserAsync();
        var other = await SaveUserAsync();
        var request = new StartAttemptRequest(exam.Id, AttemptMode.Simulation);

        var first = await StartAsync(new FakeCurrentUser(user.Id), request);
        var second = await StartAsync(new FakeCurrentUser(user.Id), request);
        var third = await StartAsync(new FakeCurrentUser(user.Id), request);
        var practice = await StartAsync(new FakeCurrentUser(user.Id), request with { Mode = AttemptMode.Practice });
        var otherUser = await StartAsync(new FakeCurrentUser(other.Id), request);

        Assert.True(first.Succeeded && second.Succeeded && practice.Succeeded && otherUser.Succeeded);
        Assert.Equal(2, first.Value!.QuestionCount);
        var problem = Assert.Single(third.Problems);
        Assert.Equal((StartAttempt.MaxAttemptsReached, 2), (problem.Code, (int)problem.Args[0]));
        using var rules = JsonDocument.Parse((await LoadAsync(first.Value.AttemptId)).RulesSnapshot);
        Assert.False(rules.RootElement.GetProperty("hintsEnabled").GetBoolean());
        Assert.Equal(30, rules.RootElement.GetProperty("timeLimitMinutes").GetInt32());
    }

    [Fact]
    public async Task Practice_overrides_out_of_bounds_are_rejected()
    {
        var exam = await SeedExamAsync(TestData.Exam().Free().WithQuestions(4));
        var user = await SaveUserAsync();

        var result = await StartAsync(new FakeCurrentUser(user.Id), new StartAttemptRequest(exam.Id, AttemptMode.Practice, new RuleOverrides(QuestionCount: 5)));

        Assert.Equal([SessionRulesBuilder.CountRange], result.Problems.Select(p => p.Code));
    }

    [Fact]
    public async Task Topic_filter_and_count_pick_from_the_chosen_topics_only()
    {
        var built = TestData.Exam().Free().WithQuestions(6).Build();
        var other = new Topic { Subject = built.Subject, Name = "Topik " + TestData.Unique() };
        foreach (var eq in built.Questions.Where(q => q.SortOrder > 3))
        {
            eq.Question!.Topic = other;
        }

        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(built)).Id;
        }

        var user = await SaveUserAsync();
        var overrides = new RuleOverrides(QuestionCount: 2, TopicIds: [other.Id]);

        var result = await StartAsync(new FakeCurrentUser(user.Id), new StartAttemptRequest(examId, AttemptMode.Practice, overrides));

        Assert.True(result.Succeeded, Describe(result));
        await using var check = database.CreateContext();
        var topics = await check.AttemptItems.Where(i => i.AttemptId == result.Value!.AttemptId).Select(i => i.Question!.TopicId).ToListAsync();
        Assert.Equal([other.Id, other.Id], topics);
    }

    [Fact]
    public void Guest_token_cookie_is_created_once_and_http_only()
    {
        var first = new DefaultHttpContext();
        var token = GuestTokenCookie.GetOrCreate(first);
        var header = first.Response.Headers.SetCookie.ToString();

        var second = new DefaultHttpContext();
        second.Request.Headers.Cookie = $"{GuestTokenCookie.Name}={token:N}";

        Assert.NotEqual(Guid.Empty, token);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", header, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(token, GuestTokenCookie.GetOrCreate(second));
        Assert.Empty(second.Response.Headers.SetCookie.ToString());
    }

    // ---------- Helpers ----------

    private sealed record SeededExam(long Id, IReadOnlyList<long> AllIds, IReadOnlyList<long> TrialIds);

    private sealed class SeededRandom(int seed) : IRandomSource
    {
        public Random Random { get; } = new(seed);
    }

    private async Task<Result<StartedAttempt>> StartAsync(FakeCurrentUser viewer, StartAttemptRequest request, int seed = 42)
    {
        await using var db = database.CreateContext();
        var clock = new TestClock();
        var handler = new StartAttempt(db, new EntitlementService(db, viewer, clock), new ExamQuestionScope(db), viewer, clock, new SeededRandom(seed));
        return await handler.HandleAsync(request, CancellationToken.None);
    }

    private async Task<SeededExam> SeedExamAsync(Exam exam, bool retireFirst = false)
    {
        if (retireFirst)
        {
            exam.Questions.OrderBy(q => q.SortOrder).First().Question!.Status = QuestionStatus.Retired;
        }

        await using var db = database.CreateContext();
        await db.SaveAsync(exam);
        var ordered = exam.Questions.OrderBy(q => q.SortOrder).ToList();
        return new SeededExam(exam.Id, [.. ordered.Select(q => q.QuestionId)], [.. ordered.Where(q => q.IsTrial).Select(q => q.QuestionId)]);
    }

    private async Task<User> SaveUserAsync()
    {
        await using var db = database.CreateContext();
        return await db.SaveAsync(TestData.User().Build());
    }

    private async Task<User> BuyerAsync(long examId)
    {
        var user = await SaveUserAsync();
        await using var db = database.CreateContext();
        db.ExamPurchases.Add(new ExamPurchase
        {
            UserId = user.Id,
            ExamId = examId,
            PricePaidIdr = 49_000,
            Status = ExamPurchaseStatus.Active,
            PurchasedAt = TestClock.DefaultStart.AddDays(-1),
            AccessExpiresAt = TestClock.DefaultStart.AddDays(30),
        });
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<Attempt> LoadAsync(long attemptId)
    {
        await using var db = database.CreateContext();
        return await db.Attempts.Include(a => a.Items).SingleAsync(a => a.Id == attemptId);
    }

    private async Task<List<long>> OrderAsync(long attemptId)
    {
        await using var db = database.CreateContext();
        return await db.AttemptItems.Where(i => i.AttemptId == attemptId).OrderBy(i => i.Position).Select(i => i.QuestionId).ToListAsync();
    }

    private static string Describe<T>(Result<T> result) => string.Join(", ", result.Problems.Select(p => p.Code));
}
