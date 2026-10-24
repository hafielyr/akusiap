using Akusiap.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace Akusiap.Web.Pages.Checkout;

/// <summary>
/// <c>/bayar/{orderNumber}</c> (PAY-05): how to pay (QR, VA number with steps, or app link), a countdown to the deadline,
/// and the result once the gateway confirms. Only the payer can open it. The status is polled with htmx every 5 seconds
/// (<c>?handler=Status</c>); when it changes the page reloads (<c>HX-Refresh</c>). While Pending, opening the page and the
/// poll also ask the gateway itself, at most every 30 seconds (<see cref="CheckPaymentStatus"/>), in case its
/// notification never arrives.
/// </summary>
[Authorize]
public sealed partial class PayModel(GetPayments getPayments, CheckPaymentStatus checkStatus, ILogger<PayModel> logger) : PageModel
{
    public PaymentView Payment { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string orderNumber, CancellationToken cancellationToken)
    {
        var payment = await getPayments.ByOrderNumberAsync(orderNumber, cancellationToken);
        if (payment is null)
        {
            return NotFound();
        }

        if (await CheckedAsync(payment, cancellationToken))
        {
            payment = await getPayments.ByOrderNumberAsync(orderNumber, cancellationToken) ?? payment;
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

        if (payment.State != shown || await CheckedAsync(payment, cancellationToken))
        {
            Response.Headers["HX-Refresh"] = "true";
        }

        return new NoContentResult();
    }

    /// <summary>Asks the gateway while the payment is Pending; true when its answer changed the payment. Errors only log.</summary>
    private async Task<bool> CheckedAsync(PaymentView payment, CancellationToken cancellationToken)
    {
        if (payment.State != PaymentDisplayState.Pending)
        {
            return false;
        }

        try
        {
            return await checkStatus.HandleAsync(payment.Id, cancellationToken);
        }
        catch (Exception ex) when ((ex is HttpRequestException or InvalidOperationException or TaskCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            LogCheckFailed(logger, payment.OrderNumber, ex);
            return false;
        }
    }

    /// <summary>The QR code as an inline SVG (no image request, scales to any screen).</summary>
    public static string QrSvg(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(new System.Drawing.Size(240, 240), "#000000", "#ffffff", drawQuietZones: true, sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not check the gateway status of {OrderNumber}.")]
    private static partial void LogCheckFailed(ILogger logger, string orderNumber, Exception exception);
}
