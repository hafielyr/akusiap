using Akusiap.Application.Abstractions;
using Akusiap.Application.Planning;
using Akusiap.Domain;
using Akusiap.Domain.Planning;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Learn.Pages.Goals;

/// <summary>
/// <c>/learn/target</c> (PLN-01, D-120): the learner's exam goals with their countdown, and a form to set or move the
/// date of an exam. <c>?examId=</c> preselects an exam (from the exam page).
/// </summary>
public sealed class IndexModel(StudyGoals goals, IClock clock, IStringLocalizer<SharedResource> t) : PageModel
{
    public const string Route = "/learn/target";
    public const string DateRequired = "Goals.DateRequired";

    [BindProperty]
    public GoalInput Input { get; set; } = new();

    public IReadOnlyList<StudyGoalView> Goals { get; private set; } = [];

    public IReadOnlyList<GoalExamOption> Exams { get; private set; } = [];

    /// <summary>Earliest and latest dates the form accepts (Jakarta).</summary>
    public DateOnly MinDate { get; private set; }

    public DateOnly MaxDate { get; private set; }

    public async Task OnGetAsync(long? examId, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        if (examId is { } id && Exams.Any(e => e.ExamId == id))
        {
            Input.ExamId = id;
            Input.ExamDate = Goals.FirstOrDefault(g => g.ExamId == id)?.ExamDate;
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var dateKey = $"{nameof(Input)}.{nameof(GoalInput.ExamDate)}";
        if (Input.ExamDate is not { } date)
        {
            ModelState.Remove(dateKey);
            ModelState.AddModelError(dateKey, t[DateRequired]);
            await LoadAsync(cancellationToken);
            return Page();
        }

        var result = await goals.SetAsync(Input.ExamId, date, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddProblems(result.Problems, t, nameof(Input));
            await LoadAsync(cancellationToken);
            return Page();
        }

        TempData["Flash"] = "Goals.Saved";
        return Redirect(Route);
    }

    public async Task<IActionResult> OnPostRemoveAsync(long id, CancellationToken cancellationToken)
    {
        var result = await goals.ArchiveAsync(id, cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        TempData["Flash"] = "Goals.Removed";
        return Redirect(Route);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Goals = await goals.ActiveAsync(cancellationToken);
        Exams = await goals.OptionsAsync(cancellationToken);
        var today = JakartaTime.Today(clock.UtcNow);
        MinDate = today.AddDays(1);
        MaxDate = today.AddYears(StudyGoal.MaxYearsAhead);
    }
}

public sealed class GoalInput
{
    public long ExamId { get; set; }

    public DateOnly? ExamDate { get; set; }
}
