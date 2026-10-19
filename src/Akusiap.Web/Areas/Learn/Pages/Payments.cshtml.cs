using Akusiap.Application.Payments;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Learn.Pages;

/// <summary><c>/learn/pembayaran</c> (PAY-05, story ID-13): payments still waiting, with a link back to their instructions.</summary>
public sealed class PaymentsModel(GetPayments getPayments) : PageModel
{
    public IReadOnlyList<PaymentView> Pending { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Pending = await getPayments.PendingAsync(cancellationToken);
}
