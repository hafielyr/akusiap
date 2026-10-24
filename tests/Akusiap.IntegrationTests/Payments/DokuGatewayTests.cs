using System.Net;
using System.Text;
using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Domain.Payments;
using Akusiap.Infrastructure.Payments;
using Akusiap.TestSupport.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Akusiap.IntegrationTests.Payments;

/// <summary>
/// PAY-04: the DOKU client against recorded sandbox answers (no network): the request signature, virtual accounts on
/// the Direct API, card/e-wallet/QRIS on the hosted page, errors, and verifying and reading notifications.
/// </summary>
public sealed class DokuGatewayTests
{
    private static readonly DokuOptions Options = new()
    {
        ClientId = "BRN-0000-1234567890123",
        SecretKey = "SK-test-secret",
        ReturnUrl = "https://latihansoal.test/bayar/{0}",
    };

    private readonly TestClock _clock = new();

    [Fact]
    public void The_signature_matches_dokus_documented_scheme()
    {
        // DOKU's documented example string; the expected value was computed independently with openssl.
        const string text = "Client-Id:MCH-0001-10791114622547\nRequest-Id:cc682442-6c22-493e-8121-b9ef6b3fa728\nRequest-Timestamp:2020-08-11T08:45:42Z\nRequest-Target:/doku-virtual-account/v2/payment-code\nDigest:5WIYK2TJg6iiZ0d5v4IXSR0EkYEkYOezJIma3Ufli5s=";

        Assert.Equal("HMACSHA256=9oIvh8TAJ/V5GT1awNcj/+UlhhGweNFB5MgF8H/iA+w=", DokuSignature.SignText("SK-test-secret", text));
        Assert.Equal("eVxtsKCvqVuy2xSu6fgPxJuyXo9tkXfznTIwBjcGcaQ=", DokuSignature.Digest("{\"order\":{\"invoice_number\":\"INV-1\",\"amount\":15000}}"));
        Assert.EndsWith("Request-Target:/x", DokuSignature.StringToSign("c", "r", "t", "/x", null), StringComparison.Ordinal); // GET: no Digest line
    }

