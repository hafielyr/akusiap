using Akusiap.Application.Gamification;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages.Exams;

/// <summary><c>/ujian/{id}/peringkat</c> (GAM-01): the public leaderboard of a catalog exam.</summary>
public sealed class LeaderboardModel(Leaderboards leaderboards) : PageModel
{
    public LeaderboardView Board { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        if (await leaderboards.ForExamAsync(id, cancellationToken) is not { } board)
        {
            return NotFound();
        }

        Board = board;
        return Page();
    }
}
