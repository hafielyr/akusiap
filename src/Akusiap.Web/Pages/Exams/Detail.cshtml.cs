using Akusiap.Application.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages.Exams;

/// <summary>
/// An exam's page at <c>/ujian/{id}</c> (CAT-02): description, topics with counts, price and the buttons that fit the
/// viewer's access decision. Unpublished, taken-down and non-app exams answer 404.
/// </summary>
public sealed class DetailModel(
    GetCatalogExam getExam,
    Akusiap.Application.Sessions.FindOpenAttempt findOpen,
    Akusiap.Application.Gamification.Leaderboards leaderboards,
    Akusiap.Application.Planning.StudyGoals goals,
    Akusiap.Application.UpcomingTests.UpcomingTests upcomingTests) : PageModel
{
    /// <summary>The upcoming test this exam prepares for, if one links to it (CPNS-02).</summary>
    public Akusiap.Application.UpcomingTests.UpcomingTestItem? Test { get; private set; }

    public CatalogExamDetail Exam { get; private set; } = null!;

    /// <summary>Signed-in viewers who can start a published exam can set a goal for it (PLN-01).</summary>
    public bool CanSetGoal { get; private set; }

    /// <summary>The viewer's active goal for this exam, if any (PLN-01).</summary>
    public Akusiap.Application.Planning.StudyGoalView? Goal { get; private set; }

    /// <summary>An unfinished attempt of this viewer on this exam (SES-04 "continue" banner).</summary>
    public long? OpenAttemptId { get; private set; }

    /// <summary>Whether the exam's leaderboard is on (DRL-02).</summary>
    public bool LeaderboardEnabled { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var exam = await getExam.HandleAsync(id, cancellationToken);
        if (exam is null)
        {
            return NotFound();
        }

        Exam = exam;
        var guestToken = User.Identity?.IsAuthenticated == true ? null : Akusiap.Web.Sessions.GuestTokenCookie.Read(Request);
        OpenAttemptId = await findOpen.HandleAsync(id, guestToken, cancellationToken);
        LeaderboardEnabled = await leaderboards.IsEnabledAsync(id, cancellationToken);
        Test = await upcomingTests.ForExamAsync(id, cancellationToken);
        CanSetGoal = User.Identity?.IsAuthenticated == true && exam.Status == Akusiap.Domain.Content.ExamStatus.Published && exam.Decision.CanStart;
        if (CanSetGoal)
        {
            Goal = await goals.ForExamAsync(id, cancellationToken);
        }

        return Page();
    }
}
