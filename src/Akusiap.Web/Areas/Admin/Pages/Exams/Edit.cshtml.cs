using Akusiap.Application.Exams;
using Akusiap.Application.RulePresets;
using Akusiap.Application.Subjects;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Admin.Pages.Exams;

/// <summary>Edit an app exam (status unchanged) or delete one that was never used.</summary>
public sealed class EditModel(
    GetExam getExam,
    SaveExam saveExam,
    DeleteExam deleteExam,
    ListSubjects listSubjects,
    ListRulePresetOptions listPresets,
    IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty]
    public ExamInput Input { get; set; } = new();

    public ExamDetail Exam { get; private set; } = null!;

    public ExamFormView Form { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        Input = ExamInput.From(Exam);
        return await ShowAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        var (request, priceProblem) = Input.ToRequest(Id);
        var result = priceProblem is null ? await saveExam.HandleAsync(request, cancellationToken) : null;
        if (result is { IsNotFound: true })
        {
            return NotFound();
        }

        if (result is { Succeeded: true })
        {
            TempData["Flash"] = "Admin.Saved";
            return Redirect($"/admin/ujian/{Id}");
        }

        ModelState.AddProblems(priceProblem is null ? result!.Problems : [priceProblem], t, nameof(Input));
        return await LoadAsync(cancellationToken) ? await ShowAsync(cancellationToken) : NotFound();
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        var result = await deleteExam.HandleAsync(Id, cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            ModelState.AddProblems(result.Problems, t, nameof(Input));
            if (!await LoadAsync(cancellationToken))
            {
                return NotFound();
            }

            Input = ExamInput.From(Exam);
            return await ShowAsync(cancellationToken);
        }

        TempData["Flash"] = "Admin.Deleted";
        return Redirect("/admin/ujian");
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var exam = await getExam.HandleAsync(Id, cancellationToken);
        if (exam is null)
        {
            return false;
        }

        Exam = exam;
        return true;
    }

    private async Task<PageResult> ShowAsync(CancellationToken cancellationToken)
    {
        Form = new ExamFormView(Input, await listSubjects.OptionsAsync(cancellationToken), await listPresets.HandleAsync(cancellationToken));
        return Page();
    }
}
