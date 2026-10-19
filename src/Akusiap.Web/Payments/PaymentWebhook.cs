using Akusiap.Application.Abstractions;
using Akusiap.Application.Payments;

namespace Akusiap.Web.Payments;

/// <summary>
/// <c>POST /webhooks/doku</c> (PAY-06): the gateway's payment notifications. The raw body and headers go to
/// <see cref="ApplyGatewayNotification"/>, which stores the event, checks the signature and applies it once. Answers 200
/// for every readable notification (so the gateway stops retrying), 400 for an unreadable one, and 503 while no gateway
/// is configured.
/// </summary>
public static class PaymentWebhook
{
    public const string Route = "/webhooks/doku";
    public const int MaxBodyBytes = 64 * 1024;

    public static async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.RequestServices.GetServices<IPaymentGateway>().Any())
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        using var reader = new StreamReader(context.Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        var headers = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var apply = context.RequestServices.GetRequiredService<ApplyGatewayNotification>();
        var (outcome, _) = await apply.HandleAsync(new GatewayNotification(headers, body), cancellationToken);
        return outcome == NotificationOutcome.Unreadable ? Results.BadRequest() : Results.Ok();
    }
}
