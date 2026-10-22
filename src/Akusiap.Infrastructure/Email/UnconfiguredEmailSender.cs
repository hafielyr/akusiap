using Akusiap.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Akusiap.Infrastructure.Email;

/// <summary>
/// Used outside Development when no SMTP host is set: the app keeps working, but emails are dropped with a warning.
/// The message body (which may hold a confirmation or reset link) is never logged.
/// </summary>
internal sealed partial class UnconfiguredEmailSender(ILogger<UnconfiguredEmailSender> logger) : IEmailSender
{
    public EmailDelivery Delivery => EmailDelivery.Dropped;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        LogDropped(logger, message.Subject);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email:Smtp:Host is not set, so the email \"{Subject}\" was not sent.")]
    private static partial void LogDropped(ILogger logger, string subject);
}