    [Fact]
    public async Task A_virtual_account_is_created_on_the_direct_api_for_24_hours()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"order":{"invoice_number":"LSI-20261023-AB12"},"virtual_account_info":{"virtual_account_number":"1900800000371404","expired_date_utc":"2026-10-24T12:26:39Z"}}""");
        var gateway = Gateway(handler);

        var result = await gateway.CreateChargeAsync(Charge(PaymentMethod.VirtualAccount, "BCA"), CancellationToken.None);

        Assert.Equal(("1900800000371404", (string?)null, (string?)null), (result.VaNumber, result.QrString, result.DeeplinkUrl));
        Assert.Equal(new DateTime(2026, 10, 24, 12, 26, 39, DateTimeKind.Utc), result.ExpiresAt);
        var sent = handler.Requests.Single();
        Assert.Equal("/bca-virtual-account/v2/payment-code", sent.Path);
        AssertSigned(sent);
        using var body = JsonDocument.Parse(sent.Body);
        Assert.Equal(1440, body.RootElement.GetProperty("virtual_account_info").GetProperty("expired_time").GetInt32());
        Assert.Equal(49_000, body.RootElement.GetProperty("order").GetProperty("amount").GetInt64());
        Assert.False(body.RootElement.GetProperty("virtual_account_info").TryGetProperty("merchant_unique_reference", out _));
    }

    [Fact]
    public async Task Bni_gets_the_numeric_reference_it_requires()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"virtual_account_info":{"virtual_account_number":"8803300000118740","expired_date_utc":"2026-10-24T12:32:29Z"}}""");

        await Gateway(handler).CreateChargeAsync(Charge(PaymentMethod.VirtualAccount, "BNI"), CancellationToken.None);

        var sent = handler.Requests.Single();
        Assert.Equal("/bni-virtual-account/v2/payment-code", sent.Path);
        using var body = JsonDocument.Parse(sent.Body);
        var reference = body.RootElement.GetProperty("virtual_account_info").GetProperty("merchant_unique_reference").GetString()!;
        Assert.Matches("^261023[0-9]{6}$", reference);                                       // under 13 digits, numeric
    }

    [Theory]
    [InlineData(PaymentMethod.CreditCard, null, "CREDIT_CARD")]
    [InlineData(PaymentMethod.EWallet, "DANA", "EMONEY_DANA")]
    [InlineData(PaymentMethod.EWallet, "SHOPEEPAY", "EMONEY_SHOPEE_PAY")]
    [InlineData(PaymentMethod.QRIS, null, "QRIS")]
    public async Task Card_e_wallets_and_qris_open_dokus_hosted_page_for_that_method(PaymentMethod method, string? channel, string type)
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"message":["SUCCESS"],"response":{"payment":{"url":"https://staging.doku.com/checkout-link-v2/abc","expired_datetime":"2026-10-23T13:00:00Z"}}}""");

        var result = await Gateway(handler).CreateChargeAsync(Charge(method, channel), CancellationToken.None);

        Assert.Equal("https://staging.doku.com/checkout-link-v2/abc", result.DeeplinkUrl);
        Assert.Null(result.VaNumber);
        var sent = handler.Requests.Single();
        Assert.Equal(DokuGateway.CheckoutPath, sent.Path);
        AssertSigned(sent);
        using var body = JsonDocument.Parse(sent.Body);
        var payment = body.RootElement.GetProperty("payment");
        Assert.Equal([type], payment.GetProperty("payment_method_types").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(30, payment.GetProperty("payment_due_date").GetInt32());
        Assert.Equal("https://latihansoal.test/bayar/LSI-20261023-AB12", body.RootElement.GetProperty("order").GetProperty("callback_url").GetString());
    }

    [Fact]
    public async Task A_refused_charge_raises_dokus_message()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, """{"message":["PAYMENT CHANNEL IS INACTIVE"]}""");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Gateway(handler).CreateChargeAsync(Charge(PaymentMethod.QRIS, null), CancellationToken.None));

        Assert.Equal("DOKU 400: PAYMENT CHANNEL IS INACTIVE", error.Message);
    }

    [Fact]
    public void A_signed_notification_is_trusted_and_read_and_tampering_is_caught()
    {
        var gateway = Gateway(new StubHandler(HttpStatusCode.OK, "{}"));
        var paid = DokuGateway.CreateSampleNotification(Options, "LSI-20261023-AB12", "SUCCESS", 49_000, _clock.UtcNow);
        var tampered = paid with { Body = paid.Body.Replace("49000", "1", StringComparison.Ordinal) };
        var otherKey = DokuGateway.CreateSampleNotification(new DokuOptions { ClientId = Options.ClientId, SecretKey = "SK-other" }, "LSI-20261023-AB12", "SUCCESS", 49_000, _clock.UtcNow);
        var otherClient = DokuGateway.CreateSampleNotification(new DokuOptions { ClientId = "BRN-9999-0000000000000", SecretKey = Options.SecretKey }, "LSI-20261023-AB12", "SUCCESS", 49_000, _clock.UtcNow);

        Assert.True(gateway.VerifySignature(paid));
        Assert.False(gateway.VerifySignature(tampered));
        Assert.False(gateway.VerifySignature(otherKey));
        Assert.False(gateway.VerifySignature(otherClient));
        var ev = gateway.ParseNotification(paid)!;
        Assert.Equal(("LSI-20261023-AB12", GatewayEventKind.Paid, (long?)49_000), (ev.InvoiceNumber, ev.Kind, ev.AmountIdr));
        Assert.Equal(paid.Headers["Request-Id"], ev.EventId);
    }

    [Theory]
    [InlineData("""{"order":{"invoice_number":"LSI-1","amount":"15000"},"transaction":{"status":"FAILED"}}""", GatewayEventKind.Failed, 15_000L)]
    [InlineData("""{"order":{"invoice_number":"LSI-1","amount":15000},"transaction":{"status":"EXPIRED"}}""", GatewayEventKind.Expired, 15_000L)]
    [InlineData("""{"order":{"invoice_number":"LSI-1"},"transaction":{"status":"PENDING"}}""", GatewayEventKind.Other, null)]
    public void Notification_statuses_map_to_events(string body, GatewayEventKind kind, long? amount)
    {
        var ev = Gateway(new StubHandler(HttpStatusCode.OK, "{}")).ParseNotification(new GatewayNotification(new Dictionary<string, string> { ["Request-Id"] = "r-1" }, body))!;

        Assert.Equal((kind, amount), (ev.Kind, ev.AmountIdr));
    }

    [Fact]
    public void An_unreadable_notification_is_rejected()
    {
        var gateway = Gateway(new StubHandler(HttpStatusCode.OK, "{}"));

        Assert.Null(gateway.ParseNotification(new GatewayNotification(new Dictionary<string, string> { ["Request-Id"] = "r-1" }, "not json")));
        Assert.Null(gateway.ParseNotification(new GatewayNotification(new Dictionary<string, string>(), """{"order":{"invoice_number":"LSI-1"}}""")));
    }

    [Fact]
    public async Task The_status_api_is_asked_with_a_signed_get_and_read_like_a_notification()
    {
        // Recorded from the sandbox (trimmed) for a BCA virtual account paid in DOKU's simulator.
        var handler = new StubHandler(HttpStatusCode.OK, """{"order":{"invoice_number":"LSI-20261024-YVFF","amount":49000},"transaction":{"status":"SUCCESS","type":"PAYMENT","date":"2026-10-24T07:46:32Z"},"service":{"id":"VIRTUAL_ACCOUNT"}}""");

        var check = await Gateway(handler).CheckStatusAsync("LSI-20261024-YVFF", CancellationToken.None);

        Assert.NotNull(check);
        Assert.Equal(("status:LSI-20261024-YVFF:Paid", "LSI-20261024-YVFF", GatewayEventKind.Paid, (long?)49_000), (check.Event.EventId, check.Event.InvoiceNumber, check.Event.Kind, check.Event.AmountIdr));
        var sent = handler.Requests.Single();
        Assert.Equal("/orders/v1/status/LSI-20261024-YVFF", sent.Path);
        Assert.Equal(string.Empty, sent.Body);
        var expected = DokuSignature.Sign(Options.SecretKey, Options.ClientId, sent.Headers["Request-Id"], sent.Headers["Request-Timestamp"], sent.Path, null);
        Assert.Equal(expected, sent.Headers["Signature"]);                                  // GET: signed without a Digest
    }

    [Fact]
    public async Task An_invoice_dokus_status_api_doesnt_know_has_no_status()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, """{"error":{"code":"not_found","message":"Order not found"}}""");

        Assert.Null(await Gateway(handler).CheckStatusAsync("LSI-20261024-NONE", CancellationToken.None));
    }

    private DokuGateway Gateway(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri(DokuOptions.SandboxUrl) }, Microsoft.Extensions.Options.Options.Create(Options), _clock, NullLogger<DokuGateway>.Instance);

    private static ChargeRequest Charge(PaymentMethod method, string? channel) =>
        new("LSI-20261023-AB12", 49_000, method, channel, "Tryout SKD CPNS", "Sari", "sari@example.com");

    private static void AssertSigned(StubHandler.Sent sent)
    {
        Assert.Equal(Options.ClientId, sent.Headers["Client-Id"]);
        var expected = DokuSignature.Sign(Options.SecretKey, Options.ClientId, sent.Headers["Request-Id"], sent.Headers["Request-Timestamp"], sent.Path, sent.Body);
        Assert.Equal(expected, sent.Headers["Signature"]);
    }

    /// <summary>Answers every request with one recorded response and keeps what was sent.</summary>
    internal sealed class StubHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public sealed record Sent(string Path, Dictionary<string, string> Headers, string Body);

        public List<Sent> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Sent(request.RequestUri!.AbsolutePath, request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value)), body));
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
