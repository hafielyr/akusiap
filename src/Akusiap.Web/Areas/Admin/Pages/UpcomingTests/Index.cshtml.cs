using Akusiap.Application.UpcomingTests;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Admin.Pages.UpcomingTests;

/// <summary><c>/admin/jadwal-tes</c> (CPNS-01): every upcoming test, published or not, soonest first.</summary>
public sealed class IndexModel(ManageUpcomingTests manage) : PageModel
{
    public IReadOnlyList<UpcomingTestAdminRow> Tests { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Tests = await manage.ListAsync(cancellationToken);
}
