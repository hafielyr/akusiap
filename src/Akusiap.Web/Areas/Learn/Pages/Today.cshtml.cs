using Akusiap.Application.Memorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Learn.Pages;

/// <summary>
/// <c>/learn/hari-ini</c> (SRS-03, story M-08): today's reviews. The due count in total ("Kartu saya") and per exam with
/// full access, each with "Mulai 10 kartu". The home page for signed-in learners.
/// </summary>
public sealed class TodayModel(Application.Memorization.Flashcards flashcards, Application.Gamification.Badges badges, Application.Abstractions.ICurrentUser currentUser) : PageModel
{
    public const string Route = "/learn/hari-ini";

    public FlashcardDeck All { get; private set; } = null!;

    public IReadOnlyList<FlashcardDeck> Exams { get; private set; } = [];

    /// <summary>Level and streak for the journey strip (GAM-01).</summary>
    public Domain.Gamification.LevelProgress Level { get; private set; } = null!;

    public Domain.Gamification.StreakInfo Streak { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var decks = await flashcards.DecksAsync(cancellationToken);
        All = decks.Single(d => d.ExamId is null);
        Exams = [.. decks.Where(d => d.ExamId is not null).OrderByDescending(d => d.DueCount)];
        (Level, Streak) = await badges.SummaryAsync(currentUser.UserId!.Value, cancellationToken);
    }
}
