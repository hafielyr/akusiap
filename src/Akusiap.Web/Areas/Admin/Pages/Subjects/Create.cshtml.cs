using Akusiap.Application.Subjects;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Admin.Pages.Subjects;

public sealed class CreateModel(SaveSubject saveSubject, IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty]
    public SubjectInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await saveSubject.HandleAsync(new SaveSubjectRequest(null, Input.Name, Input.Description), cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddProblems(result.Problems, t, nameof(Input));
            return Page();
        }

        TempData["Flash"] = "Admin.Saved";
        return Redirect($"/admin/subjek/{result.Value}");
    }
}

/// <summary>Subject form fields. Rules live in <see cref="SubjectRules"/>.</summary>
public sealed class SubjectInput
{
    public string? Name { get; set; }

    public string? Description { get; set; }
}
