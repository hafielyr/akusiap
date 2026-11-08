using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Application.Catalog;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Sessions;

/// <summary>Correct answers per topic.</summary>
public sealed record TopicResult(string Topic, int Correct, int Total, long TopicId = 0)
{
    public decimal Percent => Total == 0 ? 0m : Math.Round(100m * Correct / Total, 1, MidpointRounding.AwayFromZero);
}

/// <summary>
/// What the result page says about the session (CPNS-05, D-132): the weakest topic to practise next, the pace per
/// question, and how many wrong answers went to the review cards.
/// </summary>
/// <param name="WeakestTopic">The topic with the lowest share right, among topics with at least two questions and a
/// wrong answer; null when every topic went well.</param>
/// <param name="SecondsPerQuestion">Time spent divided by the questions; null when no time was recorded.</param>
/// <param name="TargetSecondsPerQuestion">The real test's pace, for SKD exams (about 54 seconds).</param>
/// <param name="CardsQueued">Wrong answers that became review cards (signed-in learners only).</param>
public sealed record SessionAnalysis(TopicResult? WeakestTopic, int? SecondsPerQuestion, int? TargetSecondsPerQuestion, int CardsQueued);

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
    TrialOffer? Offer,
    SkdScore? Skd = null,
    SessionAnalysis? Analysis = null);

/// <summary>
/// The result of a finished attempt (SES-07, stories R-03, G-04, D-73): score, pass or fail, time, and correct answers per
/// topic (they add up to the total). Trial attempts carry an offer to buy the exam or subscribe. SKD exams add the score
/// per subtest (CPNS-03) and every result gets a short analysis (CPNS-05). Only the attempt's owner (or its guest browser)
/// gets it; an attempt still in progress has no result yet (null).
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
                Scheme = a.Exam == null ? ScoringScheme.Standard : a.Exam.ScoringScheme,
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
            .GroupBy(i => new { i.Question!.TopicId, i.Question.Topic!.Name })
            .Select(g => new { g.Key.TopicId, Topic = g.Key.Name, Correct = g.Count(i => i.IsCorrect), Total = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var topicResults = topics
            .OrderBy(t => t.Topic, StringComparer.CurrentCulture)
            .Select(t => new TopicResult(t.Topic, t.Correct, t.Total, t.TopicId))
            .ToList();

        SkdScore? skd = null;
        if (attempt.Scheme == ScoringScheme.Skd && attempt.Mode != AttemptMode.Flashcard)
        {
            skd = await SkdAsync(attemptId, cancellationToken).ConfigureAwait(false);
        }

        var weakest = topicResults
            .Where(t => t.Total >= 2 && t.Correct < t.Total)
            .OrderBy(t => t.Percent)
            .ThenByDescending(t => t.Total)
            .FirstOrDefault();
        var analysis = new SessionAnalysis(
            weakest,
            attempt.TotalCount == 0 || attempt.TimeSpentSeconds == 0 ? null : attempt.TimeSpentSeconds / attempt.TotalCount,
            skd is null ? null : SkdScoring.SecondsPerQuestion,
            attempt.UserId is null ? 0 : attempt.TotalCount - attempt.CorrectCount);

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
            topicResults,
            offer,
            skd,
            analysis);
    }

    /// <summary>Whether the viewer's attempt exists and is still in progress (the result page then sends them to the player).</summary>
    public async Task<bool> IsOpenForViewerAsync(long attemptId, Guid? guestToken, CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.AsNoTracking().Where(a => a.Id == attemptId)
            .Select(a => new { a.UserId, a.GuestToken, a.Status })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return attempt is { Status: AttemptStatus.InProgress } && AttemptAccess.IsOwner(currentUser, attempt.UserId, attempt.GuestToken, guestToken);
    }

    /// <summary>
    /// The SKD score of an attempt (CPNS-03): each item's subtest comes from its topic name; TKP items with per-option
    /// points score the picked option's points. Items outside the three subtests are left out. Null when none is SKD.
    /// </summary>
    private async Task<SkdScore?> SkdAsync(long attemptId, CancellationToken cancellationToken)
    {
        var items = await db.AttemptItems
            .Where(i => i.AttemptId == attemptId)
            .Select(i => new
            {
                Topic = i.Question!.Topic!.Name,
                i.IsCorrect,
                HasPoints = i.Question.Options.Any(o => o.Points != null),
                Picked = i.SelectedOptions.Max(s => s.QuestionOption!.Points),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var skdItems = items
            .Select(i => (Subtest: SkdScoring.SubtestOf(i.Topic), i.IsCorrect, i.Picked, i.HasPoints))
            .Where(i => i.Subtest is not null)
            .Select(i => new SkdItem(i.Subtest!.Value, i.IsCorrect, i.Picked, i.HasPoints))
            .ToList();
        return skdItems.Count == 0 ? null : SkdScoring.Score(skdItems);
    }

    private static int? PassMark(string rulesSnapshot)
    {
        using var rules = JsonDocument.Parse(rulesSnapshot);
        return rules.RootElement.TryGetProperty("passMarkPercent", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
    }
}
