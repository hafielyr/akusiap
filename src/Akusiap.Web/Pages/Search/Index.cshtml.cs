using Akusiap.Application.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages.Search;

/// <summary>
/// <c>/cari</c> (BRD-04): search questions and exams in the catalog. A plain GET form, so it works without JavaScript;
/// htmx requests (typing in the box) get only the results fragment.
/// </summary>
public sealed class IndexModel(SearchCatalog search) : PageModel
{
    public const string Route = "/cari";

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Query { get; set; }

    /// <summary>Null when nothing (or too little) was typed.</summary>
    public SearchResults? Results { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Results = await search.HandleAsync(Query, cancellationToken);
        var isHtmx = Request.Headers.ContainsKey("HX-Request") && !Request.Headers.ContainsKey("HX-History-Restore-Request");
        return isHtmx ? Partial("_SearchResultsPartial", this) : Page();
    }

    public static string For(string query) => $"{Route}?q={Uri.EscapeDataString(query)}";
}
