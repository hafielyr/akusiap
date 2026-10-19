using Akusiap.Application.Abstractions;
using Akusiap.Application.Payments;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Pages.Checkout;

/// <summary>Payment-related URLs. The instructions page is PAY-05; the legal pages are AC-05.</summary>
public static class PaymentRoutes
{
    public static string Instructions(string orderNumber) => $"/bayar/{Uri.EscapeDataString(orderNumber)}";

    public const string Terms = "/syarat-layanan";
    public const string RefundPolicy = "/kebijakan-refund";
}

/// <summary>
/// <c>/checkout?exam={id}</c> or <c>?planPrice={id}</c> (PAY-03): summary, access end date, price incl. tax, channel list
/// and the terms checkbox. Signed-in users only; an unconfirmed email is asked to be confirmed first. Placing the order
/// sends the learner to the payment instructions (PAY-05).
/// </summary>
[Authorize]
public sealed class IndexModel(Application.Payments.Checkout checkout, IEnumerable<IPaymentGateway> gateways, IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "exam")]
    public long? ExamId { get; set; }

    [BindProperty(SupportsGet = true, Name = "planPrice")]
    public long? PlanPriceId { get; set; }

    [BindProperty]
    public string? Channel { get; set; }

    [BindProperty]
    public bool AcceptTerms { get; set; }

    public CheckoutSummary Summary { get; private set; } = null!;

    public IReadOnlyList<PaymentChannelSetting> Channels { get; private set; } = [];

    public bool EmailConfirmed { get; private set; }

    public bool PaymentsAvailable => gateways.Any();

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        if (!PaymentsAvailable)
        {
            Errors = [t["Checkout.Unavailable"].Value];
            return Page();
        }

        var createPayment = HttpContext.RequestServices.GetRequiredService<CreatePayment>();
        var result = await checkout.PlaceOrderAsync(new CheckoutItemRef(ExamId, PlanPriceId), Channel, AcceptTerms, createPayment, cancellationToken);
        if (result.Succeeded)
        {
            return Redirect(PaymentRoutes.Instructions(result.Value!));
        }

        if (result.IsNotFound)
        {
            return NotFound();
        }

        Errors = [.. result.Problems.Select(p => ProblemModelState.Describe(p, t))];
        return Page();
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var summary = await checkout.SummaryAsync(new CheckoutItemRef(ExamId, PlanPriceId), cancellationToken);
        if (summary is null)
        {
            return false;
        }

        Summary = summary;
        Channels = checkout.ChannelsFor(summary.PriceIdr);
        EmailConfirmed = await checkout.IsEmailConfirmedAsync(cancellationToken);
        return true;
    }
}
