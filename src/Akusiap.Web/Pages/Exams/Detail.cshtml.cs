using Akusiap.Application.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages.Exams;

/// <summary>
/// An exam's page at <c>/ujian/{id}</c> (CAT-02): description, topics with counts, price and the buttons that fit the
/// viewer's access decision. Unpublished, taken-down and non-app exams answer 404.
/// </summary>
public sealed class DetailModel(GetCatalogExam getExam) : PageModel
{
    public CatalogExamDetail Exam { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var exam = await getExam.HandleAsync(id, cancellationToken);
        if (exam is null)
        {
            return NotFound();
        }

        Exam = exam;
        return Page();
    }
}
