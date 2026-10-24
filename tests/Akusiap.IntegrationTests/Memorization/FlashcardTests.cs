using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Memorization;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Memorization;

/// <summary>MEM-03: flashcard sets of 10 (due first), rated with SM-2, ending in a summary; only fully accessible questions.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class FlashcardTests(PostgresFixture database)
{
    [Fact]
    public async Task A_set_ends_after_10_cards_with_a_summary_and_due_cards_come_first()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        List<long> questions;
        long examId;
        await using (var db = database.CreateContext())
        {
            var exam = await db.SaveAsync(TestData.Exam().Free().WithQuestions(12).Build());
            examId = exam.Id;
            questions = [.. exam.Questions.OrderBy(q => q.SortOrder).Select(q => q.QuestionId)];
            db.ReviewCards.Add(new ReviewCard { UserId = user.Id, QuestionId = questions[11], State = ReviewCardState.Review, DueAt = app.Clock.UtcNow.AddDays(-1), IntervalDays = 6, Repetitions = 2 });
            await db.SaveChangesAsync();
        }

        var set = await StartAsync(learner, examId.ToString(CultureInfo.InvariantCulture));
        var seen = new List<int>();
        for (var i = 0; i < 10; i++)
        {
            var page = await learner.GetStringAsync(new Uri(set, UriKind.Relative));
            var position = int.Parse(PositionPattern().Match(page).Groups[1].Value, CultureInfo.InvariantCulture);
            seen.Add(position);
            using var rated = await learner.PostFormAsync(set, new Dictionary<string, string>
            {
                ["position"] = position.ToString(CultureInfo.InvariantCulture), ["rating"] = i == 0 ? nameof(ReviewRating.Forgot) : nameof(ReviewRating.Good),
            }, $"{set}?handler=Rate");
            Assert.Equal(HttpStatusCode.Redirect, rated.StatusCode);
        }

        var summary = await learner.GetStringAsync(new Uri(set, UriKind.Relative));

        Assert.Equal(Enumerable.Range(1, 10), seen);
        Assert.Contains("data-testid=\"flashcard-summary\"", summary, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"summary-Good\">9<", summary, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"summary-Forgot\">1<", summary, StringComparison.Ordinal);
        await using var check = database.CreateContext();
        var attemptId = long.Parse(set.Split('/')[^1], CultureInfo.InvariantCulture);
        var attempt = await check.Attempts.Include(a => a.Items).SingleAsync(a => a.Id == attemptId);
        Assert.Equal((AttemptMode.Flashcard, AttemptSource.Review, AttemptStatus.Submitted, 10), (attempt.Mode, attempt.Source, attempt.Status, attempt.Items.Count));
        Assert.Equal(questions[11], attempt.Items.Single(i => i.Position == 1).QuestionId);
        var cards = await check.ReviewCards.Include(c => c.Logs).Where(c => c.UserId == user.Id).ToListAsync();
        Assert.Equal(10, cards.Count);
        Assert.All(cards, c => Assert.Single(c.Logs));
        var due = cards.Single(c => c.QuestionId == questions[11]);
        Assert.Equal((ReviewCardState.Learning, 1), (due.State, due.Lapses));
    }

    [Fact]
    public async Task Questions_from_exams_without_full_access_are_excluded()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        long paidExamId;
        long freeQuestion;
        List<long> paidQuestions;
        await using (var db = database.CreateContext())
        {
            var paid = await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(3, trial: 3).Build());
            var free = await db.SaveAsync(TestData.Exam().Free().WithQuestions(1).Build());
            paidExamId = paid.Id;
            paidQuestions = [.. paid.Questions.Select(q => q.QuestionId)];
            freeQuestion = free.Questions.Single().QuestionId;
            db.ExamPurchases.Add(new ExamPurchase
            {
                UserId = user.Id, ExamId = paid.Id, PricePaidIdr = 49_000, Status = ExamPurchaseStatus.Expired,
                PurchasedAt = app.Clock.UtcNow.AddDays(-40), AccessExpiresAt = app.Clock.UtcNow.AddDays(-10),
            });
            foreach (var questionId in paidQuestions.Append(freeQuestion))
            {
                db.ReviewCards.Add(new ReviewCard { UserId = user.Id, QuestionId = questionId, State = ReviewCardState.Learning, DueAt = app.Clock.UtcNow.AddHours(-1) });
            }

            await db.SaveChangesAsync();
        }

        var decks = await learner.GetStringAsync(new Uri("/learn/kartu", UriKind.Relative));
        var set = await StartAsync(learner, null);
        using var blocked = await learner.PostFormAsync("/learn/kartu", new Dictionary<string, string> { ["examId"] = paidExamId.ToString(CultureInfo.InvariantCulture) }, "/learn/kartu?handler=Start");

        Assert.DoesNotContain($"data-exam=\"{paidExamId}\"", decks, StringComparison.Ordinal);
        Assert.Equal("/learn/kartu", blocked.Headers.Location!.OriginalString);
        await using var check = database.CreateContext();
        var attemptId = long.Parse(set.Split('/')[^1], CultureInfo.InvariantCulture);
        var items = await check.AttemptItems.Where(i => i.AttemptId == attemptId).Select(i => i.QuestionId).ToListAsync();
        Assert.Equal([freeQuestion], items);
    }

    [Fact]
    public async Task A_wrong_pick_shows_the_answer_and_a_right_pick_moves_to_the_next_card()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(TestData.Exam().Free().WithQuestions(3).Build())).Id;
        }

        var set = await StartAsync(learner, examId.ToString(CultureInfo.InvariantCulture));
        var attemptId = long.Parse(set.Split('/')[^1], CultureInfo.InvariantCulture);
        var options = await OptionsByPositionAsync(attemptId);

        // Card 1, wrong pick (no htmx): back to card 1 with the answer shown.
        using var wrong = await AnswerAsync(learner, set, 1, options[1].Wrong);
        var revealed = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri(wrong.Headers.Location!.OriginalString, UriKind.Relative)));
        // Card 2, right pick with htmx: the next card comes back as a partial with "Benar!".
        using var right = await AnswerAsync(learner, set, 2, options[2].Right, htmx: true);
        var next = await right.Content.ReadAsStringAsync();
        // Card 3, right pick: the set ends.
        using var last = await AnswerAsync(learner, set, 3, options[3].Right);
        var summary = await learner.GetStringAsync(new Uri(set, UriKind.Relative));

        Assert.Equal($"{set}?kartu=1", wrong.Headers.Location!.OriginalString);
        Assert.Contains("data-position=\"1\" data-answered=\"true\"", revealed, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"flashcard-verdict\" data-correct=\"false\"", revealed, StringComparison.Ordinal);
        Assert.Contains("data-state=\"is-wrong\"", revealed, StringComparison.Ordinal);
        Assert.Contains("data-state=\"is-right\"", revealed, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"next-card\"", revealed, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Contains("data-position=\"3\" data-answered=\"false\"", next, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"flashcard-correct\"", next, StringComparison.Ordinal);
        Assert.DoesNotContain("<html", next, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"summary-Forgot\">1<", summary, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"summary-Good\">2<", summary, StringComparison.Ordinal);
        await using var check = database.CreateContext();
        var items = await check.AttemptItems.Include(i => i.SelectedOptions).Where(i => i.AttemptId == attemptId).OrderBy(i => i.Position).ToListAsync();
        Assert.Equal([false, true, true], items.Select(i => i.IsCorrect));
        Assert.Equal([options[1].Wrong], items[0].SelectedOptions.Select(o => o.QuestionOptionId));
        var first = await check.ReviewCards.SingleAsync(c => c.UserId == user.Id && c.QuestionId == items[0].QuestionId);
        Assert.Equal(app.Clock.UtcNow.AddDays(1), first.DueAt);                                     // wrong: rated Forgot, back tomorrow
    }

    [Fact]
    public async Task Picking_options_on_someone_elses_set_is_not_found()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var owner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        using var other = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(TestData.Exam().Free().WithQuestions(1).Build())).Id;
        }

        var set = await StartAsync(owner, examId.ToString(CultureInfo.InvariantCulture));
        var options = await OptionsByPositionAsync(long.Parse(set.Split('/')[^1], CultureInfo.InvariantCulture));
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["position"] = "1", ["option"] = options[1].Right.ToString(CultureInfo.InvariantCulture),
            ["__RequestVerificationToken"] = await other.GetAntiforgeryTokenAsync("/learn/kartu"),
        });

        using var response = await other.PostAsync(new Uri($"{set}?handler=Answer", UriKind.Relative), form);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Dictionary<int, (long Right, long Wrong)>> OptionsByPositionAsync(long attemptId)
    {
        await using var db = database.CreateContext();
        var items = await db.AttemptItems.Where(i => i.AttemptId == attemptId)
            .Select(i => new { i.Position, Options = i.Question!.Options.Select(o => new { o.Id, o.IsCorrect }).ToList() })
            .ToListAsync();
        return items.ToDictionary(i => i.Position, i => (i.Options.First(o => o.IsCorrect).Id, i.Options.First(o => !o.IsCorrect).Id));
    }

    private static async Task<HttpResponseMessage> AnswerAsync(HttpClient client, string set, int position, long option, bool htmx = false)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["position"] = position.ToString(CultureInfo.InvariantCulture),
            ["option"] = option.ToString(CultureInfo.InvariantCulture),
            ["__RequestVerificationToken"] = await client.GetAntiforgeryTokenAsync(set),
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{set}?handler=Answer", UriKind.Relative)) { Content = form };
        if (htmx)
        {
            request.Headers.Add("HX-Request", "true");
        }

        return await client.SendAsync(request);
    }

    private static async Task<string> StartAsync(HttpClient client, string? examId)
    {
        var fields = examId is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["examId"] = examId };
        using var response = await client.PostFormAsync("/learn/kartu", fields, "/learn/kartu?handler=Start");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.Matches("^/learn/kartu/\\d+$", location);
        return location;
    }

    [GeneratedRegex("data-testid=\"flashcard\" data-position=\"(\\d+)\"")]
    private static partial Regex PositionPattern();
}
