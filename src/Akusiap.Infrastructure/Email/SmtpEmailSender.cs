using System.Net;
using System.Net.Mail;
using Akusiap.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Akusiap.Infrastructure.Email;

/// <summary>Production sender: an SMTP relay such as Brevo or Amazon SES (AC-02, D-47). STARTTLS on port 587 by default.</summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var settings = options.Value;
        using var mail = MailMessages.Create(message, settings.From);
        using var client = new SmtpClient(settings.Smtp.Host, settings.Smtp.Port)
        {
            EnableSsl = settings.Smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };
        if (!string.IsNullOrEmpty(settings.Smtp.UserName))
        {
            client.Credentials = new NetworkCredential(settings.Smtp.UserName, settings.Smtp.Password);
        }

        await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
    }
}
