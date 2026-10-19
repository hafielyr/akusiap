using Akusiap.Application.Payments;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Admin.Pages.Transactions;

/// <summary><c>/admin/transaksi/{id}</c> (PAY-10): one payment with its gateway notifications and refunds. Read only.</summary>
public sealed class DetailModel(AdminPayments payments) : PageModel
{
    public AdminPaymentDetail Detail { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var detail = await payments.DetailAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        Detail = detail;
        return Page();
    }
}
