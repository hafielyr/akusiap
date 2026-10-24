using Akusiap.Application.Abstractions;
using Akusiap.Application.Access;
using Akusiap.Application.Common;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Memorization;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Memorization;

/// <summary>A deck to pick on <c>/learn/kartu</c>: an exam with access, or "Kartu saya" (ExamId null).</summary>
public sealed record FlashcardDeck(long? ExamId, string? Title, int DueCount);

public sealed record FlashcardOption(long Id, string Text, bool IsCorrect);

public sealed record FlashcardCard(
    long AttemptId,
    int Position,
    int Total,
    string? Title,
    QuestionType Type,
    string Text,
    IReadOnlyList<FlashcardOption> Options,
    string Explanation,
    string? KeyFact,
    IReadOnlyList<bool?> Results,
    IReadOnlyList<long> Picked)
{
    /// <summary>Whether this card was already answered or rated in the set (its answer is then shown).</summary>
    public bool IsAnswered => Results[Position - 1] is not null;

    /// <summary>Correct answers in a row, counting back from the last answered card.</summary>
    public int Streak => Results.Where(r => r is not null).Reverse().TakeWhile(r => r == true).Count();
}

/// <summary>The outcome of picking options on a card (MEM-04): right or wrong, and when the card comes back.</summary>
public sealed record FlashcardAnswer(bool IsCorrect, DateTime NextDue);

public sealed record FlashcardSummary(long AttemptId, string? Title, int Total, int Forgot, int Hard, int Good, int Easy);

/// <summary>
/// Flashcard mode (MEM-03, stories M-01, M-02, M-03, M-05, D-82). A set is an Attempt (Mode Flashcard, Source Review,
/// ExamId = the deck's exam or null for "Kartu saya") with up to <see cref="SetSize"/> items: due cards first (oldest
/// due first; wrong answers are due at once, MEM-01), then questions without a card. Cards not yet due wait out their
/// interval (SRS-02, D-87). Only questions the learner can fully use now are offered: an exam deck needs Full, Owner or Admin access, and
/// "Kartu saya" keeps a card only while one of its exams still gives that access (trial access doesn't count), or
/// while it is the learner's own Active question (SRS-01).
/// Rating a card runs <see cref="Sm2Scheduler"/>, writes a <see cref="ReviewLog"/> and moves to the next card; after the
/// last card the set is closed and summarised.
/// </summary>
public sealed class Flashcards(IAppDbContext db, IEntitlementService entitlements, ExamQuestionScope questionScope, ICurrentUser currentUser, IClock clock, Gamification.Badges badges)
{
    public const int SetSize = 10;
    public const string Empty = "Flashcards.Empty";
    public const string NoAccess = "Flashcards.NoAccess";

