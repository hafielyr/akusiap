using Akusiap.Application.Abstractions;
using Akusiap.Domain.Content;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Akusiap.IntegrationTests.Infrastructure;

/// <summary>F-05 sample: the app under test uses the per-run database, the test clock and the fake email sender.</summary>
[Collection(UsesDatabase.Name)]
public sealed class AppFactoryTests(PostgresFixture database)
{
    [Fact]
    public async Task App_reads_data_saved_by_the_test_from_the_per_run_database()
    {
        await using (var db = database.CreateContext())
        {
            await db.SaveAsync<Exam>(TestData.Exam().Titled("Tryout CPNS Contoh").Paid(49_000).WithQuestions(40, trial: 30));
        }

        await using var app = new AppFactory(database);
        await using var scope = app.Services.CreateAsyncScope();
        var appDb = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var exam = await appDb.Exams
            .Include(e => e.Questions).ThenInclude(eq => eq.Question!).ThenInclude(q => q.Options)
            .SingleAsync(e => e.Title == "Tryout CPNS Contoh");

        Assert.Equal(49_000, exam.PriceIdr);
        Assert.False(exam.IsFree);
        Assert.Equal(40, exam.Questions.Count);
        Assert.Equal(30, exam.Questions.Count(q => q.IsTrial));
        Assert.All(exam.Questions, eq => Assert.Single(eq.Question!.Options, o => o.IsCorrect));
    }

    [Fact]
    public async Task App_uses_the_test_clock_and_captures_email()
    {
        await using var app = new AppFactory(database);
        app.Clock.UtcNow = new DateTime(2026, 12, 31, 16, 59, 0, DateTimeKind.Utc);
        app.Clock.Advance(TimeSpan.FromMinutes(1));

        var clock = app.Services.GetRequiredService<IClock>();
        await app.Services.GetRequiredService<IEmailSender>()
            .SendAsync(new EmailMessage("budi@example.com", "Struk pembayaran", "<p>Terima kasih</p>"), CancellationToken.None);

        Assert.Equal(new DateTime(2026, 12, 31, 17, 0, 0, DateTimeKind.Utc), clock.UtcNow);
        Assert.Equal("Struk pembayaran", Assert.Single(app.Email.To("BUDI@example.com")).Subject);
    }

    [Fact]
    public async Task Builders_make_unique_rows_so_tests_can_share_the_database()
    {
        await using var db = database.CreateContext();

        var first = await db.SaveAsync(TestData.User().Build());
        var second = await db.SaveAsync(TestData.User().Build());
        await db.SaveAsync<Exam>(TestData.Exam().WithQuestions(2));
        await db.SaveAsync<Exam>(TestData.Exam().WithQuestions(2));

        Assert.NotEqual(first.Email, second.Email);
    }
}
