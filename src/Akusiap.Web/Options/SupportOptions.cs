namespace Akusiap.Web.Options;

public sealed class SupportOptions
{
    public const string SectionName = "Support";

    /// <summary>WhatsApp number in international format without "+", e.g. 6281234567890.</summary>
    public string WhatsAppNumber { get; set; } = string.Empty;

    public string WhatsAppUrl => $"https://wa.me/{WhatsAppNumber}";
}
