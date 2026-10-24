using Akusiap.Application.Abstractions;
using Akusiap.Domain.Payments;
using Akusiap.Infrastructure.Payments;
using Akusiap.TestSupport.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Akusiap.IntegrationTests.Payments;

/// <summary>
/// PAY-04 against the real DOKU sandbox. Runs only when <c>DOKU_SANDBOX_TESTS=1</c> and sandbox keys are in
/// user-secrets or env vars (<c>Doku:ClientId</c>, <c>Doku:SecretKey</c>); otherwise it does nothing, so CI never calls
/// DOKU. Never point it at production keys: it creates real (sandbox) payment requests.
/// </summary>
public sealed class DokuSandboxTests
{
    public const string Variable = "DOKU_SANDBOX_TESTS";

    [Fact]
    public async Task The_sandbox_creates_a_virtual_account_and_a_hosted_card_page()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
        {
            return;
        }

        var configuration = new ConfigurationBuilder().AddUserSecrets<Program>(optional: true).AddEnvironmentVariables().Build();
        var options = configuration.GetSection(DokuOptions.SectionName).Get<DokuOptions>() ?? new DokuOptions();
        Assert.True(options.IsConfigured, "Set Doku:ClientId and Doku:SecretKey (sandbox) first.");
        Assert.False(options.IsProduction, "The sandbox test never runs with Doku:Environment=Production.");
        options.ReturnUrl = "https://latihansoal.test/bayar/{0}";
        var clock = new TestClock { UtcNow = DateTime.UtcNow };
        using var http = new HttpClient { BaseAddress = new Uri(DokuOptions.SandboxUrl) };
        var gateway = new DokuGateway(http, Microsoft.Extensions.Options.Options.Create(options), clock, NullLogger<DokuGateway>.Instance);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);

        var va = await gateway.CreateChargeAsync(new ChargeRequest($"LSI-{stamp}-VA", 15_000, PaymentMethod.VirtualAccount, "BCA", "Sandbox test", "Sandbox Tester", "sandbox@example.com"), CancellationToken.None);
        var card = await gateway.CreateChargeAsync(new ChargeRequest($"LSI-{stamp}-CC", 15_000, PaymentMethod.CreditCard, null, "Sandbox test", "Sandbox Tester", "sandbox@example.com"), CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(va.VaNumber));
        Assert.InRange(va.ExpiresAt, DateTime.UtcNow.AddHours(23), DateTime.UtcNow.AddHours(25));      // 24 hours (owner decision)
        Assert.StartsWith("https://", card.DeeplinkUrl, StringComparison.Ordinal);
        Assert.InRange(card.ExpiresAt, DateTime.UtcNow.AddMinutes(25), DateTime.UtcNow.AddMinutes(35)); // 30 minutes
    }
}
