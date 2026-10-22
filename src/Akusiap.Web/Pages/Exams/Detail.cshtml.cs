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
    Akusiap.Application.Gamification.Leaderboards leaderboards) : PageModel
{
    public CatalogExamDetail Exam { get; private set; } = null!;

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
        return Page();
    }
}
