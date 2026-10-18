namespace Akusiap.Application.Abstractions;

/// <summary>An outgoing email. <paramref name="TextBody"/> is an optional plain-text alternative.</summary>
public sealed record EmailMessage(string To, string Subject, string HtmlBody, string? TextBody = null);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
