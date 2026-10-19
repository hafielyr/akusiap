using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Application.Catalog;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Sessions;

/// <summary>Correct answers per topic.</summary>
public sealed record TopicResult(string Topic, int Correct, int Total)
{
    public decimal Percent => Total == 0 ? 0m : Math.Round(100m * Correct / Total, 1, MidpointRounding.AwayFromZero);
}

/// <summary>What a trial result offers: buy this exam, or subscribe from the cheapest monthly price (CAT-03).</summary>
public sealed record TrialOffer(long ExamId, long ExamPriceIdr, long? CheapestMonthlyIdr);

public sealed record AttemptResult(
    long AttemptId,
    long? ExamId,
    string Title,
    AttemptSource Source,
    AttemptMode Mode,
    AttemptStatus Status,
    int CorrectCount,
    int TotalCount,
    decimal ScorePercent,
    bool? Passed,
    int? PassMarkPercent,
    int TimeSpentSeconds,
    IReadOnlyList<TopicResult> Topics,
    TrialOffer? Offer);

/// <summary>
/// The result of a finished attempt (SES-07, stories R-03, G-04, D-73): score, pass or fail, time, and correct answers per
/// topic (they add up to the total). Trial attempts carry an offer to buy the exam or subscribe. Only the attempt's owner
/// (or its guest browser) gets it; an attempt still in progress has no result yet (null).
/// </summary>
public sealed class GetResult(IAppDbContext db, ICurrentUser currentUser, GetPricing pricing)
{
    public async Task<AttemptResult?> HandleAsync(long attemptId, Guid? guestToken, CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.AsNoTracking()
            .Where(a => a.Id == attemptId)
            .Select(a => new
            {
                a.Id,
                a.UserId,
                a.GuestToken,
                a.ExamId,
                Title = a.Exam == null ? null : a.Exam.Title,
                ExamPrice = a.Exam == null ? null : a.Exam.PriceIdr,
                a.Source,
                a.Mode,
                a.Status,
                a.CorrectCount,
                a.TotalCount,
                a.ScorePercent,
                a.Passed,
                a.TimeSpentSeconds,
                a.RulesSnapshot,
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (attempt is null || !AttemptAccess.IsOwner(currentUser, attempt.UserId, attempt.GuestToken, guestToken) || attempt.Status == AttemptStatus.InProgress)
        {
            return null;
        }

        var topics = await db.AttemptItems
            .Where(i => i.AttemptId == attemptId)
            .GroupBy(i => i.Question!.Topic!.Name)
            .Select(g => new { Topic = g.Key, Correct = g.Count(i => i.IsCorrect), Total = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        TrialOffer? offer = null;
        if (attempt.Source == AttemptSource.Trial && attempt.ExamId is { } examId && attempt.ExamPrice is { } price)
        {
            var plans = await pricing.HandleAsync(examId, cancellationToken).ConfigureAwait(false);
            offer = new TrialOffer(examId, price, plans.Packages.Count == 0 ? null : plans.Packages.Min(p => p.MonthlyIdr));
        }

        return new AttemptResult(
            attempt.Id,
            attempt.ExamId,
            attempt.Title ?? string.Empty,
            attempt.Source,
            attempt.Mode,
            attempt.Status,
            attempt.CorrectCount,
            attempt.TotalCount,
            attempt.ScorePercent ?? 0m,
            attempt.Passed,
            PassMark(attempt.RulesSnapshot),
            attempt.TimeSpentSeconds,
            [.. topics.OrderBy(t => t.Topic, StringComparer.CurrentCulture).Select(t => new TopicResult(t.Topic, t.Correct, t.Total))],
            offer);
    }

    /// <summary>Whether the viewer's attempt exists and is still in progress (the result page then sends them to the player).</summary>
    public async Task<bool> IsOpenForViewerAsync(long attemptId, Guid? guestToken, CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.AsNoTracking().Where(a => a.Id == attemptId)
            .Select(a => new { a.UserId, a.GuestToken, a.Status })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return attempt is { Status: AttemptStatus.InProgress } && AttemptAccess.IsOwner(currentUser, attempt.UserId, attempt.GuestToken, guestToken);
    }

    private static int? PassMark(string rulesSnapshot)
    {
        using var rules = JsonDocument.Parse(rulesSnapshot);
        return rules.RootElement.TryGetProperty("passMarkPercent", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
    }
}
