using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Memorization;

/// <summary>MEM-03 on a phone: flip a card, rate it, and the next card swaps in without a page load.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class FlashcardFlowTests(MobileSite site)
{
    [Fact]
    public async Task Flipping_and_rating_swaps_in_the_next_card()
    {
        var learner = await site.CreateUserAsync(TestData.User().Build(), "kartu-sandi-2026", RoleNames.Learner);
        var exam = await site.SaveExamAsync(TestData.Exam().Titled($"Kartu {TestData.Unique()}").Free().WithQuestions(2));
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        await page.GotoAsync("/akun/masuk");
        await page.GetByLabel("Email").FillAsync(learner.Email!);
        await page.GetByLabel("Kata sandi").FillAsync("kartu-sandi-2026");
        await page.Locator("form button[type=submit]").ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/akun/masuk", StringComparison.Ordinal));

        await page.GotoAsync("/learn/kartu");
        await page.Locator($"[data-testid=deck][data-exam='{exam.Id}']").GetByTestId("start-deck").ClickAsync();
        await page.WaitForURLAsync("**/learn/kartu/*");
        var setUrl = page.Url;

        var card = page.GetByTestId("flashcard");
        await Assertions.Expect(card).ToHaveAttributeAsync("data-position", "1");
        await Assertions.Expect(page.GetByTestId("answer")).ToBeHiddenAsync();
        await page.GetByTestId("flip").ClickAsync();
        await Assertions.Expect(page.GetByTestId("answer")).ToBeVisibleAsync();
        await page.GetByTestId("rate-Good").ClickAsync();

        await Assertions.Expect(card).ToHaveAttributeAsync("data-position", "2");
        await Assertions.Expect(page.GetByTestId("answer")).ToBeHiddenAsync();
        Assert.Equal(setUrl, page.Url);
        await page.GetByTestId("flip").ClickAsync();
        await page.GetByTestId("rate-Easy").ClickAsync();

        await Assertions.Expect(page.GetByTestId("flashcard-summary")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("summary-Good")).ToHaveTextAsync("1");
        await Assertions.Expect(page.GetByTestId("summary-Easy")).ToHaveTextAsync("1");
    }

    /// <summary>MEM-04: tapping an option answers the card; right goes straight on, wrong shows the answer first.</summary>
    [Fact]
    public async Task A_right_pick_shows_the_next_card_and_a_wrong_pick_shows_the_answer()
    {
        var learner = await site.CreateUserAsync(TestData.User().Build(), "kartu-sandi-2026", RoleNames.Learner);
        var exam = await site.SaveExamAsync(TestData.Exam().Titled($"Kuis {TestData.Unique()}").Free().WithQuestions(3));
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        await page.GotoAsync("/akun/masuk");
        await page.GetByLabel("Email").FillAsync(learner.Email!);
        await page.GetByLabel("Kata sandi").FillAsync("kartu-sandi-2026");
        await page.Locator("form button[type=submit]").ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/akun/masuk", StringComparison.Ordinal));
        await page.GotoAsync("/learn/kartu");
        await page.Locator($"[data-testid=deck][data-exam='{exam.Id}']").GetByTestId("start-deck").ClickAsync();
        await page.WaitForURLAsync("**/learn/kartu/*");
        var setUrl = page.Url;
        var card = page.GetByTestId("flashcard");

        // "Pilihan 1" is the right option in test exams.
        await page.GetByTestId("option").Filter(new() { HasText = "Pilihan 2" }).ClickAsync();
        await Assertions.Expect(card).ToHaveAttributeAsync("data-answered", "true");
        await Assertions.Expect(page.GetByTestId("flashcard-verdict")).ToHaveAttributeAsync("data-correct", "false");
        await Assertions.Expect(page.GetByTestId("answer")).ToBeVisibleAsync();
        await page.GetByTestId("next-card").ClickAsync();

        await Assertions.Expect(card).ToHaveAttributeAsync("data-position", "2");
        await page.GetByTestId("option").Filter(new() { HasText = "Pilihan 1" }).ClickAsync();
        await Assertions.Expect(card).ToHaveAttributeAsync("data-position", "3");
        await Assertions.Expect(page.GetByTestId("flashcard-correct")).ToBeVisibleAsync();
        await page.GetByTestId("option").Filter(new() { HasText = "Pilihan 1" }).ClickAsync();

        await Assertions.Expect(page.GetByTestId("flashcard-summary")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("summary-Forgot")).ToHaveTextAsync("1");
        await Assertions.Expect(page.GetByTestId("summary-Good")).ToHaveTextAsync("2");
        Assert.Equal(setUrl, page.Url);
        var sideways = await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth");
        Assert.False(sideways);
    }
}
