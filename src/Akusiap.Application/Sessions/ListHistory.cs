using Akusiap.Application.Abstractions;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Sessions;

public sealed record HistoryRow(
    long AttemptId,
    DateTime StartedAt,
    string? ExamTitle,
    AttemptMode Mode,
    AttemptStatus Status,
    AttemptSource Source,
    decimal? ScorePercent,
    int CorrectCount,
    int TotalCount,
    int TimeSpentSeconds);

public sealed record HistoryPage(IReadOnlyList<HistoryRow> Items, int Page, int PageSize, int Total)
{
    public int PageCount => Math.Max(1, (Total + PageSize - 1) / PageSize);
}

/// <summary>
/// The signed-in learner's past attempts, newest first and paged (SES-10, stories H-01, H-02, D-75). Only their own
/// attempts; guests have none.
/// </summary>
public sealed class ListHistory(IAppDbContext db, ICurrentUser currentUser)
{
    public const int PageSize = 20;

    public async Task<HistoryPage> HandleAsync(int page, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return new HistoryPage([], 1, PageSize, 0);
        }

        var query = db.Attempts.AsNoTracking().Where(a => a.UserId == userId);
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var pageCount = Math.Max(1, (total + PageSize - 1) / PageSize);
        page = Math.Clamp(page, 1, pageCount);
        var rows = await query
            .OrderByDescending(a => a.StartedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(a => new HistoryRow(
                a.Id,
                a.StartedAt,
                a.Exam == null ? null : a.Exam.Title,
                a.Mode,
                a.Status,
                a.Source,
                a.ScorePercent,
                a.CorrectCount,
                a.TotalCount,
                a.TimeSpentSeconds))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new HistoryPage(rows, page, PageSize, total);
    }
}
