namespace Akusiap.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Sender address, for example <c>Akusiap &lt;no-reply@akusiap.id&gt;</c>.</summary>
    public string From { get; set; } = "Akusiap <no-reply@akusiap.local>";

    /// <summary>Folder for the development file sender, relative to the app's content root.</summary>
    public string PickupDirectory { get; set; } = ".dev-mail";

    /// <summary>The production SMTP relay (Brevo, SES …). Set through env vars or user-secrets only, never committed.</summary>
    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    /// <summary>Empty means "not configured": outside Development, emails are then dropped with a warning.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>587 with STARTTLS by default.</summary>
    public int Port { get; set; } = 587;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool EnableSsl { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
}
