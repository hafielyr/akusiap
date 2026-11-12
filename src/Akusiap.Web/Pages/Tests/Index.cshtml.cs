using Akusiap.Application.UpcomingTests;
using Akusiap.Domain.Content;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages.Tests;

/// <summary>
/// <c>/jadwal-tes</c> (CPNS-01): upcoming hiring tests, soonest first, with search and category chips. A plain GET form,
/// so it works without JavaScript; htmx requests get only the list.
/// </summary>
public sealed class IndexModel(UpcomingTests upcomingTests) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "kategori")]
    public UpcomingTestCategory? Category { get; set; }

    public IReadOnlyList<UpcomingTestItem> Tests { get; private set; } = [];

    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || Category is not null;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Tests = await upcomingTests.ListAsync(Search, Category, cancellationToken);
        var isHtmx = Request.Headers.ContainsKey("HX-Request") && !Request.Headers.ContainsKey("HX-History-Restore-Request");
        return isHtmx ? Partial("_TestListPartial", this) : Page();
    }
}
