using Akusiap.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;

namespace Akusiap.Web.Pages.Checkout;

/// <summary>
/// <c>/bayar/{orderNumber}</c> (PAY-05): how to pay (QR, VA number with steps, or app link), a countdown to the deadline,
/// and the result once the gateway confirms. Only the payer can open it. The status is polled with htmx every 5 seconds
/// (<c>?handler=Status</c>); when it changes the page reloads (<c>HX-Refresh</c>).
/// </summary>
[Authorize]
public sealed class PayModel(GetPayments getPayments) : PageModel
{
    public PaymentView Payment { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string orderNumber, CancellationToken cancellationToken)
    {
        var payment = await getPayments.ByOrderNumberAsync(orderNumber, cancellationToken);
        if (payment is null)
        {
            return NotFound();
        }

        Payment = payment;
        return Page();
    }

    /// <summary>The polled status: 204 while nothing changed, or an <c>HX-Refresh</c> so the page shows the new state.</summary>
    public async Task<IActionResult> OnGetStatusAsync(string orderNumber, PaymentDisplayState shown, CancellationToken cancellationToken)
    {
        var payment = await getPayments.ByOrderNumberAsync(orderNumber, cancellationToken);
        if (payment is null)
        {
            return NotFound();
        }

        if (payment.State != shown)
        {
            Response.Headers["HX-Refresh"] = "true";
        }

        return new NoContentResult();
    }

    /// <summary>The QR code as an inline SVG (no image request, scales to any screen).</summary>
    public static string QrSvg(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(new System.Drawing.Size(240, 240), "#000000", "#ffffff", drawQuietZones: true, sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);
    }
}
