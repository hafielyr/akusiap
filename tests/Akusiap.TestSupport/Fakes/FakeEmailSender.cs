using System.Collections.Concurrent;
using Akusiap.Application.Abstractions;

namespace Akusiap.TestSupport.Fakes;

/// <summary>Keeps sent emails in memory so tests can read them.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    /// <summary>Where the app thinks emails go; tests set it to check the messages for each case (AC-07).</summary>
    public EmailDelivery Delivery { get; set; } = EmailDelivery.Inbox;

    public IReadOnlyList<EmailMessage> Sent => [.. _sent];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>The emails sent to one address, oldest first.</summary>
    public IReadOnlyList<EmailMessage> To(string address) =>
        [.. _sent.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase))];
}
