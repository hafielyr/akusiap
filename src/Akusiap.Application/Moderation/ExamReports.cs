using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Application.Sharing;
using Akusiap.Domain.Auditing;
using Akusiap.Domain.Content;
using Akusiap.Domain.Moderation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Akusiap.Application.Moderation;

public enum ExamReportReason
{
    Inappropriate,
    Copyright,
    Misleading,
    Other,
}

/// <summary>A shared exam as its link shows it. <paramref name="Available"/> is false once an admin took it down.</summary>
public sealed record SharedExamInfo(long ExamId, string Title, string? Description, int QuestionCount, string OwnerName, bool Available, string Token);

public sealed record ExamReportEntry(string Category, string? Details, DateTime At, bool FromGuest);

public sealed record ExamReportGroup(long ExamId, string Title, string OwnerName, ExamStatus Status, int Count, DateTime LatestAt, IReadOnlyList<ExamReportEntry> Entries);

public sealed record TakenDownExam(long ExamId, string Title, string OwnerName);

/// <summary>
/// Reports about shared exams and the admin takedown (SHR-02, story ADM-27, D-86). Anyone holding a working share link
/// (signed in or guest) can report the exam; a signed-in user's repeat report on the same exam is merged. Admins see
/// Open exam reports grouped by exam, newest first, and either take the exam down (Exam TakenDown, its Open reports
/// TakenDown) or reject the reports; a taken-down exam can be restored to Published. Every admin action is audited.
/// Only learner exams, and only while <see cref="FeatureOptions.Sharing"/> is on.
/// </summary>
public sealed class ExamReports(IAppDbContext db, ICurrentUser currentUser, IClock clock, IOptions<FeatureOptions> features)
{
    public const string ReasonRequired = "Report.ReasonRequired";
    public const string ReasonField = "Reason";
    public const string DetailsField = "Details";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The exam behind a share link, or null when the link doesn't work (flag off, unknown token, not shared).</summary>
    public async Task<SharedExamInfo?> FindSharedAsync(string token, CancellationToken cancellationToken)
    {
        if (!features.Value.Sharing || string.IsNullOrEmpty(token) || token.Length != ShareTokens.Length)
        {
            return null;
        }

        var exam = await db.Exams.AsNoTracking()
            .Where(e => e.ShareToken == token && e.IsShared && e.OwnerKind == OwnerKind.Learner)
            .Select(e => new
            {
                e.Id,
                e.Title,
                e.Description,
                e.Status,
                e.ShareToken,
                Count = e.Questions.Count(q => q.Question!.Status == QuestionStatus.Active),
                OwnerName = e.OwnerUser!.DisplayName,
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // The database compares text exactly, but check again so the match is case-sensitive whatever the collation.
        return exam is null || !string.Equals(exam.ShareToken, token, StringComparison.Ordinal)
            ? null
            : new SharedExamInfo(exam.Id, exam.Title, exam.Description, exam.Count, exam.OwnerName, exam.Status != ExamStatus.TakenDown, token);
    }

    public async Task<Result<ReportOutcome>> ReportAsync(string token, ExamReportReason? reason, string? details, CancellationToken cancellationToken)
    {
        var exam = await FindSharedAsync(token, cancellationToken).ConfigureAwait(false);
        if (exam is null)
        {
            return Result.Fail<ReportOutcome>(Problem.NotFound);
        }

        var problems = new List<Problem>();
        if (reason is not { } chosen || !Enum.IsDefined(chosen))
        {
            problems.Add(new Problem(ReasonRequired, ReasonField));
        }

        var cleaned = TextRules.Clean(details);
        if (cleaned is { Length: > QuestionReports.DetailsMaxLength })
        {
            problems.Add(new Problem(TextRules.TooLong, DetailsField, "Report.Details", QuestionReports.DetailsMaxLength));
        }

        if (problems.Count > 0)
        {
            return Result.Fail<ReportOutcome>(problems);
        }

        var text = cleaned is null ? $"{reason}" : $"{reason}: {cleaned}";
        var userId = currentUser.UserId;
        if (userId is not null)
        {
            var existing = await db.ContentReports
                .Where(r => r.ReporterUserId == userId && r.ExamId == exam.ExamId && r.QuestionId == null && r.Status == ContentReportStatus.Open)
                .OrderBy(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                if (!existing.Reason.Split('\n').Contains(text, StringComparer.Ordinal) && existing.Reason.Length + text.Length < QuestionReports.ReasonMaxLength)
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
            ExamId = exam.ExamId,
            Reason = text,
            Status = ContentReportStatus.Open,
            CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(ReportOutcome.Created);
    }

    /// <summary>Open exam reports grouped by exam, the group with the newest report first.</summary>
    public async Task<IReadOnlyList<ExamReportGroup>> OpenGroupsAsync(CancellationToken cancellationToken)
    {
        var rows = await db.ContentReports.AsNoTracking()
            .Where(r => r.Status == ContentReportStatus.Open && r.ExamId != null && r.QuestionId == null)
            .OrderByDescending(r => r.CreatedAt)
            .Take(2000)
            .Select(r => new
            {
                ExamId = r.ExamId!.Value,
                r.Exam!.Title,
                r.Exam.Status,
                OwnerName = r.Exam.OwnerUser == null ? string.Empty : r.Exam.OwnerUser.DisplayName,
                r.Reason,
                r.CreatedAt,
                r.ReporterUserId,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. rows
            .GroupBy(r => r.ExamId)
            .Select(g => new ExamReportGroup(
                g.Key,
                g.First().Title,
                g.First().OwnerName,
                g.First().Status,
                g.Count(),
                g.Max(r => r.CreatedAt),
                [.. g.SelectMany(r => r.Reason.Split('\n').Select(line => Parse(line, r.CreatedAt, r.ReporterUserId is null)))]))
            .OrderByDescending(g => g.LatestAt)
            .ThenByDescending(g => g.ExamId)];
    }

    public async Task<IReadOnlyList<TakenDownExam>> TakenDownAsync(CancellationToken cancellationToken) =>
        await db.Exams.AsNoTracking()
            .Where(e => e.OwnerKind == OwnerKind.Learner && e.Status == ExamStatus.TakenDown)
            .OrderBy(e => e.Title)
            .Select(e => new TakenDownExam(e.Id, e.Title, e.OwnerUser == null ? string.Empty : e.OwnerUser.DisplayName))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Takes the exam down (link shows "tidak tersedia", nobody can start it) and closes its Open reports.</summary>
    public Task<bool> TakeDownAsync(long examId, CancellationToken cancellationToken) =>
        ResolveAsync(examId, ContentReportStatus.TakenDown, ExamStatus.TakenDown, "Exam.TakeDown", cancellationToken);

    /// <summary>Rejects the exam's Open reports; the exam stays as it is.</summary>
    public Task<bool> RejectAsync(long examId, CancellationToken cancellationToken) =>
        ResolveAsync(examId, ContentReportStatus.Rejected, null, "Report.Rejected", cancellationToken);

    /// <summary>Brings a taken-down exam back as Published (its share settings are unchanged).</summary>
    public async Task<bool> RestoreAsync(long examId, CancellationToken cancellationToken)
    {
        var exam = await db.Exams.SingleOrDefaultAsync(e => e.Id == examId && e.OwnerKind == OwnerKind.Learner && e.Status == ExamStatus.TakenDown, cancellationToken).ConfigureAwait(false);
        if (exam is null)
        {
            return false;
        }

        exam.Status = ExamStatus.Published;
        Audit("Exam.Restore", examId, new { before = ExamStatus.TakenDown.ToString(), after = ExamStatus.Published.ToString() });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ResolveAsync(long examId, ContentReportStatus reportStatus, ExamStatus? examStatus, string action, CancellationToken cancellationToken)
    {
        var exam = await db.Exams.SingleOrDefaultAsync(e => e.Id == examId && e.OwnerKind == OwnerKind.Learner, cancellationToken).ConfigureAwait(false);
        if (exam is null)
        {
            return false;
        }

        var adminId = AdminId();
        var now = clock.UtcNow;
        var reports = await db.ContentReports
            .Where(r => r.ExamId == examId && r.QuestionId == null && r.Status == ContentReportStatus.Open)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var report in reports)
        {
            report.Status = reportStatus;
            report.ResolvedByUserId = adminId;
            report.ResolvedAt = now;
        }

        var before = exam.Status;
        if (examStatus is { } status)
        {
            exam.Status = status;
        }

        Audit(action, examId, new { reports = reports.Select(r => r.Id).ToList(), before = before.ToString(), after = exam.Status.ToString() });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void Audit(string action, long examId, object changes) =>
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = AdminId(),
            Action = action,
            EntityName = "Exam",
            EntityId = examId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Changes = JsonSerializer.Serialize(changes, Json),
            CreatedAt = clock.UtcNow,
        });

    private Guid AdminId() => currentUser.UserId ?? throw new InvalidOperationException("Moderation needs a signed-in admin.");

    private static ExamReportEntry Parse(string line, DateTime at, bool fromGuest)
    {
        var split = line.IndexOf(':', StringComparison.Ordinal);
        var head = split < 0 ? line : line[..split];
        var category = Enum.TryParse<ExamReportReason>(head, out var parsed) && Enum.IsDefined(parsed) ? parsed.ToString() : nameof(ExamReportReason.Other);
        var details = split < 0 ? null : line[(split + 1)..].Trim();
        return new ExamReportEntry(category, string.IsNullOrEmpty(details) ? null : details, at, fromGuest);
    }
}
