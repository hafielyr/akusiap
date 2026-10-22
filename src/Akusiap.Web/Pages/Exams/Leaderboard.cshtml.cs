using Akusiap.Application.Gamification;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages.Exams;

/// <summary>
/// <c>/ujian/{id}/peringkat</c> (GAM-01, DRL-02): the public leaderboard of a catalog exam. Exams that reset weekly open
/// on this week's board, with <c>?periode=semua</c> for all time. A board that is switched off is not found.
/// </summary>
public sealed class LeaderboardModel(Leaderboards leaderboards) : PageModel
{
    public const string AllTimeValue = "semua";

    [BindProperty(SupportsGet = true, Name = "periode")]
    public string? PeriodText { get; set; }

    public LeaderboardView Board { get; private set; } = null!;

    private LeaderboardPeriod Period => PeriodText == AllTimeValue ? LeaderboardPeriod.AllTime : LeaderboardPeriod.ThisWeek;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        if (await leaderboards.ForExamAsync(id, Period, cancellationToken) is not { } board)
        {
            return NotFound();
        }

        Board = board;
        return Page();
    }
}
