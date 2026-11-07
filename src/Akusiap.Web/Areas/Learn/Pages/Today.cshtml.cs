using Akusiap.Application.Gamification;
using Akusiap.Application.Memorization;
using Akusiap.Web.Pages.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Learn.Pages;

/// <summary>
/// <c>/learn/hari-ini</c> (SRS-03, DRL-01, PLN-01, PLN-02, story M-08): today's practice. Each exam goal with its countdown
/// and today's plan, the daily drill
/// with its week strip and streak, the level strip, and the due flashcards in total and per exam with full access. The
/// home page for signed-in learners.
/// </summary>
public sealed class TodayModel(
    Application.Memorization.Flashcards flashcards,
    Badges badges,
    DailyDrills drills,
    Application.Planning.StudyPlans plans,
    Application.Abstractions.ICurrentUser currentUser) : PageModel
{
    public const string Route = "/learn/hari-ini";

    /// <summary>Today's plan for each goal whose exam is today or later (PLN-01, PLN-02); passed goals stay only on <c>/learn/target</c>.</summary>
    public IReadOnlyList<Application.Planning.StudyPlanToday> Plans { get; private set; } = [];

    public FlashcardDeck All { get; private set; } = null!;

    public IReadOnlyList<FlashcardDeck> Exams { get; private set; } = [];

    /// <summary>Level and streak for the journey strip (GAM-01).</summary>
    public Domain.Gamification.LevelProgress Level { get; private set; } = null!;

    public Domain.Gamification.StreakInfo Streak { get; private set; } = null!;

    /// <summary>Today's drill (DRL-01).</summary>
    public DrillToday Drill { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var decks = await flashcards.DecksAsync(cancellationToken);
        All = decks.Single(d => d.ExamId is null);
        Exams = [.. decks.Where(d => d.ExamId is not null).OrderByDescending(d => d.DueCount)];
        (Level, Streak) = await badges.SummaryAsync(currentUser.UserId!.Value, cancellationToken);
        Drill = await drills.TodayAsync(cancellationToken);
        Plans = await plans.TodayAsync(cancellationToken);
    }

    /// <summary>Opens the unfinished drill or starts a new one.</summary>
    public async Task<IActionResult> OnPostDrillAsync(CancellationToken cancellationToken)
    {
        var result = await drills.StartAsync(cancellationToken);
        if (!result.Succeeded)
        {
            TempData["Flash"] = result.Problems[0].Code;
            return Redirect(Route);
        }

        return Redirect(SessionRoutes.Player(result.Value));
    }

    /// <summary>Opens the goal's unfinished plan session or starts today's next one (PLN-02).</summary>
    public async Task<IActionResult> OnPostPlanAsync(long goalId, CancellationToken cancellationToken)
    {
        var result = await plans.StartAsync(goalId, cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            TempData["Flash"] = result.Problems[0].Code;
            return Redirect(Route);
        }

        return Redirect(SessionRoutes.Player(result.Value));
    }
}
