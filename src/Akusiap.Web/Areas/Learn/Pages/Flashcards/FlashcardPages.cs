using Akusiap.Application.Memorization;
using Akusiap.Domain;
using Akusiap.Domain.Memorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Akusiap.Web.Areas.Learn.Pages.Flashcards;

/// <summary><c>/learn/kartu</c> (MEM-03): pick "Kartu saya" or an exam with full access, then start a set of 10.</summary>
public sealed class IndexModel(Application.Memorization.Flashcards flashcards) : PageModel
{
    public const string Route = "/learn/kartu";

    public IReadOnlyList<FlashcardDeck> Decks { get; private set; } = [];

    public static string SetUrl(long attemptId) => $"{Route}/{attemptId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Decks = await flashcards.DecksAsync(cancellationToken);

    public async Task<IActionResult> OnPostStartAsync(long? examId, CancellationToken cancellationToken)
    {
        var result = await flashcards.StartAsync(examId, cancellationToken);
        if (!result.Succeeded)
        {
            TempData["Flash"] = result.Problems[0].Code;
            return Redirect(Route);
        }

        return Redirect(SetUrl(result.Value));
    }
}

/// <summary>
/// <c>/learn/kartu/{id}</c> (MEM-03, MEM-04): one card at a time, answered by picking options. A right pick swaps in
/// the next card (or the summary) with a "Benar!" note; a wrong pick shows the same card with the answer and a "next"
/// button. Flipping the card and rating it yourself still works. Everything posts with htmx; without htmx the page
/// reloads (<c>?kartu={position}</c> shows an answered card).
/// </summary>
public sealed class SetModel(Application.Memorization.Flashcards flashcards) : PageModel
{
    public const string NextDueKey = "NextDue";
    public const string FeedbackKey = "FlashcardFeedback";

    public FlashcardCard? Card { get; private set; }

    public FlashcardSummary? Summary { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, [FromQuery(Name = "kartu")] int? position, CancellationToken cancellationToken)
    {
        if (position is { } at && await flashcards.CardAtAsync(id, at, cancellationToken) is { IsAnswered: true } answered)
        {
            Card = answered;
            return Page();
        }

        return await LoadAsync(id, cancellationToken) ? Page() : NotFound();
    }

    /// <summary>Checks the picked options (MEM-04): right → the next card, wrong → this card with its answer.</summary>
    public async Task<IActionResult> OnPostAnswerAsync(long id, int position, [FromForm(Name = "option")] long[]? options, CancellationToken cancellationToken)
    {
        if (await flashcards.AnswerAsync(id, position, options ?? [], cancellationToken) is not { } answer)
        {
            return NotFound();
        }

        var note = JakartaTime.FormatDate(answer.NextDue);
        var htmx = Request.Headers.ContainsKey("HX-Request");
        if (answer.IsCorrect)
        {
            if (htmx)
            {
                ViewData[NextDueKey] = note;
                ViewData[FeedbackKey] = "correct";
                await LoadAsync(id, cancellationToken);
                return CurrentPartial();
            }

            TempData[NextDueKey] = note;
            TempData[FeedbackKey] = "correct";
            return Redirect(IndexModel.SetUrl(id));
        }

        if (htmx)
        {
            ViewData[NextDueKey] = note;
            Card = await flashcards.CardAtAsync(id, position, cancellationToken);
            return CurrentPartial();
        }

        TempData[NextDueKey] = note;
        return Redirect($"{IndexModel.SetUrl(id)}?kartu={position.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    }

    /// <summary>The current card (or the summary) as an htmx partial.</summary>
    public async Task<IActionResult> OnGetCardAsync(long id, CancellationToken cancellationToken) =>
        await LoadAsync(id, cancellationToken) ? CurrentPartial() : NotFound();

    public async Task<IActionResult> OnPostRateAsync(long id, int position, ReviewRating rating, CancellationToken cancellationToken)
    {
        if (await flashcards.RateAsync(id, position, rating, cancellationToken) is not { } nextDue)
        {
            return NotFound();
        }

        // SRS-02: say when the card just rated comes back.
        var note = JakartaTime.FormatDate(nextDue);
        if (Request.Headers.ContainsKey("HX-Request"))
        {
            ViewData[NextDueKey] = note;
            await LoadAsync(id, cancellationToken);
            return CurrentPartial();
        }

        TempData[NextDueKey] = note;
        return Redirect(IndexModel.SetUrl(id));
    }

    /// <summary>The card or summary partial, keeping this page's ViewData (the next-due note).</summary>
    private PartialViewResult CurrentPartial() => Card is { } card
        ? new() { ViewName = "_FlashcardPartial", ViewData = new ViewDataDictionary<FlashcardCard>(ViewData, card) }
        : new() { ViewName = "_FlashcardSummaryPartial", ViewData = new ViewDataDictionary<FlashcardSummary>(ViewData, Summary!) };

    private async Task<bool> LoadAsync(long id, CancellationToken cancellationToken)
    {
        Card = await flashcards.NextCardAsync(id, cancellationToken);
        if (Card is null)
        {
            Summary = await flashcards.SummaryAsync(id, cancellationToken);
        }

        return Card is not null || Summary is not null;
    }
}
