using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Sessions;

/// <summary>TRY-01 on a phone: the CAT grid shows answered, ragu-ragu and empty, and finishing asks for confirmation.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class CatTryoutFlowTests(MobileSite site)
{
    [Fact]
    public async Task The_cat_grid_tracks_answered_ragu_and_empty_questions()
    {
        var learner = await site.CreateUserAsync(TestData.User().Build(), "peserta-sandi-2026", RoleNames.Learner);
        var preset = new RulePreset { Name = "CAT " + TestData.Unique(), CatMode = true, TimeLimitMinutes = 30, ShuffleQuestions = false, ShuffleOptions = false };
        var exam = await site.SaveExamAsync(TestData.Exam().Titled($"Tryout {TestData.Unique()}").Free().WithRules(preset).WithQuestions(4));
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        await page.GotoAsync("/akun/masuk");
        await page.GetByLabel("Email").FillAsync(learner.Email!);
        await page.GetByLabel("Kata sandi").FillAsync("peserta-sandi-2026");
        await page.Locator("form button[type=submit]").ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/akun/masuk", StringComparison.Ordinal));

        await page.GotoAsync($"/ujian/{exam.Id}/mulai");
        await page.GetByTestId("mode-Simulation").CheckAsync();
        await page.GetByTestId("start").ClickAsync();
        await page.WaitForURLAsync("**/sesi/*");
        await Assertions.Expect(page.GetByTestId("player")).ToHaveAttributeAsync("data-cat", "true");
        var cells = page.GetByTestId("grid-cell");
        await Assertions.Expect(cells).ToHaveCountAsync(4);

        // Answer question 1, answer and mark question 2 ragu-ragu, leave 3 and 4 empty.
        await page.GetByTestId("option").First.CheckAsync();
        await page.GetByTestId("next").ClickAsync();
        await Assertions.Expect(page.GetByTestId("position")).ToHaveTextAsync("2");
        await page.GetByTestId("option").Nth(1).CheckAsync();
        await page.GetByTestId("flag").ClickAsync();

        string[] states = ["answered", "ragu", "empty", "empty"];
        for (var i = 0; i < states.Length; i++)
        {
            await Assertions.Expect(cells.Nth(i)).ToHaveAttributeAsync("data-state", states[i]);
        }

        await Assertions.Expect(page.GetByTestId("count-answered")).ToHaveTextAsync("2");
        await Assertions.Expect(page.GetByTestId("count-ragu")).ToHaveTextAsync("1");
        await Assertions.Expect(page.GetByTestId("remaining")).ToHaveTextAsync("2");
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 0, $"The page scrolls sideways by {overflow}px at {MobileSite.Width}px");

        // Finishing asks first; confirming submits and opens the result.
        await page.GetByTestId("finish").ClickAsync();
        await Assertions.Expect(page.GetByTestId("confirm-finish")).ToBeVisibleAsync();
        await page.GetByTestId("submit").ClickAsync();
        await page.WaitForURLAsync("**/hasil");
    }
}
