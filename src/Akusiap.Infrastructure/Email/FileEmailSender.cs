using System.Net.Mail;
using System.Text;
using Akusiap.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Akusiap.Infrastructure.Email;

/// <summary>Development sender: writes each message as an <c>.eml</c> file instead of sending it (D-15).</summary>
internal sealed class FileEmailSender(IOptions<EmailOptions> options, string pickupDirectory) : IEmailSender
{
    public string PickupDirectory { get; } = pickupDirectory;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        Directory.CreateDirectory(PickupDirectory);

        using var mail = new MailMessage
        {
            From = new MailAddress(options.Value.From),
            Subject = message.Subject,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8,
            HeadersEncoding = Encoding.UTF8,
        };
        mail.To.Add(message.To);

        if (message.TextBody is null)
        {
            mail.Body = message.HtmlBody;
            mail.IsBodyHtml = true;
        }
        else
        {
            mail.Body = message.TextBody;
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, Encoding.UTF8, "text/html"));
        }

        // SmtpClient's pickup-directory mode writes a complete MIME message to <guid>.eml without any network I/O.
        using var client = new SmtpClient
        {
            DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
            PickupDirectoryLocation = PickupDirectory,
        };
        await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
    }
}
