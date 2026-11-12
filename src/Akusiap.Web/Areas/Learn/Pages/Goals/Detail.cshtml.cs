using Akusiap.Application.Planning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Learn.Pages.Goals;

/// <summary>
/// <c>/learn/target/{id}</c> (PLN-04, stories M-11, H-04): one goal's progress. Answered and mastered questions overall,
/// whether the learner is on schedule, and every topic with its shares, weakest first. Other users' goals answer 404.
/// </summary>
public sealed class DetailModel(StudyPlans plans) : PageModel
{
    public GoalProgressView Progress { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var result = await plans.ProgressAsync(id, cancellationToken);
        if (!result.Succeeded)
        {
            return NotFound();
        }

        Progress = result.Value!;
        return Page();
    }
}
