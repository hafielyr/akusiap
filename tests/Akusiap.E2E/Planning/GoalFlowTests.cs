using System.Globalization;
using Akusiap.Domain;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Planning;

/// <summary>PLN-01 on a phone: set an exam goal from the exam page, then see its countdown on Today.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class GoalFlowTests(MobileSite site)
{
    [Fact]
    public async Task Setting_a_goal_shows_the_countdown_on_today()
    {
        var learner = await site.CreateUserAsync(TestData.User().Build(), "target-sandi-2026", RoleNames.Learner);
        var exam = await site.SaveExamAsync(TestData.Exam().Titled($"Target {TestData.Unique()}").Free().WithQuestions(2));
        var examDate = JakartaTime.Today(site.Now).AddDays(30);
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        await page.GotoAsync("/akun/masuk");
        await page.GetByLabel("Email").FillAsync(learner.Email!);
        await page.GetByLabel("Kata sandi").FillAsync("target-sandi-2026");
        await page.Locator("form button[type=submit]").ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/akun/masuk", StringComparison.Ordinal));

        await page.GotoAsync($"/ujian/{exam.Id}");
        await page.GetByTestId("goal-link").ClickAsync();
        await page.WaitForURLAsync("**/learn/target?examId=*");
        await Assertions.Expect(page.GetByLabel("Ujian", new() { Exact = true })).ToHaveValueAsync(exam.Id.ToString(CultureInfo.InvariantCulture));
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        await page.GetByLabel("Tanggal ujian").FillAsync(examDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        await page.GetByTestId("goal-save").ClickAsync();

        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("Target tersimpan.");
        await Assertions.Expect(page.Locator($"[data-testid=goal][data-exam='{exam.Id}']").GetByTestId("goal-days")).ToHaveTextAsync("H-30");
        Assert.Equal(0, overflow);

        await page.GotoAsync("/learn/hari-ini");
        var countdown = page.Locator($"[data-testid=goal-countdown][data-exam='{exam.Id}']");
        await Assertions.Expect(countdown.GetByTestId("goal-countdown-days")).ToHaveTextAsync("H-30");
        Assert.Equal(0, await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth"));
    }
}
