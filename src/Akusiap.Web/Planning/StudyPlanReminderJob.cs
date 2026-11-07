using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Application.Planning;
using Akusiap.Domain;
using Akusiap.Domain.Notifications;
using Akusiap.Web.Identity;
using Akusiap.Web.Localization;
using Akusiap.Web.Options;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Akusiap.Web.Planning;

/// <summary>
/// Daily at 06:00 WIB (PLN-05, story M-14, D-126): emails each learner with an active exam goal today's new questions,
/// due cards, days left and any scheduled simulation (PLN-06) per goal, in their preferred language, with a link to the Today page. Skipped when the
/// profile switch is off, the email isn't confirmed, nothing is planned, or a reminder already went out this Jakarta
/// day. The plan is computed by <see cref="StudyPlans"/> in a scope acting as the learner, so access rules match the
/// Today page. Needs <c>Site:BaseUrl</c> for the links; without it nothing is sent.
/// </summary>
public sealed partial class StudyPlanReminderJob(
    IServiceScopeFactory scopes,
    IAppDbContext db,
    IEmailSender sender,
    IStringLocalizer<SharedResource> t,
    IOptions<SiteOptions> site,
    IClock clock,
    ILogger<StudyPlanReminderJob> logger)
{
    public const string Id = "study-plan-reminders";

    /// <summary>06:00 WIB (23:00 UTC the day before).</summary>
    public const string Cron = "0 23 * * *";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 1800)]
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var baseUrl = site.Value.BaseUrl.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            LogNoBaseUrl(logger);
            return 0;
        }

        var now = clock.UtcNow;
        var today = JakartaTime.Today(now);
        var startOfToday = JakartaTime.StartOfDayUtc(today);
        var learners = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.EmailConfirmed && u.Email != null && u.PlanReminderEmails)
            .Where(u => db.StudyGoals.Any(g => g.UserId == u.Id && g.ArchivedAt == null && g.ExamDate >= today))
            .Where(u => !db.Notifications.Any(n => n.UserId == u.Id && n.TemplateCode == NotificationTemplate.ReviewDue && n.Status == NotificationStatus.Sent && n.SentAt >= startOfToday))
            .Select(u => new { u.Id, u.Email, u.DisplayName, u.PreferredLanguage })
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var learner in learners)
        {
            var plans = await PlansForAsync(learner.Id, cancellationToken);
            var due = plans.Where(p => p.Day.NewLeftToday + p.DueCards > 0 || p.Simulation == PlanSimulation.Due).ToList();
            if (due.Count == 0)
            {
                continue;
            }

            var message = Compose(learner.Email!, learner.DisplayName, learner.PreferredLanguage, due, baseUrl);
            var notification = new Notification
            {
                UserId = learner.Id,
                Channel = NotificationChannel.Email,
                TemplateCode = NotificationTemplate.ReviewDue,
                Language = learner.PreferredLanguage,
                Payload = JsonSerializer.Serialize(due.Select(p => new { examId = p.Goal.ExamId, daysLeft = p.Goal.DaysLeft, newQuestions = p.Day.NewLeftToday, dueCards = p.DueCards, simulation = p.Simulation == PlanSimulation.Due }), Json),
                ScheduledAt = now,
            };
            try
            {
                await sender.SendAsync(message, cancellationToken);
                notification.Status = NotificationStatus.Sent;
                notification.SentAt = clock.UtcNow;
                sent++;
            }
            catch (Exception ex) when (ex is SmtpException or IOException or InvalidOperationException)
            {
                notification.Status = NotificationStatus.Failed;
                notification.Error = ex.Message;
                LogFailed(logger, ex, learner.Id);
            }

            db.Notifications.Add(notification);
            await db.SaveChangesAsync(cancellationToken);
        }

        return sent;
    }

    private async Task<IReadOnlyList<StudyPlanToday>> PlansForAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ActingUser>().UserId = userId;
        return await scope.ServiceProvider.GetRequiredService<StudyPlans>().TodayAsync(cancellationToken);
    }

    private EmailMessage Compose(string email, string displayName, string language, IReadOnlyList<StudyPlanToday> plans, string baseUrl)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language == Domain.Users.Languages.English ? "en" : "id-ID");
        try
        {
            var todayUrl = baseUrl + "/learn/hari-ini";
            var profileUrl = baseUrl + "/learn/profil";
            var subject = t["PlanReminder.Subject", plans.Sum(p => p.Day.NewLeftToday), plans.Sum(p => p.DueCards)].Value;
            var greeting = t["Email.Greeting", displayName].Value;
            var intro = t["PlanReminder.Intro"].Value;
            var lines = plans.Select(p => t[p.Simulation == PlanSimulation.Due ? "PlanReminder.LineSimulation" : "PlanReminder.Line", p.Goal.ExamTitle, p.Goal.DaysLeft == 0 ? t["Goals.ExamToday"].Value : t["Goals.DaysLeft", p.Goal.DaysLeft].Value, p.Day.NewLeftToday, p.DueCards].Value).ToList();
            var action = t["PlanReminder.Open"].Value;
            var footer = t["PlanReminder.Footer", profileUrl].Value;
            var items = new StringBuilder();
            foreach (var line in lines)
            {
                items.Append(CultureInfo.InvariantCulture, $"<li>{WebUtility.HtmlEncode(line)}</li>");
            }

            var html = $"""
                <!doctype html>
                <html><body style="font-family:Arial,sans-serif;font-size:16px;line-height:1.5;color:#0f172a">
                <p>{WebUtility.HtmlEncode(greeting)}</p>
                <p>{WebUtility.HtmlEncode(intro)}</p>
                <ul>{items}</ul>
                <p><a href="{WebUtility.HtmlEncode(todayUrl)}" style="display:inline-block;background:#1d5fd1;color:#ffffff;padding:12px 20px;border-radius:8px;text-decoration:none">{WebUtility.HtmlEncode(action)}</a></p>
                <p style="font-size:14px;color:#475569">{WebUtility.HtmlEncode(footer)}</p>
                <p>{Brand.Name}</p>
                </body></html>
                """;
            var text = string.Join("\n", [greeting, string.Empty, intro, .. lines.Select(l => "- " + l), string.Empty, $"{action}: {todayUrl}", string.Empty, footer, string.Empty, Brand.Name]);
            return new EmailMessage(email, subject, html, text);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Study plan reminders skipped: set Site:BaseUrl so the email can link back to the site.")]
    private static partial void LogNoBaseUrl(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not send the study plan reminder to user {UserId}.")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid userId);
}
