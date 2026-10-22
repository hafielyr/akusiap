using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Sessions;

/// <summary>An option as the player shows it: text only, never whether it is correct.</summary>
public sealed record PlayerOption(long Id, string Text);

/// <param name="Feedback">Set once the item was checked in Practice (SES-05); the answer is then locked.</param>
/// <param name="Hint">Set once the hint was used.</param>
public sealed record PlayerItem(
    int Position,
    long QuestionId,
    QuestionType Type,
    string Text,
    IReadOnlyList<PlayerOption> Options,
    IReadOnlyList<long> Selected,
    bool IsFlagged,
    bool HasHint = false,
    string? Hint = null,
    AnswerFeedback? Feedback = null);

/// <param name="SecondsLeft">Null when untimed; never negative.</param>
public sealed record PlayerView(
    long AttemptId,
    long? ExamId,
    string Title,
    AttemptMode Mode,
    AttemptSource Source,
    AttemptStatus Status,
    bool HintsEnabled,
    int? TimeLimitMinutes,
    int? SecondsLeft,
    IReadOnlyList<PlayerItem> Items)
{
    /// <summary>Show the full-screen CAT layout (TRY-01): a Simulation whose rules ask for it.</summary>
    public bool Cat { get; init; }

    /// <summary>The result once the attempt is closed (SES-06); the full result page is SES-07.</summary>
    public int CorrectCount { get; init; }

    public int TotalCount { get; init; }

    public decimal? ScorePercent { get; init; }

    /// <summary>"Periksa" is offered (Practice with answers after each question).</summary>
    public bool CanCheck { get; init; }
}

/// <summary>
/// The quiz player's data (SES-03, D-56). Only the attempt's owner may load it: the signed-in user it belongs to, or, for
/// guest attempts, the browser holding its guest token. Everyone else gets null (the page answers 404, revealing nothing).
/// Options come in the attempt's stored order; correct answers are never included.
/// </summary>
public sealed class GetPlayer(IAppDbContext db, ICurrentUser currentUser, IClock clock)
{
    public async Task<PlayerView?> HandleAsync(long attemptId, Guid? guestToken, CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts
            .AsNoTracking()
            .Where(a => a.Id == attemptId)
            .Select(a => new
            {
                a.Id,
                a.UserId,
                a.GuestToken,
                a.ExamId,
                Title = a.Exam == null ? null : a.Exam.Title,
                a.Mode,
                a.Source,
                a.Status,
                a.RulesSnapshot,
                a.StartedAt,
                a.CorrectCount,
                a.TotalCount,
                a.ScorePercent,
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (attempt is null || !AttemptAccess.IsOwner(currentUser, attempt.UserId, attempt.GuestToken, guestToken))
        {
            return null;
        }

        var items = await db.AttemptItems
            .AsNoTracking()
            .Where(i => i.AttemptId == attemptId)
            .OrderBy(i => i.Position)
            .Select(i => new
            {
                i.Position,
                i.QuestionId,
                i.Question!.QuestionType,
                i.Question.Text,
                i.OptionOrder,
                i.IsFlagged,
                i.CheckedAt,
                i.HintUsed,
                i.Question.Hint,
                i.Question.Explanation,
                i.Question.KeyFact,
                Correct = i.Question.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList(),
                Options = i.Question.Options.Select(o => new PlayerOption(o.Id, o.Text)).ToList(),
                Selected = i.SelectedOptions.Select(s => s.QuestionOptionId).ToList(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var practice = PracticeRules.From(attempt.RulesSnapshot);
        using var rules = JsonDocument.Parse(attempt.RulesSnapshot);
        var hints = rules.RootElement.TryGetProperty("hintsEnabled", out var h) && h.ValueKind == JsonValueKind.True;
        int? timeLimit = rules.RootElement.TryGetProperty("timeLimitMinutes", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : null;
        int? secondsLeft = timeLimit is { } minutes
            ? (int)Math.Max(0, Math.Floor((attempt.StartedAt.AddMinutes(minutes) - clock.UtcNow).TotalSeconds))
            : null;

        return new PlayerView(
            attempt.Id,
            attempt.ExamId,
            attempt.Title ?? string.Empty,
            attempt.Mode,
            attempt.Source,
            attempt.Status,
            hints,
            timeLimit,
            secondsLeft,
            [.. items.Select(i => new PlayerItem(
                i.Position,
                i.QuestionId,
                i.QuestionType,
                i.Text,
                Ordered(i.Options, i.OptionOrder),
                i.Selected,
                i.IsFlagged,
                practice.HintsEnabled && !string.IsNullOrWhiteSpace(i.Hint),
                i.HintUsed ? i.Hint : null,
                i.CheckedAt is null ? null : new AnswerFeedback(Scoring.IsCorrect(i.QuestionType, i.Correct, i.Selected), i.Correct, i.Explanation, i.KeyFact)))])
        {
            CanCheck = practice.CanCheck,
            Cat = attempt.Mode == AttemptMode.Simulation && rules.RootElement.TryGetProperty("catMode", out var cat) && cat.ValueKind == JsonValueKind.True,
            CorrectCount = attempt.CorrectCount,
            TotalCount = attempt.TotalCount,
            ScorePercent = attempt.ScorePercent,
        };
    }

    /// <summary>Options in the stored order; any option missing from it (added later) goes last.</summary>
    private static List<PlayerOption> Ordered(List<PlayerOption> options, string? optionOrder)
    {
        var order = string.IsNullOrEmpty(optionOrder) ? [] : JsonSerializer.Deserialize<List<long>>(optionOrder) ?? [];
        return [.. options.OrderBy(o => order.IndexOf(o.Id) is var i and >= 0 ? i : int.MaxValue).ThenBy(o => o.Id)];
    }
}
