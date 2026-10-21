using Akusiap.Application.Gamification;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Learn.Pages.OwnExams;

/// <summary><c>/learn/ujian-saya/{id}/peserta</c> (GAM-01, story C-09): who took the owner's exam, best result first.</summary>
public sealed class ParticipantsModel(Leaderboards leaderboards, Application.OwnExams.OwnExams own) : PageModel
{
    public IReadOnlyList<ParticipantRow> Rows { get; private set; } = [];

    public string Title { get; private set; } = string.Empty;

    public long ExamId { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        if (await own.GetAsync(id, cancellationToken) is not { } exam || await leaderboards.ParticipantsAsync(id, cancellationToken) is not { } rows)
        {
            return NotFound();
        }

        (ExamId, Title, Rows) = (id, exam.Title, rows);
        return Page();
    }
}
