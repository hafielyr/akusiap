using Akusiap.Application.Exams;
using Akusiap.Application.RulePresets;
using Akusiap.Application.Subjects;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Admin.Pages.Exams;

public sealed class CreateModel(
    SaveExam saveExam,
    ListSubjects listSubjects,
    ListRulePresetOptions listPresets,
    IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty]
    public ExamInput Input { get; set; } = new();

    public ExamFormView Form { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var presets = await listPresets.HandleAsync(cancellationToken);
        Input = ExamInput.Blank(presets);
        Form = new ExamFormView(Input, await listSubjects.OptionsAsync(cancellationToken), presets);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        // The use case is the single source of messages; drop model-binding errors (for example "abc" in a number field).
        ModelState.Clear();
        var (request, priceProblem) = Input.ToRequest(null);
        var result = priceProblem is null ? await saveExam.HandleAsync(request, cancellationToken) : null;
        if (result is { Succeeded: true })
        {
            TempData["Flash"] = "Admin.Exams.Created";
            return Redirect($"/admin/ujian/{result.Value}");
        }

        ModelState.AddProblems(priceProblem is null ? result!.Problems : [priceProblem], t, nameof(Input));
        Form = new ExamFormView(Input, await listSubjects.OptionsAsync(cancellationToken), await listPresets.HandleAsync(cancellationToken));
        return Page();
    }
}
