using Akusiap.Application.Planning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Learn.Pages.Goals;

/// <summary>
/// <c>/learn/target/{id}</c> (PLN-04, stories M-11, H-04): one goal's progress. Answered and mastered questions overall,
/// whether the learner is on schedule, and every topic with its shares, weakest first; then the SKD subtests and the
/// learning plan from the analysis (ANL-02). Other users' goals answer 404.
/// </summary>
public sealed class DetailModel(StudyPlans plans, LearningAnalysis analysis) : PageModel
{
    public GoalProgressView Progress { get; private set; } = null!;

    /// <summary>Subtests and the learning plan for the goal's exam (ANL-02); null when the pool is empty.</summary>
    public ExamAnalysis? Analysis { get; private set; }

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var result = await plans.ProgressAsync(id, cancellationToken);
        if (!result.Succeeded)
        {
            return NotFound();
        }

        Progress = result.Value!;
        Analysis = await analysis.ForExamAsync(Progress.Plan.Goal.ExamId, cancellationToken);
        return Page();
    }
}
