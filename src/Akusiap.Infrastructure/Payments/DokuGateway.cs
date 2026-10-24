using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Akusiap.Application.Abstractions;
using Akusiap.Domain.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Akusiap.Infrastructure.Payments;

/// <summary>
/// DOKU (PAY-04, D-110), non-SNAP API with the Client-Id/Secret-Key signature (<see cref="DokuSignature"/>).
/// Virtual accounts (BCA, BNI, BRI, Mandiri) use the Direct API, so our payment page shows the number and countdown.
/// Card, e-wallets and QRIS use DOKU Checkout limited to the one method the payer chose: the payer opens DOKU's hosted
/// page (card data never reaches us) and comes back to <c>/bayar/{order}</c>. Every notification is verified with the
/// same signature scheme (our notification path as the target) before anything trusts it.
/// </summary>
public sealed partial class DokuGateway(HttpClient http, IOptions<DokuOptions> options, IClock clock, ILogger<DokuGateway> logger) : IPaymentGateway
{
    public const string CheckoutPath = "/checkout/v1/payment";

    private static readonly Dictionary<string, string> WalletTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OVO"] = "EMONEY_OVO",
        ["DANA"] = "EMONEY_DANA",
        ["SHOPEEPAY"] = "EMONEY_SHOPEE_PAY",
        ["LINKAJA"] = "EMONEY_LINKAJA",
    };

    private static readonly HashSet<string> Banks = new(StringComparer.OrdinalIgnoreCase) { "BCA", "BNI", "BRI", "MANDIRI" };

    private DokuOptions Options => options.Value;

    public string Name => PaymentGateways.Doku;

    public async Task<ChargeInstructions> CreateChargeAsync(ChargeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Method == PaymentMethod.VirtualAccount
            ? await VirtualAccountAsync(request, cancellationToken).ConfigureAwait(false)
            : await HostedPageAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public bool VerifySignature(GatewayNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var clientId = Header(notification, "Client-Id");
        var requestId = Header(notification, "Request-Id");
        var timestamp = Header(notification, "Request-Timestamp");
        var signature = Header(notification, "Signature");
        if (clientId is null || requestId is null || timestamp is null || signature is null || !string.Equals(clientId, Options.ClientId, StringComparison.Ordinal))
        {
            return false;
        }

        var expected = DokuSignature.Sign(Options.SecretKey, clientId, requestId, timestamp, Options.NotificationPath, notification.Body);
        return DokuSignature.Matches(expected, signature);
    }

    public GatewayEvent? ParseNotification(GatewayNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var requestId = Header(notification, "Request-Id");
        if (requestId is null)
        {
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(notification.Body);
            var root = json.RootElement;
            var order = root.GetProperty("order");
            var invoice = order.GetProperty("invoice_number").GetString();
            if (string.IsNullOrEmpty(invoice))
            {
                return null;
            }

            var status = root.TryGetProperty("transaction", out var tx) && tx.TryGetProperty("status", out var s) ? s.GetString() : null;
            var kind = status?.ToUpperInvariant() switch
            {
                "SUCCESS" => GatewayEventKind.Paid,
                "FAILED" => GatewayEventKind.Failed,
                "EXPIRED" => GatewayEventKind.Expired,
                _ => GatewayEventKind.Other,
            };
            return new GatewayEvent(requestId, invoice, kind, Amount(order), kind == GatewayEventKind.Paid ? null : status);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Refunds are not automated yet: PAY-09 waits for the owner's refund policy, and DOKU refunds virtual accounts
    /// outside the API. Refund in the DOKU Back Office.
    /// </summary>
    public Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new GatewayRefundResult(false, null, null, "DOKU refunds are not automated yet; refund in the DOKU Back Office."));

    /// <summary>
    /// A notification in DOKU's format, signed with these options, as DOKU would send it for a virtual account payment.
    /// For local testing without a public webhook URL (owner decision: signed samples).
    /// </summary>
    public static GatewayNotification CreateSampleNotification(DokuOptions options, string invoiceNumber, string status, long amount, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(options);
        var body = new JsonObject
        {
            ["service"] = new JsonObject { ["id"] = "VIRTUAL_ACCOUNT" },
            ["acquirer"] = new JsonObject { ["id"] = "BCA" },
            ["channel"] = new JsonObject { ["id"] = "VIRTUAL_ACCOUNT_BCA" },
            ["order"] = new JsonObject { ["invoice_number"] = invoiceNumber, ["amount"] = amount },
            ["transaction"] = new JsonObject { ["status"] = status, ["date"] = Timestamp(utcNow), ["original_request_id"] = Guid.NewGuid().ToString() },
        }.ToJsonString();
        var requestId = Guid.NewGuid().ToString();
        var timestamp = Timestamp(utcNow);
        return new GatewayNotification(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Client-Id"] = options.ClientId,
                ["Request-Id"] = requestId,
                ["Request-Timestamp"] = timestamp,
                ["Signature"] = DokuSignature.Sign(options.SecretKey, options.ClientId, requestId, timestamp, options.NotificationPath, body),
            },
            body);
    }

    private async Task<ChargeInstructions> VirtualAccountAsync(ChargeRequest request, CancellationToken cancellationToken)
    {
        var bank = request.Channel ?? throw new InvalidOperationException("A virtual account needs a bank.");
        if (!Banks.Contains(bank))
        {
            throw new InvalidOperationException($"DOKU virtual accounts are set up for BCA, BNI, BRI and Mandiri, not {bank}.");
        }

        var info = new JsonObject
        {
            ["expired_time"] = Options.VirtualAccountMinutes,
            ["reusable_status"] = false,
            ["info1"] = Truncate(request.ItemName, 20),
        };
        if (string.Equals(bank, "BNI", StringComparison.OrdinalIgnoreCase))
        {
            info["merchant_unique_reference"] = BniReference(request.InvoiceNumber);
        }

        var body = new JsonObject
        {
            ["order"] = new JsonObject { ["invoice_number"] = request.InvoiceNumber, ["amount"] = request.AmountIdr },
            ["virtual_account_info"] = info,
            ["customer"] = Customer(request),
        };
        var (requestId, json) = await PostAsync($"/{bank.ToLowerInvariant()}-virtual-account/v2/payment-code", body, cancellationToken).ConfigureAwait(false);
        using (json)
        {
            var va = json.RootElement.GetProperty("virtual_account_info");
            return new ChargeInstructions(
                requestId,
                QrString: null,
                VaNumber: va.GetProperty("virtual_account_number").GetString(),
                DeeplinkUrl: null,
                ExpiresAt: ExpiresAt(va, "expired_date_utc", Options.VirtualAccountMinutes));
        }
    }

    private async Task<ChargeInstructions> HostedPageAsync(ChargeRequest request, CancellationToken cancellationToken)
    {
        var type = request.Method switch
        {
            PaymentMethod.CreditCard => "CREDIT_CARD",
            PaymentMethod.QRIS => "QRIS",
            PaymentMethod.EWallet when request.Channel is { } wallet && WalletTypes.TryGetValue(wallet, out var t) => t,
            _ => throw new InvalidOperationException($"DOKU is not set up for {request.Method} {request.Channel}."),
        };
        var order = new JsonObject
        {
            ["amount"] = request.AmountIdr,
            ["invoice_number"] = request.InvoiceNumber,
            ["currency"] = "IDR",
        };
        if (!string.IsNullOrWhiteSpace(Options.ReturnUrl))
        {
            order["callback_url"] = string.Format(CultureInfo.InvariantCulture, Options.ReturnUrl, Uri.EscapeDataString(request.InvoiceNumber));
        }

        var body = new JsonObject
        {
            ["order"] = order,
            ["payment"] = new JsonObject { ["payment_due_date"] = Options.HostedPageMinutes, ["payment_method_types"] = new JsonArray(type) },
            ["customer"] = Customer(request),
        };
        var (requestId, json) = await PostAsync(CheckoutPath, body, cancellationToken).ConfigureAwait(false);
        using (json)
        {
            var payment = json.RootElement.GetProperty("response").GetProperty("payment");
            return new ChargeInstructions(
                requestId,
                QrString: null,
                VaNumber: null,
                DeeplinkUrl: payment.GetProperty("url").GetString(),
                ExpiresAt: ExpiresAt(payment, "expired_datetime", Options.HostedPageMinutes));
        }
    }

    private async Task<(string RequestId, JsonDocument Json)> PostAsync(string target, JsonObject body, CancellationToken cancellationToken)
    {
        var json = body.ToJsonString();
        var requestId = Guid.NewGuid().ToString();
        var timestamp = Timestamp(clock.UtcNow);
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(target, UriKind.Relative));
        message.Headers.Add("Client-Id", Options.ClientId);
        message.Headers.Add("Request-Id", requestId);
        message.Headers.Add("Request-Timestamp", timestamp);
        message.Headers.Add("Signature", DokuSignature.Sign(Options.SecretKey, Options.ClientId, requestId, timestamp, target, json));
        message.Content = new StringContent(json, Encoding.UTF8);
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var reason = ErrorMessage(text);
            LogRejected(logger, target, (int)response.StatusCode, reason);
            throw new InvalidOperationException($"DOKU {(int)response.StatusCode}: {reason}");
        }

        try
        {
            return (requestId, JsonDocument.Parse(text));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("DOKU sent an unreadable answer.", ex);
        }
    }

    private static JsonObject Customer(ChargeRequest request) => new()
    {
        ["name"] = Truncate(request.CustomerName, 255),
        ["email"] = request.CustomerEmail,
    };

    private DateTime ExpiresAt(JsonElement element, string property, int minutes) =>
        element.TryGetProperty(property, out var value)
        && DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
            ? DateTime.SpecifyKind(at, DateTimeKind.Utc)
            : clock.UtcNow.AddMinutes(minutes);

    private static long? Amount(JsonElement order) =>
        !order.TryGetProperty("amount", out var amount) ? null
        : amount.ValueKind == JsonValueKind.Number ? (long)amount.GetDecimal()
        : long.TryParse(amount.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string? Header(GatewayNotification notification, string name) =>
        notification.Headers.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value is { Length: > 0 } value ? value : null;

    private static string Timestamp(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length];

    /// <summary>BNI wants a numeric reference under 13 digits: the order date plus 6 digits from the order number.</summary>
    internal static string BniReference(string invoiceNumber)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(invoiceNumber));
        var six = (BitConverter.ToUInt32(hash, 0) % 1_000_000).ToString("000000", CultureInfo.InvariantCulture);
        var digits = new string([.. invoiceNumber.Where(char.IsAsciiDigit)]);
        var date = digits.Length >= 8 ? digits.Substring(2, 6) : "000000";                 // LSI-yyyyMMdd-XXXX: yyMMdd
        return date + six;
    }

    private static string ErrorMessage(string text)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            if (root.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var m))
            {
                return m.ToString();
            }

            if (root.TryGetProperty("message", out var message))
            {
                return message.ValueKind == JsonValueKind.Array ? string.Join(", ", message.EnumerateArray().Select(x => x.ToString())) : message.ToString();
            }
        }
        catch (JsonException)
        {
        }

        return text.Length > 200 ? text[..200] : text;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "DOKU rejected {Target} with {Status}: {Reason}")]
    private static partial void LogRejected(ILogger logger, string target, int status, string reason);
}
