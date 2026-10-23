namespace Akusiap.Infrastructure.Payments;

/// <summary>
/// DOKU settings (PAY-04, D-110). <see cref="ClientId"/> and <see cref="SecretKey"/> come from user-secrets or env vars
/// only (<c>Doku__ClientId</c>, <c>Doku__SecretKey</c>). The sandbox is the default; the live API is used only when
/// <see cref="Environment"/> is <c>Production</c>, which a Development run refuses.
/// </summary>
public sealed class DokuOptions
{
    public const string SectionName = "Doku";
    public const string SandboxUrl = "https://api-sandbox.doku.com";
    public const string ProductionUrl = "https://api.doku.com";

    public string ClientId { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    /// <summary><c>Sandbox</c> (default) or <c>Production</c>.</summary>
    public string Environment { get; set; } = "Sandbox";

    /// <summary>The path of our notification URL; it is part of the signature DOKU sends (Request-Target).</summary>
    public string NotificationPath { get; set; } = "/webhooks/doku";

    /// <summary>
    /// Where DOKU's hosted page sends the payer back, with <c>{0}</c> for the order number. Empty uses
    /// <c>Site:BaseUrl</c> + <c>/bayar/{0}</c>; with neither, DOKU shows its own result page.
    /// </summary>
    public string ReturnUrl { get; set; } = string.Empty;

    /// <summary>How long a virtual account stays open (owner decision: 24 hours).</summary>
    public int VirtualAccountMinutes { get; set; } = 24 * 60;

    /// <summary>How long a hosted-page payment (card, e-wallet, QRIS) stays open (owner decision: 30 minutes).</summary>
    public int HostedPageMinutes { get; set; } = 30;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(SecretKey);

    public bool IsProduction => string.Equals(Environment, "Production", StringComparison.OrdinalIgnoreCase);

    public string BaseUrl => IsProduction ? ProductionUrl : SandboxUrl;
}
