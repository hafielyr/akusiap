using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using Akusiap.Application.Abstractions;
using Akusiap.Domain.Users;
using Akusiap.Web.Localization;
using Akusiap.Web.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Akusiap.Web.Identity;

/// <summary>
/// Account emails (AC-02): email confirmation and password reset, in the language of the current request. Links use
/// <see cref="SiteOptions.BaseUrl"/> (the request's own address only in Development). A failed send is logged and
/// reported as false, so registering never fails because the mail relay is down.
/// </summary>
public sealed partial class AccountEmails(
    UserManager<User> users,
    IEmailSender sender,
    IStringLocalizer<SharedResource> t,
    IOptions<SiteOptions> site,
    IHostEnvironment environment,
    IHttpContextAccessor http,
    ILogger<AccountEmails> logger)
{
    public async Task<bool> SendConfirmationAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var code = Encode(await users.GenerateEmailConfirmationTokenAsync(user));
        return await SendAsync(user, "Email.Confirm", $"{AccountRoutes.ConfirmEmail}?userId={user.Id}&code={code}", cancellationToken);
    }

    public async Task<bool> SendPasswordResetAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var code = Encode(await users.GeneratePasswordResetTokenAsync(user));
        return await SendAsync(user, "Email.Reset", $"{AccountRoutes.ResetPassword}?email={Uri.EscapeDataString(user.Email!)}&code={code}", cancellationToken);
    }

    /// <summary>
    /// For accounts an admin created (USR-02): a "set your password" link. It uses the password-reset flow, which also
    /// confirms the email.
    /// </summary>
    public async Task<bool> SendInviteAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var code = Encode(await users.GeneratePasswordResetTokenAsync(user));
        return await SendAsync(user, "Email.Invite", $"{AccountRoutes.ResetPassword}?email={Uri.EscapeDataString(user.Email!)}&code={code}", cancellationToken);
    }

    /// <summary>Turns a code from a link back into the Identity token; null when it isn't valid Base64Url.</summary>
    public static string? Decode(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private string BaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(site.Value.BaseUrl))
        {
            return site.Value.BaseUrl.TrimEnd('/');
        }

        var request = http.HttpContext?.Request;
        if (environment.IsDevelopment() && request is not null)
        {
            return $"{request.Scheme}://{request.Host}";
        }

        throw new InvalidOperationException($"Set {SiteOptions.SectionName}:{nameof(SiteOptions.BaseUrl)} so account emails can link back to the site.");
    }

    private async Task<bool> SendAsync(User user, string template, string pathAndQuery, CancellationToken cancellationToken)
    {
        try
        {
            var message = Compose(user, template, BaseUrl() + pathAndQuery);
            await sender.SendAsync(message, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is SmtpException or IOException or InvalidOperationException)
        {
            LogSendFailed(logger, ex, template);
            return false;
        }
    }

    private EmailMessage Compose(User user, string template, string link)
    {
        var subject = t[$"{template}.Subject"].Value;
        var intro = t[$"{template}.Intro"].Value;
        var action = t[$"{template}.Action"].Value;
        var note = t[$"{template}.Note"].Value;
        var greeting = t["Email.Greeting", user.DisplayName].Value;
        var html = string.Create(CultureInfo.InvariantCulture, $"""
            <!doctype html>
            <html><body style="font-family:Arial,sans-serif;font-size:16px;line-height:1.5;color:#0f172a">
            <p>{WebUtility.HtmlEncode(greeting)}</p>
            <p>{WebUtility.HtmlEncode(intro)}</p>
            <p><a href="{WebUtility.HtmlEncode(link)}" style="display:inline-block;background:#1d5fd1;color:#ffffff;padding:12px 20px;border-radius:8px;text-decoration:none">{WebUtility.HtmlEncode(action)}</a></p>
            <p style="font-size:14px;color:#475569">{WebUtility.HtmlEncode(t["Email.LinkFallback"].Value)}<br><a href="{WebUtility.HtmlEncode(link)}">{WebUtility.HtmlEncode(link)}</a></p>
            <p style="font-size:14px;color:#475569">{WebUtility.HtmlEncode(note)}</p>
            <p>{Brand.Name}</p>
            </body></html>
            """);
        var text = string.Join("\n\n", greeting, intro, $"{action}: {link}", note, Brand.Name);
        return new EmailMessage(user.Email!, subject, html, text);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not send the {Template} email.")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, string template);
}
