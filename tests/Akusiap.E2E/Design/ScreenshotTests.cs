using Akusiap.Domain.Memorization;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Design;

/// <summary>
/// BRD-01: full-page screenshots of the main screens on a phone and a desktop, for design review. Runs only when
/// LS_SCREENSHOTS_DIR is set (the PNGs go there); otherwise it does nothing, so CI stays fast.
/// </summary>
[Collection(UsesMobileSite.Name)]
public sealed class ScreenshotTests(MobileSite site)
{
    public const string DirectoryVariable = "LS_SCREENSHOTS_DIR";

    [Fact]
    public async Task Capture_main_screens()
    {
        var directory = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var learner = await site.CreateUserAsync(TestData.User().Named("Sari").Build(), "layar-sandi-2026", RoleNames.Learner);
        var exams = new List<long>();
        foreach (var (title, free) in new[] { ("Tryout SKD CPNS 2026", false), ("Latihan TIU Dasar", true), ("Rekrutmen Bersama BUMN: TKD", false) })
        {
            var builder = TestData.Exam().Titled($"{title} {TestData.Unique()[..4]}").WithQuestions(12, trial: free ? 0 : 10);
            exams.Add((await site.SaveExamAsync(free ? builder.Free() : builder.Paid(79_000))).Id);
        }

        await site.WithDatabaseAsync(async db =>
        {
            var question = db.ExamQuestions.Where(q => q.ExamId == exams[1]).Select(q => q.QuestionId).First();
            db.ReviewCards.Add(new ReviewCard { UserId = learner.Id, QuestionId = question, State = ReviewCardState.Learning, DueAt = site.Now.AddHours(-1) });
            await db.SaveChangesAsync();
        });

        foreach (var (name, desktop) in new[] { ("phone", false), ("desktop", true) })
        {
            await using var device = desktop ? await site.NewDesktopAsync() : await site.NewPhoneAsync();
            var page = await device.NewPageAsync();
            await ShotAsync(page, "/", $"{name}-1-home");
            await ShotAsync(page, "/ujian", $"{name}-2-catalog");
            await ShotAsync(page, $"/ujian/{exams[0]}", $"{name}-3-exam");
            await ShotAsync(page, "/akun/masuk", $"{name}-4-sign-in");
            await page.GetByLabel("Email").FillAsync(learner.Email!);
            await page.GetByLabel("Kata sandi").FillAsync("layar-sandi-2026");
            await page.Locator("form button[type=submit]").ClickAsync();
            await page.WaitForURLAsync(url => !url.Contains("/akun/masuk", StringComparison.Ordinal));
            await ShotAsync(page, "/learn", $"{name}-5-learn");
            await ShotAsync(page, "/learn/hari-ini", $"{name}-6-today");
        }

        async Task ShotAsync(IPage page, string url, string file)
        {
            await page.GotoAsync(url);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(directory, $"{file}.png"), FullPage = true });
        }
    }
}
