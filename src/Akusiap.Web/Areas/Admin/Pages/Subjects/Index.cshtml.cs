using Akusiap.Application.Subjects;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Admin.Pages.Subjects;

public sealed class IndexModel(ListSubjects listSubjects) : PageModel
{
    public IReadOnlyList<SubjectSummary> Subjects { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Subjects = await listSubjects.HandleAsync(cancellationToken);
}
