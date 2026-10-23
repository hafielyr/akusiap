namespace Akusiap.Application.Abstractions;

/// <summary>An outgoing email. <paramref name="TextBody"/> is an optional plain-text alternative.</summary>
public sealed record EmailMessage(string To, string Subject, string HtmlBody, string? TextBody = null);

/// <summary>Where a sender's emails end up (AC-07), so pages can tell people the truth after "send".</summary>
public enum EmailDelivery
{
    /// <summary>Sent to the recipient's inbox (SMTP).</summary>
    Inbox,

    /// <summary>Saved as an <c>.eml</c> file in <c>.dev-mail</c> (Development without SMTP).</summary>
    File,

    /// <summary>Dropped with a warning because no SMTP host is set (outside Development).</summary>
    Dropped,
}

public interface IEmailSender
{
    /// <summary>Where this sender's emails go.</summary>
    EmailDelivery Delivery => EmailDelivery.Inbox;

    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
