using Akusiap.Application.Catalog;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages.Exams;

/// <summary>
/// The exam catalog at <c>/ujian</c> (CAT-01). A plain GET form, so search works without JavaScript; htmx requests get
/// only the results fragment.
/// </summary>
public sealed class IndexModel(ListCatalog listCatalog) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "subjek")]
    public long? SubjectId { get; set; }

    [BindProperty(SupportsGet = true, Name = "hal")]
    public int PageNumber { get; set; } = 1;

    public CatalogPage Results { get; private set; } = new([], 1, ListCatalog.PageSize, 0);

    public IReadOnlyList<CatalogSubject> Subjects { get; private set; } = [];

    public bool IsFiltered => !string.IsNullOrWhiteSpace(Search) || SubjectId is not null;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Results = await listCatalog.HandleAsync(new CatalogFilter(Search, SubjectId, PageNumber), cancellationToken);
        var isHtmx = Request.Headers.ContainsKey("HX-Request") && !Request.Headers.ContainsKey("HX-History-Restore-Request");
        if (isHtmx)
        {
            return Partial("_CatalogResultsPartial", this);
        }

        Subjects = await listCatalog.SubjectsAsync(cancellationToken);
        return Page();
    }

    /// <summary>The catalog URL for another page, keeping the current filters.</summary>
    public string PageUrl(int page)
    {
        var query = new QueryBuilder();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            query.Add("q", Search.Trim());
        }

        if (SubjectId is { } subject)
        {
            query.Add("subjek", subject.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (page > 1)
        {
            query.Add("hal", page.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return "/ujian" + query.ToQueryString();
    }
}
