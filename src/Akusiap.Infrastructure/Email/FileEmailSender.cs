using System.Net.Mail;
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
        using var mail = MailMessages.Create(message, options.Value.From);

        // Pickup-directory mode writes a complete MIME message to <guid>.eml without any network I/O.
        using var client = new SmtpClient
        {
            DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
            PickupDirectoryLocation = PickupDirectory,
        };
        await client.SendMailAsync(mail, cancellationToken).ConfigureAwait(false);
    }
}
