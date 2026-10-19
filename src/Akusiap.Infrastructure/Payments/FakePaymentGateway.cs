using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Domain.Payments;

namespace Akusiap.Infrastructure.Payments;

/// <summary>
/// A stand-in gateway for Development and tests (PAY-01). Charges succeed at once with made-up instructions; notifications
/// are JSON signed with HMAC-SHA256 in the <see cref="SignatureHeader"/> header, using a fixed development key that is
/// not a secret. <see cref="CreateNotification"/> builds them, so the dev page can play "the gateway says paid".
/// </summary>
public sealed class FakePaymentGateway(IClock clock) : IPaymentGateway
{
    public const string SignatureHeader = "X-Fake-Signature";
    public static readonly TimeSpan PayWithin = TimeSpan.FromHours(24);
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("akusiap-fake-gateway-development-key");

    public string Name => PaymentGateways.Doku;

    public Task<ChargeInstructions> CreateChargeAsync(ChargeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var digits = string.Concat(SHA256.HashData(Encoding.UTF8.GetBytes(request.InvoiceNumber)).Take(6).Select(b => (b % 100).ToString("00", CultureInfo.InvariantCulture)));
        var instructions = new ChargeInstructions(
            GatewayRequestId: Guid.NewGuid().ToString(),
            QrString: request.Method == PaymentMethod.QRIS ? $"FAKE-QRIS|{request.InvoiceNumber}|{request.AmountIdr}" : null,
            VaNumber: request.Method == PaymentMethod.VirtualAccount ? "8808" + digits : null,
            DeeplinkUrl: request.Method is PaymentMethod.EWallet or PaymentMethod.CreditCard ? $"/dev/pembayaran?invoice={request.InvoiceNumber}" : null,
            ExpiresAt: clock.UtcNow + PayWithin);
        return Task.FromResult(instructions);
    }

    public bool VerifySignature(GatewayNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var header = notification.Headers.FirstOrDefault(h => string.Equals(h.Key, SignatureHeader, StringComparison.OrdinalIgnoreCase)).Value;
        if (string.IsNullOrEmpty(header))
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Sign(notification.Body));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(header));
    }

    public GatewayEvent? ParseNotification(GatewayNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        try
        {
            using var json = JsonDocument.Parse(notification.Body);
            var root = json.RootElement;
            var kind = root.GetProperty("status").GetString() switch
            {
                "PAID" => GatewayEventKind.Paid,
                "EXPIRED" => GatewayEventKind.Expired,
                "FAILED" => GatewayEventKind.Failed,
                _ => GatewayEventKind.Other,
            };
            return new GatewayEvent(
                root.GetProperty("eventId").GetString()!,
                root.GetProperty("invoiceNumber").GetString()!,
                kind,
                root.TryGetProperty("amount", out var amount) ? amount.GetInt64() : null,
                root.TryGetProperty("reason", out var reason) ? reason.GetString() : null);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new GatewayRefundResult(true, "FAKE-REFUND-" + Guid.NewGuid().ToString("N")[..8], null, null));

    /// <summary>A signed notification such as the real gateway would send.</summary>
    public static GatewayNotification CreateNotification(string invoiceNumber, string status, long amount, string? eventId = null)
    {
        var body = JsonSerializer.Serialize(new { eventId = eventId ?? Guid.NewGuid().ToString(), invoiceNumber, status, amount });
        return new GatewayNotification(new Dictionary<string, string> { [SignatureHeader] = Sign(body) }, body);
    }

    private static string Sign(string body) => Convert.ToHexStringLower(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(body)));
}
