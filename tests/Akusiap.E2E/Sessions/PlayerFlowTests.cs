using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Sessions;

/// <summary>SES-03 on a phone: answer 3 questions, flag 1, move back and forth, and the grid keeps up.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class PlayerFlowTests(MobileSite site)
{
    [Fact]
    public async Task Answering_flagging_and_navigating_update_the_grid()
    {
        var exam = await site.SaveExamAsync(TestData.Exam().Titled($"Pemain {TestData.Unique()}").Paid(49_000).WithQuestions(5, trial: 5));
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();

        await page.GotoAsync($"/ujian/{exam.Id}/mulai");
        await page.GetByTestId("start").ClickAsync();
        await page.WaitForURLAsync("**/sesi/*");
        var cells = page.GetByTestId("grid-cell");
        await Assertions.Expect(cells).ToHaveCountAsync(5);
        await Assertions.Expect(page.GetByTestId("remaining")).ToHaveTextAsync("5");

        // Answer questions 1–3 (the first option each time) and flag question 2.
        for (var i = 1; i <= 3; i++)
        {
            await Assertions.Expect(page.GetByTestId("position")).ToHaveTextAsync($"{i}");
            await page.GetByTestId("option").First.CheckAsync();
            if (i == 2)
            {
                await page.GetByTestId("flag").ClickAsync();
            }

            await page.GetByTestId("next").ClickAsync();
        }

        await Assertions.Expect(page.GetByTestId("position")).ToHaveTextAsync("4");
        await Assertions.Expect(page.GetByTestId("remaining")).ToHaveTextAsync("2");

        // Back to question 2 with the grid; its answer and flag are still there.
        await cells.Nth(1).ClickAsync();
        await Assertions.Expect(page.GetByTestId("position")).ToHaveTextAsync("2");
        await Assertions.Expect(page.GetByTestId("option").First).ToBeCheckedAsync();
        await Assertions.Expect(page.GetByTestId("flag")).ToHaveAttributeAsync("aria-pressed", "true");
        await page.GetByTestId("prev").ClickAsync();
        await Assertions.Expect(page.GetByTestId("position")).ToHaveTextAsync("1");

        // Keyboard: the arrow keys move between questions.
        await page.GetByTestId("question").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(page.GetByTestId("position")).ToHaveTextAsync("2");

        string[] answered = ["true", "true", "true", "false", "false"];
        string[] flagged = ["false", "true", "false", "false", "false"];
        for (var i = 0; i < 5; i++)
        {
            await Assertions.Expect(cells.Nth(i)).ToHaveAttributeAsync("data-answered", answered[i]);
            await Assertions.Expect(cells.Nth(i)).ToHaveAttributeAsync("data-flagged", flagged[i]);
        }

        await Assertions.Expect(cells.Nth(1)).ToHaveAttributeAsync("aria-current", "step");
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 0, $"The page scrolls sideways by {overflow}px at {MobileSite.Width}px");

        // SES-04: closing and reopening the session restores answers, flags and the position.
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var url = page.Url;
        await page.GotoAsync("about:blank");
        await page.GotoAsync(url);
        await Assertions.Expect(page.GetByTestId("position")).ToHaveTextAsync("2");
        for (var i = 0; i < 5; i++)
        {
            await Assertions.Expect(cells.Nth(i)).ToHaveAttributeAsync("data-answered", answered[i]);
            await Assertions.Expect(cells.Nth(i)).ToHaveAttributeAsync("data-flagged", flagged[i]);
        }

        await Assertions.Expect(page.GetByTestId("remaining")).ToHaveTextAsync("2");
    }
}
