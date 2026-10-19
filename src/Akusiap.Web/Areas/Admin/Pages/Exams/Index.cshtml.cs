using Akusiap.Application.Exams;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Admin.Pages.Exams;

public sealed class IndexModel(ListExams listExams) : PageModel
{
    public IReadOnlyList<ExamRow> Exams { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) => Exams = await listExams.HandleAsync(cancellationToken);
}
