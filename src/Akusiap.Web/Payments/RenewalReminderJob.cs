using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Application.Payments;
using Akusiap.Domain;
using Akusiap.Domain.Access;
using Akusiap.Domain.Notifications;
using Akusiap.Web.Localization;
using Akusiap.Web.Options;
using Akusiap.Web.Pages.Exams;
using Hangfire;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Akusiap.Web.Payments;

/// <summary>
/// Daily at 09:00 WIB (PAY-08, story P-07, D-77): emails each package owner 7 days and 1 day before the package ends,
/// in Indonesian like the receipt, with a link to renew the same package. Each reminder is marked sent only after the
/// email went out, so a failed send is tried again on the next run and a sent one is never repeated. Needs
/// <c>Site:BaseUrl</c> for the link; without it nothing is sent.
/// </summary>
public sealed partial class RenewalReminderJob(
    ListDueReminders reminders,
    IAppDbContext db,
    IEmailSender sender,
    IStringLocalizer<SharedResource> t,
    IOptions<SiteOptions> site,
    IClock clock,
    ILogger<RenewalReminderJob> logger)
{
    public const string Id = "renewal-reminders";

    /// <summary>09:00 WIB (02:00 UTC).</summary>
    public const string Cron = "0 2 * * *";

    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id-ID");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var baseUrl = site.Value.BaseUrl.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            LogNoBaseUrl(logger);
            return 0;
        }

        var sent = 0;
        foreach (var reminder in await reminders.HandleAsync(cancellationToken))
        {
            var message = Compose(reminder, baseUrl + ExamRoutes.PlanCheckout(reminder.PlanPriceId));
            var notification = new Notification
            {
                UserId = reminder.UserId,
                Channel = NotificationChannel.Email,
                TemplateCode = reminder.Kind == RenewalReminder.OneDay ? NotificationTemplate.Renewal1d : NotificationTemplate.Renewal7d,
                Language = Domain.Users.Languages.Indonesian,
                Payload = JsonSerializer.Serialize(new { subscriptionId = reminder.SubscriptionId, periodEnd = reminder.PeriodEnd, planPriceId = reminder.PlanPriceId }, Json),
                ScheduledAt = clock.UtcNow,
            };
            try
            {
                await sender.SendAsync(message, cancellationToken);
                notification.Status = NotificationStatus.Sent;
                notification.SentAt = clock.UtcNow;
            }
            catch (Exception ex) when (ex is SmtpException or IOException or InvalidOperationException)
            {
                notification.Status = NotificationStatus.Failed;
                notification.Error = ex.Message;
                LogFailed(logger, ex, reminder.SubscriptionId);
            }

            db.Notifications.Add(notification);
            await db.SaveChangesAsync(cancellationToken);
            if (notification.Status == NotificationStatus.Sent)
            {
                await reminders.MarkSentAsync(reminder.SubscriptionId, reminder.Kind, cancellationToken);
                sent++;
            }
        }

        return sent;
    }

    private EmailMessage Compose(DueReminder reminder, string renewUrl)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = Indonesian;
        try
        {
            var package = $"{reminder.PlanName} {t["Admin.Plans.Months", reminder.DurationMonths]}";
            var daysLeft = JakartaTime.DaysLeft(reminder.PeriodEnd, clock.UtcNow);
            var when = daysLeft switch
            {
                <= 0 => t["Reminder.Today"].Value,
                1 => t["Reminder.Tomorrow"].Value,
                _ => t["Reminder.InDays", daysLeft].Value,
            };
            var endsAt = JakartaTime.ToLocal(reminder.PeriodEnd).ToString("d MMMM yyyy HH:mm", Indonesian) + " WIB";
            var subject = t["Reminder.Subject", package, when].Value;
            var greeting = t["Email.Greeting", reminder.DisplayName].Value;
            var body = t["Reminder.Body", package, when, endsAt].Value;
            var action = t["Reminder.Renew"].Value;
            var html = $"""
                <!doctype html>
                <html><body style="font-family:Arial,sans-serif;font-size:16px;line-height:1.5;color:#0f172a">
                <p>{WebUtility.HtmlEncode(greeting)}</p>
                <p>{WebUtility.HtmlEncode(body)}</p>
                <p><a href="{WebUtility.HtmlEncode(renewUrl)}" style="display:inline-block;background:#1d5fd1;color:#ffffff;padding:12px 20px;border-radius:8px;text-decoration:none">{WebUtility.HtmlEncode(action)}</a></p>
                <p>{Brand.Name}</p>
                </body></html>
                """;
            var text = string.Join("\n", greeting, string.Empty, body, string.Empty, $"{action}: {renewUrl}", string.Empty, Brand.Name);
            return new EmailMessage(reminder.Email, subject, html, text);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Renewal reminders skipped: set Site:BaseUrl so the email can link back to the site.")]
    private static partial void LogNoBaseUrl(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not send the renewal reminder for subscription {SubscriptionId}.")]
    private static partial void LogFailed(ILogger logger, Exception exception, long subscriptionId);
}
