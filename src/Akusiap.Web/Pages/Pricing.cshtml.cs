using Akusiap.Application.Catalog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages;

/// <summary><c>/harga</c> (CAT-03): a single exam next to the Learner packages. <c>?ujian={id}</c> gives the exam context.</summary>
public sealed class PricingModel(GetPricing getPricing) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "ujian")]
    public long? ExamId { get; set; }

    public Pricing Pricing { get; private set; } = new(null, null, null, []);

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Pricing = await getPricing.HandleAsync(ExamId, cancellationToken);
}
