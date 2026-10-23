using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Catalog;

/// <summary>BRD-04 on a phone: search from the home hero, land on /cari with the matching question, refine live.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class SearchFlowTests(MobileSite site)
{
    [Fact]
    public async Task Searching_from_the_home_page_finds_the_question()
    {
        var token = TestData.Unique()[..8];
        var exam = TestData.Exam().Titled($"Tryout TWK {token}").Free().WithQuestions(2).Build();
        exam.Questions.OrderBy(q => q.SortOrder).First().Question!.Text = $"Lambang sila pertama Garuda{token} adalah bintang.";
        exam.Questions.OrderBy(q => q.SortOrder).Last().Question!.Text = $"Lambang sila kedua Garuda{token} adalah rantai.";
        await site.SaveExamAsync(exam);
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();

        await page.GotoAsync("/");
        await page.GetByTestId("search-input").FillAsync($"garuda{token}");
        await page.GetByTestId("search-input").PressAsync("Enter");
        await page.WaitForURLAsync("**/cari?q=*");

        await Assertions.Expect(page.GetByTestId("search-question")).ToHaveCountAsync(2);
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 0, $"The page scrolls sideways by {overflow}px at {MobileSite.Width}px");

        // Typing on /cari narrows the results without a page load (htmx).
        await page.GetByTestId("search-input").FillAsync($"kedua garuda{token}");
        await Assertions.Expect(page.GetByTestId("search-question")).ToHaveCountAsync(1);
        await Assertions.Expect(page.GetByTestId("search-question")).ToContainTextAsync("rantai");
    }
}
