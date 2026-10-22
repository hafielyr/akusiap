using System.Net;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;

namespace Akusiap.IntegrationTests.Catalog;

/// <summary>BRD-04: /cari finds catalog questions by their text and exams by title, showing only the question stem.</summary>
[Collection(UsesDatabase.Name)]
public sealed class SearchTests(PostgresFixture database)
{
    [Fact]
    public async Task A_question_is_found_by_its_text_and_shows_only_the_stem()
    {
        await using var app = new AppFactory(database);
        using var guest = app.CreateClient();
        var token = TestData.Unique()[..8];
        var exam = TestData.Exam().Titled($"Tryout SKD {token}").Paid(79_000).WithQuestions(3).Build();
        var question = exam.Questions.OrderBy(q => q.SortOrder).First().Question!;
        question.Text = $"Manakah sila Pancasila{token} yang berbunyi Ketuhanan Yang Maha Esa?";
        question.Explanation = $"Rahasia pembahasan {token}";
        question.Options.First().Text = $"Rahasia pilihan {token}";
        await SaveAsync(exam);

        var html = WebUtility.HtmlDecode(await guest.GetStringAsync(new Uri($"/cari?q=pancasila{token}", UriKind.Relative)));

        Assert.Contains("data-testid=\"search-question\"", html, StringComparison.Ordinal);
        Assert.Contains($"Pancasila{token}</mark>", html, StringComparison.Ordinal);
        Assert.Contains($"Tryout SKD {token}", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/ujian/{exam.Id}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"Rahasia pembahasan {token}", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"Rahasia pilihan {token}", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Drafts_own_exams_and_retired_questions_are_never_found()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var guest = app.CreateClient();
        var token = TestData.Unique()[..8];
        var owner = TestData.User().Build();
        await using (var db = database.CreateContext())
        {
            await db.SaveAsync(owner);
        }

        var draft = TestData.Exam().Free().Draft().WithQuestions(1).Build();
        var own = TestData.Exam().Free().OwnedBy(owner).WithQuestions(1).Build();
        var retired = TestData.Exam().Free().WithQuestions(1).Build();
        foreach (var e in new[] { draft, own, retired })
        {
            e.Questions.Single().Question!.Text = $"Soal tersembunyi {token}";
        }

        retired.Questions.Single().Question!.Status = QuestionStatus.Retired;
        foreach (var e in new[] { draft, own, retired })
        {
            await SaveAsync(e);
        }

        var html = await guest.GetStringAsync(new Uri($"/cari?q=tersembunyi {token}", UriKind.Relative));

        Assert.Contains("data-testid=\"search-empty\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"search-question\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exams_match_by_title_and_short_queries_ask_for_more()
    {
        await using var app = new AppFactory(database);
        using var guest = app.CreateClient();
        var token = TestData.Unique()[..8];
        var exam = await SaveAsync(TestData.Exam().Titled($"Latihan BUMN {token}").Free().WithQuestions(2).Build());

        var found = await guest.GetStringAsync(new Uri($"/cari?q=BUMN {token}", UriKind.Relative));
        var tooShort = await guest.GetStringAsync(new Uri("/cari?q=a", UriKind.Relative));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/cari?q=BUMN {token}", UriKind.Relative));
        request.Headers.Add("HX-Request", "true");
        using var fragment = await guest.SendAsync(request);
        var fragmentHtml = await fragment.Content.ReadAsStringAsync();

        Assert.Contains($"data-testid=\"search-exam\" data-exam=\"{exam.Id}\"", found, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"search-hint\"", tooShort, StringComparison.Ordinal);
        Assert.StartsWith("<div id=\"search-results\"", fragmentHtml.TrimStart(), StringComparison.Ordinal);
    }

    private async Task<Exam> SaveAsync(Exam exam)
    {
        await using var db = database.CreateContext();
        return await db.SaveAsync(exam);
    }
}
