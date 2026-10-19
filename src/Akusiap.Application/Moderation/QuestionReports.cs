using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Application.Sessions;
using Akusiap.Domain.Auditing;
using Akusiap.Domain.Moderation;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Moderation;

public enum ReportReason
{
    WrongAnswer,
    Unclear,
    Typo,
    Other,
}

public enum ReportOutcome
{
    Created,
    Merged,
}

/// <summary>The question shown on the report form.</summary>
public sealed record ReportTarget(long AttemptId, int Position, long QuestionId, string QuestionText, bool SessionEnded);

public sealed record ReportQuestionRequest(long AttemptId, int Position, Guid? GuestToken, ReportReason? Reason, string? Details);

/// <summary>Open reports about one question, for the admin queue.</summary>
public sealed record ReportGroup(long QuestionId, string QuestionText, bool IsAppQuestion, int Count, DateTime LatestAt, IReadOnlyList<ReportEntry> Entries);

public sealed record ReportEntry(ReportReason Reason, string? Details, DateTime At, bool FromGuest);

/// <summary>
/// "Laporkan soal" and the admin report queue (SES-11, stories R-07, ADM-04, D-81). A learner or guest reports a question
/// they met in one of their own attempts (identified by attempt and position, so nobody can report arbitrary ids). The
/// reason is stored as <c>Category: details</c>. A signed-in user's new report on a question they already reported
/// (still Open) is merged into that report. Admins see Open reports grouped by question, newest first, and close a whole
/// group as Fixed or Rejected, which is audited.
/// </summary>
public sealed class QuestionReports(IAppDbContext db, ICurrentUser currentUser, IClock clock)
{
    public const string ReasonRequired = "Report.ReasonRequired";
    public const string ReasonField = "Reason";
    public const string DetailsField = "Details";
    public const int DetailsMaxLength = 1000;
    public const int ReasonMaxLength = 4000;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ReportTarget?> GetTargetAsync(long attemptId, int position, Guid? guestToken, CancellationToken cancellationToken)
    {
        var row = await db.AttemptItems.AsNoTracking()
            .Where(i => i.AttemptId == attemptId && i.Position == position)
            .Select(i => new { i.Attempt!.UserId, i.Attempt.GuestToken, i.Attempt.Status, i.QuestionId, i.Question!.Text })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return row is null || !AttemptAccess.IsOwner(currentUser, row.UserId, row.GuestToken, guestToken)
            ? null
            : new ReportTarget(attemptId, position, row.QuestionId, row.Text, row.Status != Domain.Sessions.AttemptStatus.InProgress);
    }

    public async Task<Result<ReportOutcome>> ReportAsync(ReportQuestionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var target = await GetTargetAsync(request.AttemptId, request.Position, request.GuestToken, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return Result.Fail<ReportOutcome>(Problem.NotFound);
        }

        var problems = new List<Problem>();
        if (request.Reason is not { } reason || !Enum.IsDefined(reason))
        {
            problems.Add(new Problem(ReasonRequired, ReasonField));
        }

        var details = TextRules.Clean(request.Details);
        if (details is { Length: > DetailsMaxLength })
        {
            problems.Add(new Problem(TextRules.TooLong, DetailsField, "Report.Details", DetailsMaxLength));
        }

        if (problems.Count > 0)
        {
            return Result.Fail<ReportOutcome>(problems);
        }

        var text = details is null ? $"{request.Reason}" : $"{request.Reason}: {details}";
        var userId = currentUser.UserId;
        if (userId is not null)
        {
            var existing = await db.ContentReports
                .Where(r => r.ReporterUserId == userId && r.QuestionId == target.QuestionId && r.Status == ContentReportStatus.Open)
                .OrderBy(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                var lines = existing.Reason.Split('\n');
                if (!lines.Contains(text, StringComparer.Ordinal) && existing.Reason.Length + text.Length < ReasonMaxLength)
                {
                    existing.Reason = $"{existing.Reason}\n{text}";
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return Result.Ok(ReportOutcome.Merged);
            }
        }

        db.ContentReports.Add(new ContentReport
        {
            ReporterUserId = userId,
            QuestionId = target.QuestionId,
            Reason = text,
            Status = ContentReportStatus.Open,
            CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(ReportOutcome.Created);
    }

    /// <summary>Open question reports grouped by question, the group with the newest report first.</summary>
    public async Task<IReadOnlyList<ReportGroup>> OpenGroupsAsync(CancellationToken cancellationToken)
    {
        var rows = await db.ContentReports.AsNoTracking()
            .Where(r => r.Status == ContentReportStatus.Open && r.QuestionId != null)
            .OrderByDescending(r => r.CreatedAt)
            .Take(2000)
            .Select(r => new { QuestionId = r.QuestionId!.Value, r.Question!.Text, r.Question.OwnerUserId, r.Reason, r.CreatedAt, r.ReporterUserId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. rows
            .GroupBy(r => r.QuestionId)
            .Select(g => new ReportGroup(
                g.Key,
                g.First().Text,
                g.First().OwnerUserId is null,
                g.Count(),
                g.Max(r => r.CreatedAt),
                [.. g.SelectMany(r => r.Reason.Split('\n').Select(line => Parse(line, r.CreatedAt, r.ReporterUserId is null)))]))
            .OrderByDescending(g => g.LatestAt)
            .ThenByDescending(g => g.QuestionId)];
    }

    /// <summary>Closes every Open report on the question as Fixed or Rejected. Returns how many were closed.</summary>
    public async Task<int> ResolveAsync(long questionId, ContentReportStatus status, CancellationToken cancellationToken)
    {
        if (status is not (ContentReportStatus.Fixed or ContentReportStatus.Rejected))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Only Fixed or Rejected close a question report.");
        }

        var adminId = currentUser.UserId ?? throw new InvalidOperationException("Resolving reports needs a signed-in admin.");
        var now = clock.UtcNow;
        var reports = await db.ContentReports
            .Where(r => r.QuestionId == questionId && r.Status == ContentReportStatus.Open)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (reports.Count == 0)
        {
            return 0;
        }

        foreach (var report in reports)
        {
            report.Status = status;
            report.ResolvedByUserId = adminId;
            report.ResolvedAt = now;
        }

        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = adminId,
            Action = $"Report.{status}",
            EntityName = "Question",
            EntityId = questionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Changes = JsonSerializer.Serialize(new { reports = reports.Select(r => r.Id).ToList(), status = status.ToString() }, Json),
            CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return reports.Count;
    }

    private static ReportEntry Parse(string line, DateTime at, bool fromGuest)
    {
        var split = line.IndexOf(':', StringComparison.Ordinal);
        var head = split < 0 ? line : line[..split];
        var reason = Enum.TryParse<ReportReason>(head, out var parsed) && Enum.IsDefined(parsed) ? parsed : ReportReason.Other;
        var details = split < 0 ? null : line[(split + 1)..].Trim();
        return new ReportEntry(reason, string.IsNullOrEmpty(details) ? null : details, at, fromGuest);
    }
}
