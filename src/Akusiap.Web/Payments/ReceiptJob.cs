using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Domain;
using Akusiap.Domain.Notifications;
using Akusiap.Domain.Payments;
using Akusiap.Web.Localization;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Payments;

/// <summary>
/// The receipt email (PAY-07, story P-05, D-69), always in Indonesian: order number, item, amount, method, when it was
/// paid and when access ends (WIB). Records a Receipt notification as Sent or Failed; a failed send is recorded, not
/// retried, so a receipt is never sent twice.
/// </summary>
public sealed partial class ReceiptJob(IAppDbContext db, IEmailSender sender, IStringLocalizer<SharedResource> t, IClock clock, ILogger<ReceiptJob> logger)
{
    private static readonly CultureInfo Indonesian = CultureInfo.GetCultureInfo("id-ID");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync(long paymentId, CancellationToken cancellationToken)
    {
        var payment = await db.Payments
            .Where(p => p.Id == paymentId && p.Status == PaymentStatus.Paid)
            .Select(p => new
            {
                p.UserId,
                p.User!.Email,
                p.User.DisplayName,
                p.OrderNumber,
                p.AmountIdr,
                p.Method,
                p.Channel,
                p.PaidAt,
                ExamTitle = p.ExamPurchase == null ? null : p.ExamPurchase.Exam!.Title,
                ExamEnds = p.ExamPurchase == null ? null : p.ExamPurchase.AccessExpiresAt,
                PlanName = p.Subscription == null ? null : p.Subscription.Plan!.Name,
                Months = p.Subscription == null ? (int?)null : p.Subscription.PlanPrice!.DurationMonths,
                PlanEnds = p.Subscription == null ? null : p.Subscription.CurrentPeriodEnd,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (payment is null || payment.Email is null)
        {
            return;
        }

        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = Indonesian;
        EmailMessage message;
        object values;
        try
        {
            var item = payment.ExamTitle ?? $"{payment.PlanName} {t["Admin.Plans.Months", payment.Months ?? 0]}";
            var method = payment.Channel is null ? t[$"Checkout.Method.{payment.Method}"].Value : $"{t[$"Checkout.Method.{payment.Method}"]} {payment.Channel}";
            var paidAt = JakartaTime.ToLocal(payment.PaidAt!.Value).ToString("d MMMM yyyy HH:mm", Indonesian) + " WIB";
            var ends = payment.ExamEnds ?? payment.PlanEnds;
            var endsText = ends is { } e ? JakartaTime.ToLocal(e).ToString("d MMMM yyyy", Indonesian) : "-";
            var rows = new (string Label, string Value)[]
            {
                (t["Receipt.Order"], payment.OrderNumber),
                (t["Receipt.Item"], item),
                (t["Receipt.Amount"], Money.FormatIdr(payment.AmountIdr)),
                (t["Receipt.Method"], method),
                (t["Receipt.PaidAt"], paidAt),
                (t["Receipt.AccessUntil"], endsText),
            };
            var subject = t["Receipt.Subject", payment.OrderNumber].Value;
            var greeting = t["Email.Greeting", payment.DisplayName].Value;
            var intro = t["Receipt.Intro"].Value;
            var note = t["Receipt.Note"].Value;
            var table = string.Concat(rows.Select(r =>
                $"<tr><td style=\"padding:4px 12px 4px 0;color:#475569\">{WebUtility.HtmlEncode(r.Label)}</td><td style=\"padding:4px 0;font-weight:600\">{WebUtility.HtmlEncode(r.Value)}</td></tr>"));
            var html = $"""
                <!doctype html>
                <html><body style="font-family:Arial,sans-serif;font-size:16px;line-height:1.5;color:#0f172a">
                <p>{WebUtility.HtmlEncode(greeting)}</p>
                <p>{WebUtility.HtmlEncode(intro)}</p>
                <table style="border-collapse:collapse">{table}</table>
                <p style="font-size:14px;color:#475569">{WebUtility.HtmlEncode(note)}</p>
                <p>{Brand.Name}</p>
                </body></html>
                """;
            var text = string.Join("\n", [greeting, string.Empty, intro, string.Empty, .. rows.Select(r => $"{r.Label}: {r.Value}"), string.Empty, note, Brand.Name]);
            message = new EmailMessage(payment.Email, subject, html, text);
            values = new { orderNumber = payment.OrderNumber, item, amountIdr = payment.AmountIdr, method, paidAt = payment.PaidAt, accessUntil = ends };
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }

        var notification = new Notification
        {
            UserId = payment.UserId,
            Channel = NotificationChannel.Email,
            TemplateCode = NotificationTemplate.Receipt,
            Language = Domain.Users.Languages.Indonesian,
            Payload = JsonSerializer.Serialize(values, Json),
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
            LogFailed(logger, ex, payment.OrderNumber);
        }

        db.Notifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not send the receipt for {OrderNumber}.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string orderNumber);
}

/// <summary>Queues jobs on Hangfire (PAY-07).</summary>
public sealed class HangfirePaymentJobs(IBackgroundJobClient jobs) : IPaymentJobs
{
    public Task QueueReceiptAsync(long paymentId, CancellationToken cancellationToken)
    {
        jobs.Enqueue<ReceiptJob>(job => job.RunAsync(paymentId, CancellationToken.None));
        return Task.CompletedTask;
    }
}

/// <summary>Runs the job at once in its own scope; used when background jobs are off (tests).</summary>
public sealed class InlinePaymentJobs(IServiceScopeFactory scopes) : IPaymentJobs
{
    public async Task QueueReceiptAsync(long paymentId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ReceiptJob>().RunAsync(paymentId, cancellationToken);
    }
}
