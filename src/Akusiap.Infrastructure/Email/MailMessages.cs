using System.Net.Mail;
using System.Text;
using Akusiap.Application.Abstractions;

namespace Akusiap.Infrastructure.Email;

/// <summary>Builds the MIME message shared by the file and SMTP senders.</summary>
internal static class MailMessages
{
    public static MailMessage Create(EmailMessage message, string from)
    {
        ArgumentNullException.ThrowIfNull(message);
        var mail = new MailMessage
        {
            From = new MailAddress(from),
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

        return mail;
    }
}
