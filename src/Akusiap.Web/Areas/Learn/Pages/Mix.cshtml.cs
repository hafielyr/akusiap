using Akusiap.Application.OwnExams;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Akusiap.Web.Pages.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Learn.Pages;

/// <summary><c>/learn/campuran</c> (OWN-06): pick own questions, whole exams or single topics, then start a mixed set.</summary>
public sealed class MixModel(MixedPractice mix, IStringLocalizer<SharedResource> t) : PageModel
{
    public const string Route = "/learn/campuran";

    [BindProperty]
    public bool IncludeOwn { get; set; }

    [BindProperty]
    public List<long> ExamIds { get; set; } = [];

    [BindProperty]
    public List<string> TopicKeys { get; set; } = [];

    [BindProperty]
    public int QuestionCount { get; set; } = MixedPractice.DefaultCount;

    public MixOptions Options { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Options = await mix.OptionsAsync(cancellationToken);
        IncludeOwn = Options.OwnQuestionCount > 0;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await mix.StartAsync(new StartMixRequest(IncludeOwn, ExamIds, TopicKeys, QuestionCount), cancellationToken);
        if (!result.Succeeded)
        {
            foreach (var problem in result.Problems)
            {
                ModelState.AddModelError(problem.Field ?? string.Empty, ProblemModelState.Describe(problem, t));
            }

            Options = await mix.OptionsAsync(cancellationToken);
            return Page();
        }

        return Redirect(SessionRoutes.Player(result.Value!.AttemptId));
    }
}