    /// <summary>The exams the learner can make sets from, plus "Kartu saya", each with its due count.</summary>
    public async Task<IReadOnlyList<FlashcardDeck>> DecksAsync(CancellationToken cancellationToken)
    {
        var userId = UserId();
        var now = clock.UtcNow;
        var exams = await db.Exams.AsNoTracking()
            .Where(e => (e.OwnerKind == OwnerKind.App && e.Status == ExamStatus.Published) || e.OwnerUserId == userId)
            .OrderBy(e => e.Title)
            .Select(e => new { Facts = new ExamAccessFacts(e.Id, e.OwnerUserId, e.OwnerKind, e.Status, e.IsFree, e.IsShared, e.ShareToken, e.Questions.Any(q => q.IsTrial)), e.Title })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var dueByQuestion = await db.ReviewCards.AsNoTracking()
            .Where(c => c.UserId == userId && c.DueAt <= now)
            .Select(c => c.QuestionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var decks = new List<FlashcardDeck>();
        var allowedDue = new HashSet<long>();
        foreach (var exam in exams)
        {
            var decision = await entitlements.DecideAsync(exam.Facts, shareToken: null, cancellationToken).ConfigureAwait(false);
            if (!IsFullAccess(decision))
            {
                continue;
            }

            var ids = await questionScope.GetQuestionIdsAsync(exam.Facts.Id, QuestionScope.All, cancellationToken).ConfigureAwait(false);
            var due = dueByQuestion.Where(ids.Contains).ToList();
            allowedDue.UnionWith(due);
            decks.Add(new FlashcardDeck(exam.Facts.Id, exam.Title, due.Count));
        }

        allowedDue.UnionWith(await OwnActiveAsync(userId, dueByQuestion, cancellationToken).ConfigureAwait(false));
        decks.Insert(0, new FlashcardDeck(null, null, allowedDue.Count));
        return decks;
    }

    /// <summary>Builds a set of up to 10 cards and returns its attempt id.</summary>
    public async Task<Result<long>> StartAsync(long? examId, CancellationToken cancellationToken)
    {
        var userId = UserId();
        var now = clock.UtcNow;
        List<long> pool;
        if (examId is { } id)
        {
            var access = await entitlements.GetAsync(id, shareToken: null, cancellationToken).ConfigureAwait(false);
            if (access is null || !IsFullAccess(access.Decision))
            {
                return Result.Fail<long>(new Problem(NoAccess));
            }

            pool = [.. await questionScope.GetQuestionIdsAsync(id, QuestionScope.All, cancellationToken).ConfigureAwait(false)];
        }
        else
        {
            var carded = await db.ReviewCards.Where(c => c.UserId == userId).Select(c => c.QuestionId).ToListAsync(cancellationToken).ConfigureAwait(false);
            pool = [.. await AllowedAsync(carded, cancellationToken).ConfigureAwait(false)];
        }

        var cards = await db.ReviewCards.AsNoTracking()
            .Where(c => c.UserId == userId && pool.Contains(c.QuestionId))
            .Select(c => new { c.QuestionId, c.DueAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var due = cards.Where(c => c.DueAt <= now).OrderBy(c => c.DueAt).Select(c => c.QuestionId);
        var withCard = cards.Select(c => c.QuestionId).ToHashSet();
        var fresh = pool.Where(q => !withCard.Contains(q));
        var picked = due.Concat(fresh).Distinct().Take(SetSize).ToList();
        if (picked.Count == 0)
        {
            return Result.Fail<long>(new Problem(Empty));
        }

        var attempt = new Attempt
        {
            UserId = userId,
            ExamId = examId,
            Source = AttemptSource.Review,
            Mode = AttemptMode.Flashcard,
            RulesSnapshot = "{\"mode\":\"Flashcard\"}",
            StartedAt = now,
            LastSavedAt = now,
            TotalCount = picked.Count,
        };
        for (var i = 0; i < picked.Count; i++)
        {
            attempt.Items.Add(new AttemptItem { QuestionId = picked[i], Position = i + 1 });
        }

        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(attempt.Id);
    }

    /// <summary>The next card not yet rated, or null when the set is finished (or not the viewer's).</summary>
    public Task<FlashcardCard?> NextCardAsync(long attemptId, CancellationToken cancellationToken) =>
        CardAsync(attemptId, position: null, cancellationToken);

    /// <summary>
    /// The card at <paramref name="position"/>, answered or not, with the options the learner picked: shows the answer
    /// after a wrong pick (MEM-04). Null when the set isn't the viewer's.
    /// </summary>
    public Task<FlashcardCard?> CardAtAsync(long attemptId, int position, CancellationToken cancellationToken) =>
        CardAsync(attemptId, position, cancellationToken);

    /// <summary>
    /// Checks the picked options (MEM-04, D-112): exactly the question's correct options is right and rated Good,
    /// anything else is rated Forgot (SM-2 brings it back the next day). The picks are kept on the item. A card already
    /// rated keeps its rating. Null when the set or card isn't the viewer's.
    /// </summary>
    public async Task<FlashcardAnswer?> AnswerAsync(long attemptId, int position, IReadOnlyCollection<long> optionIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(optionIds);
        var userId = UserId();
        var options = await db.AttemptItems.AsNoTracking()
            .Where(i => i.AttemptId == attemptId && i.Position == position && i.Attempt!.UserId == userId && i.Attempt.Mode == AttemptMode.Flashcard)
            .Select(i => i.Question!.Options.Select(o => new { o.Id, o.IsCorrect }).ToList())
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (options is null)
        {
            return null;
        }

        var picked = optionIds.Where(id => options.Any(o => o.Id == id)).Distinct().ToList();
        var isCorrect = picked.Count > 0 && options.Where(o => o.IsCorrect).Select(o => o.Id).ToHashSet().SetEquals(picked);
        var due = await RateCoreAsync(attemptId, position, isCorrect ? ReviewRating.Good : ReviewRating.Forgot, picked, cancellationToken).ConfigureAwait(false);
        return due is { } nextDue ? new FlashcardAnswer(isCorrect, nextDue) : null;
    }

    /// <summary>
    /// Rates the card at <paramref name="position"/>: creates the review card when the question has none, applies SM-2,
    /// logs the review and marks the item done (Good or Easy count as remembered). Closes the set after the last card.
    /// Rating a card twice changes nothing. Returns the card's next due time (SRS-02), or null when the set isn't the
    /// viewer's.
    /// </summary>
    public Task<DateTime?> RateAsync(long attemptId, int position, ReviewRating rating, CancellationToken cancellationToken) =>
        RateCoreAsync(attemptId, position, rating, picked: [], cancellationToken);

    private async Task<FlashcardCard?> CardAsync(long attemptId, int? position, CancellationToken cancellationToken)
    {
        var set = await OwnSetAsync(attemptId, cancellationToken).ConfigureAwait(false);
        if (set is null || (position is null && set.Status != AttemptStatus.InProgress))
        {
            return null;
        }

        var items = db.AttemptItems.AsNoTracking().Where(i => i.AttemptId == attemptId);
        var chosen = position is { } at ? items.Where(i => i.Position == at) : items.Where(i => !i.IsAnswered).OrderBy(i => i.Position);
        var card = await chosen
            .Select(i => new
            {
                i.Position,
                i.Question!.QuestionType,
                i.Question.Text,
                i.Question.Explanation,
                i.Question.KeyFact,
                Options = i.Question.Options.OrderBy(o => o.SortOrder).ThenBy(o => o.Id).Select(o => new FlashcardOption(o.Id, o.Text, o.IsCorrect)).ToList(),
                Picked = i.SelectedOptions.Select(o => o.QuestionOptionId).ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (card is null)
        {
            return null;
        }

        var results = await items.OrderBy(i => i.Position)
            .Select(i => i.IsAnswered ? (bool?)i.IsCorrect : null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new FlashcardCard(attemptId, card.Position, set.TotalCount, set.Title, card.QuestionType, card.Text, card.Options, card.Explanation, card.KeyFact, results, card.Picked);
    }

    private async Task<DateTime?> RateCoreAsync(long attemptId, int position, ReviewRating rating, IReadOnlyList<long> picked, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(rating))
        {
            return null;
        }

        var userId = UserId();
        var now = clock.UtcNow;
        var attempt = await db.Attempts.Include(a => a.Items)
            .SingleOrDefaultAsync(a => a.Id == attemptId && a.UserId == userId && a.Mode == AttemptMode.Flashcard, cancellationToken)
            .ConfigureAwait(false);
        var item = attempt?.Items.SingleOrDefault(i => i.Position == position);
        if (attempt is null || item is null || (attempt.Status != AttemptStatus.InProgress && !item.IsAnswered))
        {
            return null;
        }

        var card = await db.ReviewCards.SingleOrDefaultAsync(c => c.UserId == userId && c.QuestionId == item.QuestionId, cancellationToken).ConfigureAwait(false);
        if (item.IsAnswered)
        {
            return card?.DueAt;
        }

        if (card is null)
        {
            card = new ReviewCard { UserId = userId, QuestionId = item.QuestionId, State = ReviewCardState.New, DueAt = now };
            db.ReviewCards.Add(card);
        }

        Sm2Scheduler.Apply(card, rating, now);
        item.IsAnswered = true;
        item.IsCorrect = rating is ReviewRating.Good or ReviewRating.Easy;
        item.AnsweredAt = now;
        foreach (var optionId in picked)
        {
            item.SelectedOptions.Add(new AttemptItemOption { QuestionOptionId = optionId });
        }

        attempt.LastSavedAt = now;
        if (attempt.Items.All(i => i.IsAnswered))
        {
            attempt.Status = AttemptStatus.Submitted;
            attempt.SubmittedAt = now;
            attempt.CorrectCount = attempt.Items.Count(i => i.IsCorrect);
            attempt.TotalCount = attempt.Items.Count;
            attempt.ScorePercent = attempt.TotalCount == 0 ? 0m : Math.Round(100m * attempt.CorrectCount / attempt.TotalCount, 2, MidpointRounding.AwayFromZero);
            attempt.TimeSpentSeconds = (int)Math.Max(0, (now - attempt.StartedAt).TotalSeconds);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await badges.AwardAsync(userId, attemptId: null, cancellationToken).ConfigureAwait(false);
        return card.DueAt;
    }

    /// <summary>How the finished set went, from its review logs; null while open or for someone else's set.</summary>
    public async Task<FlashcardSummary?> SummaryAsync(long attemptId, CancellationToken cancellationToken)
    {
        var set = await OwnSetAsync(attemptId, cancellationToken).ConfigureAwait(false);
        if (set is null || set.Status == AttemptStatus.InProgress)
        {
            return null;
        }

        var userId = UserId();
        var questionIds = await db.AttemptItems.Where(i => i.AttemptId == attemptId).Select(i => i.QuestionId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var ratings = await db.ReviewLogs.AsNoTracking()
            .Where(l => l.ReviewCard!.UserId == userId && questionIds.Contains(l.ReviewCard.QuestionId) && l.ReviewedAt >= set.StartedAt && l.ReviewedAt <= set.SubmittedAt)
            .GroupBy(l => l.ReviewCard!.QuestionId)
            .Select(g => g.OrderByDescending(l => l.ReviewedAt).ThenByDescending(l => l.Id).Select(l => l.Rating).First())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new FlashcardSummary(
            attemptId,
            set.Title,
            questionIds.Count,
            ratings.Count(r => r == ReviewRating.Forgot),
            ratings.Count(r => r == ReviewRating.Hard),
            ratings.Count(r => r == ReviewRating.Good),
            ratings.Count(r => r == ReviewRating.Easy));
    }

    /// <summary>The questions among <paramref name="questionIds"/> that one of their exams still gives full access to.</summary>
    private async Task<IReadOnlyList<long>> AllowedAsync(List<long> questionIds, CancellationToken cancellationToken)
    {
        var allowed = (await OwnActiveAsync(UserId(), questionIds, cancellationToken).ConfigureAwait(false)).ToHashSet();
        var links = await db.ExamQuestions.AsNoTracking()
            .Where(eq => questionIds.Contains(eq.QuestionId) && eq.Question!.Status == QuestionStatus.Active)
            .Select(eq => new
            {
                eq.QuestionId,
                Facts = new ExamAccessFacts(eq.Exam!.Id, eq.Exam.OwnerUserId, eq.Exam.OwnerKind, eq.Exam.Status, eq.Exam.IsFree, eq.Exam.IsShared, eq.Exam.ShareToken, eq.Exam.Questions.Any(q => q.IsTrial)),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var decisions = new Dictionary<long, bool>();
        foreach (var link in links)
        {
            if (!decisions.TryGetValue(link.Facts.Id, out var full))
            {
                full = IsFullAccess(await entitlements.DecideAsync(link.Facts, shareToken: null, cancellationToken).ConfigureAwait(false));
                decisions[link.Facts.Id] = full;
            }

            if (full)
            {
                allowed.Add(link.QuestionId);
            }
        }

        return [.. questionIds.Where(allowed.Contains)];
    }

    /// <summary>The learner's own Active questions among <paramref name="questionIds"/> (SRS-01): always theirs to review.</summary>
    private async Task<List<long>> OwnActiveAsync(Guid userId, List<long> questionIds, CancellationToken cancellationToken) =>
        await db.Questions
            .Where(q => questionIds.Contains(q.Id) && q.OwnerUserId == userId && q.Status == QuestionStatus.Active)
            .Select(q => q.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private async Task<SetFacts?> OwnSetAsync(long attemptId, CancellationToken cancellationToken)
    {
        var userId = UserId();
        return await db.Attempts.AsNoTracking()
            .Where(a => a.Id == attemptId && a.UserId == userId && a.Mode == AttemptMode.Flashcard)
            .Select(a => new SetFacts(a.Status, a.TotalCount, a.Exam == null ? null : a.Exam.Title, a.StartedAt, a.SubmittedAt))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool IsFullAccess(AccessDecision decision) =>
        decision.CanStart && decision.Level is AccessLevel.Full or AccessLevel.Owner or AccessLevel.Admin;

    private Guid UserId() => currentUser.UserId ?? throw new InvalidOperationException("Flashcards need a signed-in user.");

    private sealed record SetFacts(AttemptStatus Status, int TotalCount, string? Title, DateTime StartedAt, DateTime? SubmittedAt);
}
