using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Payments;

/// <summary>What a payment pays for: exactly one of an exam purchase or a subscription row.</summary>
public sealed record PaymentItem(long? ExamPurchaseId, long? SubscriptionId, string Name)
{
    public static PaymentItem ForExamPurchase(long id, string name) => new(id, null, name);

    public static PaymentItem ForSubscription(long id, string name) => new(null, id, name);
}

public sealed record CreatePaymentRequest(Guid UserId, PaymentItem Item, long AmountIdr, PaymentMethod Method, string? Channel);

/// <summary>
/// Opens a Pending payment and asks the gateway for instructions (PAY-01; checkout in PAY-03 builds on it). The order
/// number is unique and readable (<see cref="OrderNumbers"/>) and doubles as the gateway invoice number (D-52). The
/// gateway sets the pay-before deadline. If the gateway call fails, the payment is kept as Failed with the reason.
/// </summary>
public sealed class CreatePayment(IAppDbContext db, IPaymentGateway gateway, IClock clock, IRandomSource random)
{
    public const string PendingExists = "Payment.PendingExists";
    public const string GatewayFailed = "Payment.GatewayFailed";
    private const int OrderNumberAttempts = 5;

    public async Task<Result<Payment>> HandleAsync(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.Item.ExamPurchaseId is null) == (request.Item.SubscriptionId is null) || request.AmountIdr < 0)
        {
            throw new ArgumentException("A payment pays for exactly one item, with a non-negative amount.", nameof(request));
        }

        var pending = await db.Payments.AnyAsync(
            p => p.Status == PaymentStatus.Pending
                && ((request.Item.ExamPurchaseId != null && p.ExamPurchaseId == request.Item.ExamPurchaseId)
                    || (request.Item.SubscriptionId != null && p.SubscriptionId == request.Item.SubscriptionId)),
            cancellationToken).ConfigureAwait(false);
        if (pending)
        {
            return Result.Fail<Payment>(new Problem(PendingExists));
        }

        var user = await db.Users.Where(u => u.Id == request.UserId)
            .Select(u => new { u.DisplayName, u.Email })
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var orderNumber = await NewOrderNumberAsync(now, cancellationToken).ConfigureAwait(false);
        var payment = new Payment
        {
            OrderNumber = orderNumber,
            InvoiceNumber = orderNumber,
            UserId = request.UserId,
            ExamPurchaseId = request.Item.ExamPurchaseId,
            SubscriptionId = request.Item.SubscriptionId,
            AmountIdr = request.AmountIdr,
            Gateway = gateway.Name,
            Method = request.Method,
            Channel = request.Channel,
            CreatedAt = now,
        };

        try
        {
            var instructions = await gateway.CreateChargeAsync(
                new ChargeRequest(orderNumber, request.AmountIdr, request.Method, request.Channel, request.Item.Name, user.DisplayName, user.Email!),
                cancellationToken).ConfigureAwait(false);
            payment.GatewayRequestId = instructions.GatewayRequestId;
            payment.QrString = instructions.QrString;
            payment.VaNumber = instructions.VaNumber;
            payment.DeeplinkUrl = instructions.DeeplinkUrl;
            payment.ExpiresAt = instructions.ExpiresAt;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TimeoutException)
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureReason = ex.Message;
        }

        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return payment.Status == PaymentStatus.Failed
            ? Result.Fail<Payment>(new Problem(GatewayFailed))
            : Result.Ok(payment);
    }

    private async Task<string> NewOrderNumberAsync(DateTime now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < OrderNumberAttempts; attempt++)
        {
            var candidate = OrderNumbers.Create(now, random.Random);
            if (!await db.Payments.AnyAsync(p => p.OrderNumber == candidate, cancellationToken).ConfigureAwait(false))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not find a free order number.");
    }
}
